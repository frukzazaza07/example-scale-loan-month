using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shared;
using BankSimulator;

var builder = WebApplication.CreateBuilder(args.Where(x => x is not ("--migrate" or "--seed")).ToArray()); builder.AddDemo("BankSimulator"); _ = DemoAuth.Secret(builder.Configuration,"AUTH_SECRET"); _ = DemoAuth.Secret(builder.Configuration,"BANK_SECRET");
builder.Services.AddDbContext<SimulatorDb>(o => o.UseNpgsql(Hosting.Connection(builder.Configuration)));
builder.Services.AddSingleton<BankSender>(); builder.Services.AddHttpClient("bank",c => c.Timeout = TimeSpan.FromSeconds(10)); builder.Services.AddHostedService<Generator>();
var app = builder.Build(); if (await Hosting.MigrateIfRequested<SimulatorDb>(app,args)) return;
app.UseDemo(); app.Readiness<SimulatorDb>(); var api = app.MapGroup("/api/v1").RequireAuthorization();
api.MapGet("/presets", () => new RunConfig[] { new(), new("Month-End Spike",100,120,100), new("Duplicate Storm",20,15,40,DuplicatePercent:80), new("Partial Payments",5,10,20,AmountMinor:"50000"), new("Same-Loan Concurrency",30,10,50,LoanReference:"LOAN-1001"), new("Missing Webhook",5,10,20,MissingPercent:20), new("Dependency Recovery",10,30,40) });
api.MapPost("/runs", async (RunConfig cfg, SimulatorDb db, HttpContext ctx, CancellationToken ct) =>
{
    if (cfg.TargetRps is < 1 or > 500 || cfg.DurationSeconds is < 1 or > 600 || cfg.Concurrency is < 1 or > 200 || cfg.TargetRps * cfg.DurationSeconds > 120000 || new[] {cfg.DuplicatePercent,cfg.InvalidSignaturePercent,cfg.BankFailedPercent,cfg.UnknownReferencePercent,cfg.MissingPercent}.Any(x => !double.IsFinite(x) || x is < 0 or > 100) || !long.TryParse(cfg.AmountMinor,out var amount) || amount is <= 0 or > Contracts.MaxAmount) return Results.Problem("Run configuration exceeds demo bounds",statusCode:400);
    await using var tx = await db.Database.BeginTransactionAsync(ct); await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(72603)",ct);
    if (await db.Runs.AnyAsync(x => x.Status == "Running",ct)) return Results.Conflict(new { reason = "Stop the active run before starting another" });
    var run = new SimulationRun { ConfigJson = JsonSerializer.Serialize(cfg,Contracts.Json), Preset = cfg.Preset, PlannedUnique = cfg.TargetRps*cfg.DurationSeconds }; db.Runs.Add(run);
    var random = new Random(cfg.RandomSeed); var paidAt = new DateTimeOffset(2026,10,31,16,59,0,TimeSpan.FromHours(7));
    for (var i = 0; i < run.PlannedUnique; i++)
    {
        var id = $"{run.Id:N}-{i:000000}"; var reference = random.NextDouble()*100 < cfg.UnknownReferencePercent ? "UNKNOWN-REFERENCE" : cfg.LoanReference ?? $"LOAN-{1001+i%1000}";
        var p = new BankPayment("evt-"+id,"tx-"+id,reference,Contracts.Money(amount),"THB",random.NextDouble()*100 < cfg.BankFailedPercent ? "FAILED" : "SUCCEEDED",paidAt,run.Id);
        db.Transactions.Add(new() { RunId = run.Id, Sequence = i, EventId = p.EventId, BankTransactionId = p.BankTransactionId, PaymentReference = reference, AmountMinor = p.AmountMinor, BankOutcome = p.Status, Body = JsonSerializer.Serialize(p,Contracts.Json), PaidAt = paidAt.ToUniversalTime(), Missing = random.NextDouble()*100 < cfg.MissingPercent, InvalidSignature = random.NextDouble()*100 < cfg.InvalidSignaturePercent, PlannedCopies = random.NextDouble()*100 < cfg.DuplicatePercent ? 2 : 1 });
    }
    db.Audits.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "StartRun", Identity = run.Id.ToString() }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Accepted($"/api/v1/runs/{run.Id}",new { run.Id, run.Status, run.PlannedUnique });
}).RequireAuthorization("Admin");
api.MapGet("/runs", async (SimulatorDb db,CancellationToken ct) => await db.Runs.OrderByDescending(x => x.CreatedAt).Take(30).Select(x => new { x.Id,x.Preset,x.Status,x.CreatedAt,x.FinishedAt,x.PlannedUnique,x.MaximumLagMs,x.BusinessDate }).ToListAsync(ct));
api.MapGet("/runs/{id:guid}", async (Guid id,SimulatorDb db,CancellationToken ct) =>
{
    var run = await db.Runs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id,ct); if (run is null) return Results.NotFound();
    var q = db.Attempts.Where(x => x.RunId == id); var attempts = await q.CountAsync(ct); var elapsed = Math.Max(1,((run.FinishedAt ?? DateTimeOffset.UtcNow)-run.CreatedAt).TotalSeconds);
    return Results.Ok(new { run, config = JsonSerializer.Deserialize<RunConfig>(run.ConfigJson,Contracts.Json), attemptedDeliveries = attempts, achievedRps = attempts/elapsed, acceptedReceipts = await q.CountAsync(x => x.ResponseCode == 200,ct), rejected = await q.CountAsync(x => x.ResponseCode >= 400 && x.ResponseCode < 500 && x.ResponseCode != 429,ct), timeouts = await q.CountAsync(x => x.Timeout,ct), retryable = await q.CountAsync(x => x.ResponseCode == null || x.ResponseCode == 429 || x.ResponseCode >= 500,ct), maxSchedulingLagMs = await q.MaxAsync(x => (double?)x.SchedulingLagMs,ct) ?? 0, duplicateCopies = await db.Transactions.Where(x => x.RunId == id).SumAsync(x => x.PlannedCopies-1,ct), skipped = await db.Transactions.CountAsync(x => x.RunId == id && x.Missing,ct), responseBreakdown = await q.GroupBy(x => x.ResponseCode).Select(g => new { code = g.Key, count = g.Count() }).ToListAsync(ct), recentAttempts = await q.OrderByDescending(x => x.StartedAt).Take(25).ToListAsync(ct) });
});
api.MapPost("/runs/{id:guid}/stop", async (Guid id,SimulatorDb db,HttpContext ctx,CancellationToken ct) => { var run = await db.Runs.SingleOrDefaultAsync(x => x.Id == id,ct); if (run is null) return Results.NotFound(); if (run.Status == "Running") { run.Status = "Stopped"; run.FinishedAt = DateTimeOffset.UtcNow; db.Audits.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "StopRun", Identity = id.ToString() }); await db.SaveChangesAsync(ct); } return Results.Ok(new { run.Status }); }).RequireAuthorization("Admin");
api.MapGet("/runs/{id:guid}/settlement", async (Guid id,SimulatorDb db,int? page,int? pageSize,CancellationToken ct) => new { cutoff = DateTimeOffset.UtcNow, total = await db.Transactions.CountAsync(x => x.RunId == id && x.BankOutcome == "SUCCEEDED",ct), items = await db.Transactions.Where(x => x.RunId == id && x.BankOutcome == "SUCCEEDED").OrderBy(x => x.Sequence).Skip((Math.Max(1,page ?? 1)-1)*Math.Clamp(pageSize ?? 100,1,500)).Take(Math.Clamp(pageSize ?? 100,1,500)).Select(x => new { x.BankTransactionId,x.EventId,x.PaymentReference,x.AmountMinor,x.PaidAt,x.Missing,x.LastSentAt }).ToListAsync(ct) });
api.MapPost("/transactions/{transactionId}/redeliver", async (string transactionId,SimulatorDb db,BankSender sender,HttpContext ctx,CancellationToken ct) =>
{
    var row = await db.Transactions.AsNoTracking().SingleOrDefaultAsync(x => x.BankTransactionId == transactionId,ct); if (row is null) return Results.NotFound();
    db.Audits.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "RedeliverOriginalIdentity", Identity = transactionId }); await db.SaveChangesAsync(ct); await sender.Send(row,0,true,ct); return Results.Ok(new { status = "Redelivered",row.BankTransactionId });
}).RequireAuthorization("Admin");
api.MapGet("/audit", async (SimulatorDb db,CancellationToken ct) => await db.Audits.OrderByDescending(x => x.At).Take(100).ToListAsync(ct));
app.Run();
public partial class Program;

