using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Shared;

public sealed class OutboxRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Queue { get; set; } = "";
    public string Payload { get; set; } = "";
    public string Identity { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AvailableAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public Guid? LeaseId { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public static OutboxRow Create<T>(string queue, string identity, T value) => new() { Queue = queue, Identity = identity, Payload = JsonSerializer.Serialize(value, Contracts.Json) };
}
public sealed class FailedEnvelope
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Identity { get; set; } = "";
    public string Payload { get; set; } = "";
    public string Queue { get; set; } = "";
    public int Attempts { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset FailedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReplayedAt { get; set; }
}
public abstract class MessagingDb(DbContextOptions options) : DbContext(options)
{
    public DbSet<OutboxRow> Outbox => Set<OutboxRow>();
    public DbSet<FailedEnvelope> Failures => Set<FailedEnvelope>();
    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<OutboxRow>().ToTable("outbox");
        m.Entity<OutboxRow>().HasIndex(x => new { x.PublishedAt, x.AvailableAt });
        m.Entity<FailedEnvelope>().ToTable("failed_envelopes");
        m.Entity<FailedEnvelope>().HasIndex(x => x.Identity).IsUnique();
    }
}
public sealed class Broker(IConfiguration configuration)
{
    public async Task<IConnection> Connect(CancellationToken ct) => await new ConnectionFactory { Uri = new Uri(configuration["RABBITMQ_URL"] ?? "amqp://lms:lms@localhost:5672"), AutomaticRecoveryEnabled = true, ConsumerDispatchConcurrency = 1 }.CreateConnectionAsync(ct);
    public static async Task Topology(IChannel channel, string queue, CancellationToken ct)
    {
        await channel.QueueDeclareAsync(queue, true, false, false, cancellationToken: ct);
        await channel.QueueDeclareAsync(queue + ".failed", true, false, false, cancellationToken: ct);
        foreach (var delay in new[] { 5000, 15000, 60000 })
            await channel.QueueDeclareAsync(queue + ".retry." + delay, true, false, false, new Dictionary<string, object?> { ["x-message-ttl"] = delay, ["x-dead-letter-exchange"] = "", ["x-dead-letter-routing-key"] = queue }, cancellationToken: ct);
    }
    public static async Task Publish(IChannel channel, string queue, string identity, ReadOnlyMemory<byte> payload, int attempt, string? error, CancellationToken ct)
    {
        var props = new BasicProperties { Persistent = true, ContentType = "application/json", MessageId = identity, Headers = new Dictionary<string, object?> { ["attempt"] = attempt, ["error"] = error ?? "" } };
        await channel.BasicPublishAsync("", queue, true, props, payload, ct);
    }
}
public sealed class OutboxPublisher<T>(IServiceScopeFactory scopes, Broker broker, ILogger<OutboxPublisher<T>> log) : BackgroundService where T : MessagingDb
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var connection = await broker.Connect(ct);
                await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true), ct);
                await Broker.Topology(channel, Contracts.PaymentsQueue, ct); await Broker.Topology(channel, Contracts.ResultsQueue, ct);
                while (!ct.IsCancellationRequested)
                {
                    await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<T>();
                    var lease = Guid.NewGuid();
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox SET \"LeaseId\"={lease}, \"LeaseUntil\"=now()+interval '30 seconds' WHERE \"Id\" IN (SELECT \"Id\" FROM outbox WHERE \"PublishedAt\" IS NULL AND \"AvailableAt\" <= now() AND (\"LeaseUntil\" IS NULL OR \"LeaseUntil\" < now()) ORDER BY \"CreatedAt\" LIMIT 20 FOR UPDATE SKIP LOCKED)", ct);
                    var rows = await db.Outbox.Where(x => x.LeaseId == lease).OrderBy(x => x.CreatedAt).ToListAsync(ct);
                    foreach (var row in rows)
                    {
                        try { await Broker.Publish(channel, row.Queue, row.Identity, Encoding.UTF8.GetBytes(row.Payload), 0, null, ct); row.PublishedAt = DateTimeOffset.UtcNow; row.LastError = null; }
                        catch (Exception e) when (!ct.IsCancellationRequested) { row.Attempts++; row.LastError = e.GetType().Name; row.AvailableAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(60, Math.Pow(2, Math.Min(row.Attempts, 6))) + Random.Shared.NextDouble()); }
                        row.LeaseId = null; row.LeaseUntil = null;
                        await db.SaveChangesAsync(ct);
                    }
                    if (rows.Count == 0) await Task.Delay(300, ct);
                }
            }
            catch (Exception e) when (!ct.IsCancellationRequested) { log.LogWarning("Outbox dependency unavailable: {Kind}", e.GetType().Name); await Task.Delay(3000, ct); }
        }
    }
}
public abstract class QueueConsumer<T>(IServiceScopeFactory scopes, Broker broker, ILogger log) : BackgroundService where T : MessagingDb
{
    protected abstract string Queue { get; }
    protected abstract Task Process(IServiceProvider services, string payload, CancellationToken ct);
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var connection = await broker.Connect(ct);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
                await using var publisher = await connection.CreateChannelAsync(new CreateChannelOptions(true, true), ct);
                await Broker.Topology(channel, Queue, ct); await channel.BasicQosAsync(0, 8, false, ct);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, delivery) =>
                {
                    var payload = Encoding.UTF8.GetString(delivery.Body.Span);
                    var attempt = delivery.BasicProperties.Headers?.TryGetValue("attempt", out var a) == true ? Convert.ToInt32(a) : 0;
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        await Process(scope.ServiceProvider, payload, ct);
                        await channel.BasicAckAsync(delivery.DeliveryTag, false, ct);
                    }
                    catch (Exception e) when (!ct.IsCancellationRequested)
                    {
                        log.LogWarning("Message {Identity} processing attempt {Attempt} failed: {Kind}", delivery.BasicProperties.MessageId, attempt + 1, e.GetType().Name);
                        var delay = new[] { 5000, 15000, 60000 };
                        var destination = attempt < delay.Length ? Queue + ".retry." + delay[attempt] : Queue + ".failed";
                        try { await Broker.Publish(publisher, destination, delivery.BasicProperties.MessageId ?? Guid.NewGuid().ToString(), Encoding.UTF8.GetBytes(payload), attempt + 1, e.GetType().Name, ct); await channel.BasicAckAsync(delivery.DeliveryTag, false, ct); }
                        catch { await channel.BasicNackAsync(delivery.DeliveryTag, false, true, ct); }
                    }
                };
                var failures = new AsyncEventingBasicConsumer(channel);
                failures.ReceivedAsync += async (_, d) =>
                {
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<T>();
                        var identity = d.BasicProperties.MessageId ?? Guid.NewGuid().ToString();
                        var attempts = d.BasicProperties.Headers?.TryGetValue("attempt", out var count) == true ? Convert.ToInt32(count) : 4;
                        var lastError = d.BasicProperties.Headers?.TryGetValue("error", out var error) == true ? error is byte[] bytes ? Encoding.UTF8.GetString(bytes) : Convert.ToString(error) : "Unknown processing failure";
                        var existing = await db.Failures.SingleOrDefaultAsync(x => x.Identity == identity, ct);
                        if (existing is null)
                        {
                            db.Failures.Add(new() { Identity = identity, Queue = Queue, Payload = Encoding.UTF8.GetString(d.Body.Span), Attempts = attempts, Reason = "Processing retry budget exhausted: " + lastError });
                        }
                        else { existing.FailedAt = DateTimeOffset.UtcNow; existing.Attempts += attempts; existing.ReplayedAt = null; existing.Reason = "Processing retry budget exhausted: " + lastError; }
                        await db.SaveChangesAsync(ct);
                        await channel.BasicAckAsync(d.DeliveryTag, false, ct);
                    }
                    catch when (!ct.IsCancellationRequested) { await Task.Delay(1000, ct); await channel.BasicNackAsync(d.DeliveryTag, false, true, ct); }
                };
                await channel.BasicConsumeAsync(Queue, false, consumer, ct); await channel.BasicConsumeAsync(Queue + ".failed", false, failures, ct);
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested) { log.LogWarning("Consumer dependency unavailable: {Kind}", e.GetType().Name); await Task.Delay(3000, ct); }
        }
    }
}
