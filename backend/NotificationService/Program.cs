using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shared;
using NotificationService;

var builder = WebApplication.CreateBuilder(args.Where(x => x is not ("--migrate" or "--seed")).ToArray()); builder.AddDemo("NotificationService"); _ = DemoAuth.Secret(builder.Configuration,"AUTH_SECRET");
builder.Services.AddDbContext<NotificationDb>(o => o.UseNpgsql(Hosting.Connection(builder.Configuration)));
builder.Services.AddSingleton<Broker>(); builder.Services.AddHostedService<NotificationConsumer>();
var app = builder.Build(); if (await Hosting.MigrateIfRequested<NotificationDb>(app,args)) return;
app.UseDemo(); app.Readiness<NotificationDb>();
app.MapGet("/api/v1/notifications", async (NotificationDb db, string? transactionId, CancellationToken ct) => await db.Notifications.Where(x => transactionId == null || x.BankTransactionId == transactionId).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct)).RequireAuthorization();
app.Run();
namespace NotificationService
{
    public sealed class Notification
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string EventId { get; set; } = "";
        public string BankTransactionId { get; set; } = "";
        public string PaymentReference { get; set; } = "";
        public string Message { get; set; } = "";
        public string Status { get; set; } = "Created";
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
    public sealed class NotificationDb(DbContextOptions<NotificationDb> options) : MessagingDb(options)
    {
        public DbSet<Notification> Notifications => Set<Notification>();
        protected override void OnModelCreating(ModelBuilder m) { base.OnModelCreating(m); m.Entity<Notification>().ToTable("notifications"); m.Entity<Notification>().HasIndex(x => x.EventId).IsUnique(); }
    }
    public sealed class NotificationConsumer(IServiceScopeFactory scopes, Broker broker, ILogger<NotificationConsumer> log) : QueueConsumer<NotificationDb>(scopes,broker,log)
    {
        protected override string Queue => Contracts.ResultsQueue;
        protected override async Task Process(IServiceProvider services, string payload, CancellationToken ct)
        {
            var e = JsonSerializer.Deserialize<PaymentResult>(payload,Contracts.Json) ?? throw new ArgumentException("Invalid result");
            var db = services.GetRequiredService<NotificationDb>(); await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({e.EventId},0))",ct);
            if (!await db.Notifications.AnyAsync(x => x.EventId == e.EventId,ct)) db.Notifications.Add(new() { EventId = e.EventId, BankTransactionId = e.BankTransactionId, PaymentReference = e.PaymentReference, Message = $"Payment {e.BankTransactionId}: {e.Status}. Simulated THB only." });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
    }
}

