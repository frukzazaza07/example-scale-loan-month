using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shared;
using LoanService;

var builder = WebApplication.CreateBuilder(args.Where(x => x is not ("--migrate" or "--seed")).ToArray()); builder.AddDemo("LoanService"); _ = DemoAuth.Secret(builder.Configuration, "AUTH_SECRET");
builder.Services.AddDbContext<LoanDb>(o => o.UseNpgsql(Hosting.Connection(builder.Configuration)));
builder.Services.AddSingleton<Broker>(); builder.Services.AddScoped<PaymentProcessor>();
var mode = builder.Configuration["SERVICE_MODE"] ?? "all";
if (mode is "worker" or "all") { builder.Services.AddHostedService<LoanConsumer>(); builder.Services.AddHostedService<OutboxPublisher<LoanDb>>(); }
var app = builder.Build(); if (await Hosting.MigrateIfRequested<LoanDb>(app, args)) return;
if (args.Contains("--seed"))
{
    await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<LoanDb>();
    var count = int.TryParse(builder.Configuration["SEED_COUNT"], out var n) ? Math.Clamp(n,1,100000) : 1000;
    await using var tx = await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(72601)");
    var refs = await db.Loans.Select(x => x.Reference).ToHashSetAsync();
    for (var i = 0; i < count; i++) { var reference = $"LOAN-{1001+i}"; if (refs.Contains(reference)) continue; db.Loans.Add(new() { Reference = reference, Customer = $"Fictional Customer {i+1:0000}", OpeningPrincipal = 10_000_000, OutstandingPrincipal = 10_000_000, AnnualRate = 0.12m, MonthlyPayment = 900_000, Installments = Finance.Schedule(10_000_000,0.12m,900_000,Finance.DemoDate) }); }
    await db.SaveChangesAsync(); await tx.CommitAsync(); return;
}
app.UseDemo(); app.Readiness<LoanDb>();
var api = app.MapGroup("/api/v1").RequireAuthorization();
api.MapGet("/run-application/{runId:guid}",async(Guid runId,LoanDb db,CancellationToken ct) => {
    // The transaction prefix keeps pre-runId demo settlements readable without altering posted records.
    var prefix="tx-"+runId.ToString("N")+"-";var context="\"runId\":\""+runId+"\"";
    var counts=await db.Payments.AsNoTracking().Where(x=>x.BankTransactionId.StartsWith(prefix)||x.Envelope.Contains(context)).GroupBy(x=>x.Status).Select(g=>new {status=g.Key,count=g.Count()}).ToDictionaryAsync(x=>x.status,x=>x.count,ct);
    return new {runId,processedUnique=counts.Values.Sum(),appliedUnique=counts.GetValueOrDefault("Applied")+counts.GetValueOrDefault("AppliedWithCredit"),creditReview=counts.GetValueOrDefault("AppliedWithCredit"),unmatched=counts.GetValueOrDefault("Unmatched"),bankDeclined=counts.GetValueOrDefault("BankDeclined"),refreshedAt=DateTimeOffset.UtcNow,source="LoanService processing records"};
});
api.MapGet("/loans", async (LoanDb db, string? search, int? page, int? pageSize, CancellationToken ct) =>
{
    var q = db.Loans.AsNoTracking(); if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Reference.Contains(search) || x.Customer.Contains(search));
    var total = await q.CountAsync(ct); var loans = await q.Include(x => x.Installments).OrderBy(x => x.Id).Skip((Math.Max(1,page ?? 1)-1)*Math.Clamp(pageSize ?? 50,1,200)).Take(Math.Clamp(pageSize ?? 50,1,200)).ToListAsync(ct);
    return new { total, items = loans.Select(LoanView), businessDate = Finance.DemoDate };
});
api.MapGet("/loans/{reference}", async (string reference, LoanDb db, CancellationToken ct) =>
{
    var loan = await db.Loans.AsNoTracking().Include(x => x.Installments).SingleOrDefaultAsync(x => x.Reference == reference,ct);
    if (loan is null) return Results.NotFound();
    var payments = await db.Payments.Include(x => x.Allocations).Where(x => x.LoanId == loan.Id).OrderByDescending(x => x.ReceivedAt).ToListAsync(ct);
    return Results.Ok(new { loan = LoanView(loan), installments = loan.Installments.OrderBy(x => x.Number), payments = payments.Select(PaymentView), journals = await db.Journals.Include(x => x.Postings).Where(x => x.LoanId == loan.Id).OrderBy(x => x.PostedAt).ToListAsync(ct) });
});
api.MapGet("/payments", async (LoanDb db, string? status, string? transactionId, int? page, int? pageSize, CancellationToken ct) =>
{
    var q = db.Payments.AsNoTracking(); if (status is not null) q = q.Where(x => x.Status == status); if (transactionId is not null) q = q.Where(x => x.BankTransactionId == transactionId);
    var total = await q.CountAsync(ct); var items = await q.Include(x => x.Allocations).OrderByDescending(x => x.ReceivedAt).Skip((Math.Max(1,page ?? 1)-1)*Math.Clamp(pageSize ?? 50,1,200)).Take(Math.Clamp(pageSize ?? 50,1,200)).ToListAsync(ct);
    return new { total, items = items.Select(PaymentView) };
});
api.MapGet("/payments/{transactionId}", async (string transactionId, LoanDb db, CancellationToken ct) =>
{
    var p = await db.Payments.AsNoTracking().Include(x => x.Allocations).SingleOrDefaultAsync(x => x.BankId == "demo-bank" && x.BankTransactionId == transactionId,ct);
    return p is null ? Results.NotFound() : Results.Ok(new { payment = PaymentView(p), journals = await db.Journals.Include(x => x.Postings).Where(x => x.PaymentId == p.Id).ToListAsync(ct), outbox = await db.Outbox.Where(x => x.Identity.StartsWith("result:" + p.Id)).Select(x => new { x.CreatedAt, x.PublishedAt, x.Attempts, x.LastError }).ToListAsync(ct) });
});
api.MapGet("/payment-batch", async (LoanDb db, string[] transactionId, CancellationToken ct) => (await db.Payments.AsNoTracking().Include(x => x.Allocations).Where(x => x.BankId == "demo-bank" && transactionId.Contains(x.BankTransactionId)).ToListAsync(ct)).Select(PaymentView));
api.MapGet("/settlement", async (LoanDb db, string[] transactionId, CancellationToken ct) => await db.Payments.AsNoTracking().Where(x => x.BankId == "demo-bank" && transactionId.Contains(x.BankTransactionId)).Select(x => new { x.BankTransactionId, amountMinor = Contracts.Money(x.AmountMinor), x.Status, x.AppliedAt }).ToListAsync(ct));
api.MapGet("/summary", async (LoanDb db, CancellationToken ct) =>
{
    var date = Finance.DemoDate; var today = new DateTimeOffset(DateTime.SpecifyKind(DateTimeOffset.UtcNow.Date,DateTimeKind.Utc));
    return new { activeLoans = await db.Loans.CountAsync(x => x.OutstandingPrincipal > 0,ct), outstandingPrincipal = Contracts.Money(await db.Loans.SumAsync(x => (long?)x.OutstandingPrincipal,ct) ?? 0), dueAmount = Contracts.Money(await db.Installments.Where(x => x.DueDate <= date).SumAsync(x => (long?)(x.Interest+x.Principal-x.PaidInterest-x.PaidPrincipal),ct) ?? 0), appliedPaymentsToday = await db.Payments.CountAsync(x => x.AppliedAt >= today,ct), reviewCount = await db.Payments.CountAsync(x => x.Status == "Unmatched" || x.Status == "AppliedWithCredit" || x.LateReview,ct) + await db.Conflicts.CountAsync(ct), businessDate = date, cutoff = DateTimeOffset.UtcNow, consistency = "Eventual: durable receipts can precede allocation" };
});
api.MapGet("/failures", async (LoanDb db, CancellationToken ct) => new { reviews = (await db.Payments.AsNoTracking().Include(x => x.Allocations).Where(x => x.Status == "Unmatched" || x.Status == "AppliedWithCredit" || x.LateReview).OrderByDescending(x => x.ReceivedAt).Take(100).ToListAsync(ct)).Select(PaymentView), conflicts = await db.Conflicts.OrderByDescending(x => x.At).Take(100).ToListAsync(ct), failedMessages = await db.Failures.Select(x => new { x.Id, x.Identity, x.Attempts, x.Reason, x.FailedAt, x.ReplayedAt }).ToListAsync(ct), audit = await db.Audits.OrderByDescending(x => x.At).Take(100).ToListAsync(ct) });
api.MapPost("/payments/{transactionId}/resolve", async (string transactionId, ResolveRequest request, LoanDb db, HttpContext ctx, CancellationToken ct) =>
{
    await using var tx = await db.Database.BeginTransactionAsync(ct); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"demo-bank:"+transactionId},0))",ct);
    var p = await db.Payments.SingleOrDefaultAsync(x => x.BankId == "demo-bank" && x.BankTransactionId == transactionId,ct);
    if (p is null) return Results.NotFound(); if (p.Status != "Unmatched") return Results.Conflict(new { reason = "Only unmatched payments can be resolved" });
    if (!await db.Loans.AnyAsync(x => x.Reference == request.Reference,ct)) return Results.Problem("Target loan does not exist",statusCode:400);
    p.ResolvedReference = request.Reference; db.Audits.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "ResolveReference", Identity = transactionId, Details = p.OriginalReference + " → " + request.Reference }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    return Results.Ok(new { p.Status, p.OriginalReference, p.ResolvedReference });
}).RequireAuthorization("Admin");
api.MapPost("/payments/{transactionId}/replay", async (string transactionId, LoanDb db, PaymentProcessor processor, HttpContext ctx, CancellationToken ct) =>
{
    var p = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.BankId == "demo-bank" && x.BankTransactionId == transactionId,ct); if (p is null) return Results.NotFound();
    var result = await processor.Apply(JsonSerializer.Deserialize<PaymentReceived>(p.Envelope,Contracts.Json)!,true,ct,ctx.User.Identity!.Name!);
    return Results.Ok(PaymentView(result));
}).RequireAuthorization("Admin");
api.MapPost("/failures/{id:guid}/replay", async (Guid id, LoanDb db, HttpContext ctx, CancellationToken ct) =>
{
    await using var tx = await db.Database.BeginTransactionAsync(ct); var f = await db.Failures.FromSqlInterpolated($"SELECT * FROM failed_envelopes WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ct); if (f is null) return Results.NotFound();
    if (f.ReplayedAt is not null) return Results.Ok(new { status = "AlreadyQueued" });
    db.Outbox.Add(new() { Queue = f.Queue, Identity = f.Identity, Payload = f.Payload }); f.ReplayedAt = DateTimeOffset.UtcNow;
    db.Audits.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "ReplayFailedEnvelope", Identity = f.Identity }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Ok(new { status = "Queued" });
}).RequireAuthorization("Admin");
app.Run();
static object LoanView(Loan x) => new { x.Id, x.Reference, x.Customer, openingPrincipal = Contracts.Money(x.OpeningPrincipal), outstandingPrincipal = Contracts.Money(x.OutstandingPrincipal), paidPrincipal = Contracts.Money(x.OpeningPrincipal-x.OutstandingPrincipal), x.AnnualRate, monthlyPayment = Contracts.Money(x.MonthlyPayment), paidInterest = Contracts.Money(x.PaidInterest), unappliedCredit = Contracts.Money(x.UnappliedCredit), dueInterest = Contracts.Money(x.Installments.Where(i => i.DueDate <= Finance.DemoDate).Sum(i => i.Interest-i.PaidInterest)), dueAmount = Contracts.Money(x.Installments.Where(i => i.DueDate <= Finance.DemoDate).Sum(i => i.Interest+i.Principal-i.PaidInterest-i.PaidPrincipal)), nextDueDate = x.Installments.Where(i => i.Interest+i.Principal > i.PaidInterest+i.PaidPrincipal).OrderBy(i => i.Number).Select(i => (DateOnly?)i.DueDate).FirstOrDefault(), status = x.OutstandingPrincipal == 0 ? "Paid" : "Active" };
static object PaymentView(AppliedPayment p) => new { p.Id, p.EventId, p.BankTransactionId, p.OriginalReference, p.ResolvedReference, p.BankOutcome, amountMinor = Contracts.Money(p.AmountMinor), p.Status, p.Reason, p.PaidAt, p.ReceivedAt, p.AppliedAt, p.Attempts, p.LateReview, p.CorrelationId, p.Allocations };
public record ResolveRequest(string Reference);
public partial class Program;

