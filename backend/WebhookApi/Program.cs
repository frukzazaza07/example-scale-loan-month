using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shared;
using WebhookApi;

var builder = WebApplication.CreateBuilder(args.Where(x => x is not ("--migrate" or "--seed")).ToArray()); builder.AddDemo("WebhookApi");
_ = DemoAuth.Secret(builder.Configuration, "BANK_SECRET"); _ = DemoAuth.Secret(builder.Configuration, "AUTH_SECRET");
builder.Services.AddDbContext<ReceiptDb>(o => o.UseNpgsql(Hosting.Connection(builder.Configuration)));
builder.Services.AddSingleton<Broker>(); builder.Services.AddHostedService<OutboxPublisher<ReceiptDb>>();
var app = builder.Build(); if (await Hosting.MigrateIfRequested<ReceiptDb>(app, args)) return;
app.UseDemo(); app.Readiness<ReceiptDb>();
app.MapPost("/webhooks/bank/payments", async (HttpContext ctx, ReceiptDb db, IConfiguration config, CancellationToken ct) =>
{
    if (ctx.Request.ContentLength > 8192) return Results.Problem("Payload exceeds 8 KiB", statusCode: 413);
    using var ms = new MemoryStream(); var buffer = new byte[8193]; var total = 0;
    while (total < buffer.Length) { var read = await ctx.Request.Body.ReadAsync(buffer.AsMemory(total), ct); if (read == 0) break; total += read; }
    if (total > 8192) return Results.Problem("Payload exceeds 8 KiB", statusCode: 413);
    var raw = buffer.AsSpan(0, total).ToArray();
    var timestamp = ctx.Request.Headers["X-Bank-Timestamp"].ToString();
    var signature = ctx.Request.Headers["X-Bank-Signature"].ToString();
    bool signed = false;
    if (long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && Math.Abs((decimal)DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds) <= 300)
    {
        var bytes = Encoding.UTF8.GetBytes(timestamp + ".").Concat(raw).ToArray();
        try { signed = CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature), HMACSHA256.HashData(Encoding.UTF8.GetBytes(DemoAuth.Secret(config, "BANK_SECRET")), bytes)); } catch (FormatException) { }
    }
    if (!signed) return Results.Problem("Invalid signature or delivery timestamp", statusCode: 401);
    BankPayment? payment;
    try { payment = JsonSerializer.Deserialize<BankPayment>(raw, Contracts.Json); } catch (JsonException) { payment = null; }
    if (payment is null || !Contracts.Valid(payment))
    {
        try { db.ValidationFailures.Add(new() { Reason = "Signed payload failed bank contract validation" }); await db.SaveChangesAsync(ct); } catch { return Results.StatusCode(503); }
        return Results.Problem("Invalid bank payment contract", statusCode: 400);
    }
    try
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"demo-bank:" + payment.EventId},0))", ct);
        var existing = await db.Receipts.SingleOrDefaultAsync(x => x.BankId == "demo-bank" && x.EventId == payment.EventId, ct);
        if (existing is not null)
        {
            existing.Attempts++;
            if (existing.Fingerprint != Contracts.Fingerprint(payment)) { existing.Status = "Conflict"; existing.Reason = "Event identity reused with different immutable fields"; }
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return existing.Status == "Conflict" ? Results.Conflict(new { existing.Id, existing.Status, existing.Reason }) : Results.Ok(new { existing.Id, status = "Duplicate", financialStatus = "Query LoanService" });
        }
        var receipt = new Receipt { EventId = payment.EventId, BankTransactionId = payment.BankTransactionId, PaymentReference = payment.PaymentReference, AmountMinor = Contracts.Money(long.Parse(payment.AmountMinor, CultureInfo.InvariantCulture)), BankOutcome = payment.Status, Fingerprint = Contracts.Fingerprint(payment), PaidAt = payment.PaidAt.ToUniversalTime(), CorrelationId = (string)ctx.Items["CorrelationId"]! };
        db.Receipts.Add(receipt);
        db.Outbox.Add(OutboxRow.Create(Contracts.PaymentsQueue, $"demo-bank:{payment.EventId}", new PaymentReceived(payment.EventId, payment.BankTransactionId, "demo-bank", DateTimeOffset.UtcNow, 1, receipt.CorrelationId, Activity.Current?.Id, payment, receipt.ReceivedAt,payment.RunId)));
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return Results.Ok(new { receipt.Id, receipt.Status, financialStatus = "Pending" });
    }
    catch (Exception e) when (e is Npgsql.NpgsqlException or DbUpdateException) { ctx.Response.Headers.RetryAfter = "5"; return Results.Problem("Durable receipt unavailable; retry delivery", statusCode: 503); }
});
var api = app.MapGroup("/api/v1").RequireAuthorization();
api.MapGet("/transactions", async (ReceiptDb db, int? page, int? pageSize, CancellationToken ct) => {
    var size = Math.Clamp(pageSize ?? 50,1,100);
    var groups = db.Receipts.AsNoTracking().GroupBy(r => r.BankTransactionId);
    var total = await groups.CountAsync(ct);
    var identities = await groups.Select(g => new { bankTransactionId = g.Key, receivedAt = g.Min(r => r.ReceivedAt) }).OrderByDescending(r => r.receivedAt).Skip((Math.Max(1,page ?? 1)-1)*size).Take(size).ToListAsync(ct);
    var ids = identities.Select(x => x.bankTransactionId).ToArray();
    var receipts = await db.Receipts.AsNoTracking().Where(x => ids.Contains(x.BankTransactionId)).ToListAsync(ct);
    var events = receipts.Select(x => x.BankId+":"+x.EventId).ToArray(); var outbox = await db.Outbox.AsNoTracking().Where(x => events.Contains(x.Identity)).ToListAsync(ct);
    return new { total, items = identities.Select(identity => { var rows = receipts.Where(r => r.BankTransactionId == identity.bankTransactionId).OrderBy(r => r.ReceivedAt).ToList();var r = rows[0];var o = outbox.SingleOrDefault(x => x.Identity == r.BankId+":"+r.EventId);return new { r.Id,r.EventId,r.BankTransactionId,r.PaymentReference,r.AmountMinor,r.BankOutcome,r.PaidAt,r.ReceivedAt,attempts = rows.Sum(x => x.Attempts),status = rows.Any(x => x.Status == "Conflict") ? "Conflict" : r.Status,r.Reason,r.CorrelationId,outbox = o is null ? null : new { o.CreatedAt,o.PublishedAt,o.Attempts,o.LastError } }; }) };
});
api.MapGet("/receipts", async (ReceiptDb db, string? transactionId, string? status, int? page, int? pageSize, CancellationToken ct) =>
{
    var q = db.Receipts.AsNoTracking(); if (transactionId is not null) q = q.Where(x => x.BankTransactionId == transactionId); if (status is not null) q = q.Where(x => x.Status == status);
    return Results.Ok(new { total = await q.CountAsync(ct), items = await q.OrderByDescending(x => x.ReceivedAt).Skip((Math.Max(1, page ?? 1)-1)*Math.Clamp(pageSize ?? 50,1,200)).Take(Math.Clamp(pageSize ?? 50,1,200)).Select(x => new { x.Id, x.EventId, x.BankTransactionId, x.PaymentReference, x.AmountMinor, x.BankOutcome, x.PaidAt, x.ReceivedAt, x.Attempts, x.Status, x.Reason, x.CorrelationId, outbox = db.Outbox.Where(o => o.Identity == x.BankId + ":" + x.EventId).Select(o => new { o.PublishedAt, o.CreatedAt, o.Attempts, o.LastError }).FirstOrDefault() }).ToListAsync(ct) });
});
api.MapGet("/failures", async (ReceiptDb db, CancellationToken ct) => new { conflicts = await db.Receipts.Where(x => x.Status == "Conflict").OrderByDescending(x => x.ReceivedAt).Take(100).ToListAsync(ct), validation = await db.ValidationFailures.OrderByDescending(x => x.At).Take(100).ToListAsync(ct) });
api.MapGet("/settlement", async (ReceiptDb db, string[] transactionId, CancellationToken ct) => await db.Receipts.AsNoTracking().Where(x => x.BankId == "demo-bank" && transactionId.Contains(x.BankTransactionId)).Select(x => new { x.BankTransactionId, x.ReceivedAt, x.AmountMinor }).ToListAsync(ct));
api.MapGet("/summary", async (ReceiptDb db, CancellationToken ct) => new { receipts = await db.Receipts.CountAsync(ct), attempts = await db.Receipts.SumAsync(x => (int?)x.Attempts,ct) ?? 0, pendingOutbox = await db.Outbox.CountAsync(x => x.PublishedAt == null,ct), oldestPendingAt = await db.Outbox.Where(x => x.PublishedAt == null).MinAsync(x => (DateTimeOffset?)x.CreatedAt, ct), refreshedAt = DateTimeOffset.UtcNow });
app.Run();
public partial class Program;

