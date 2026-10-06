using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace LoanService;
public sealed class PaymentProcessor(LoanDb db, RuntimeMetrics? metrics = null)
{
    public async Task<AppliedPayment> Apply(PaymentReceived message, bool replay = false, CancellationToken ct = default, string? actor = null)
    {
        if (!Contracts.Valid(message.Payment) || message.SchemaVersion != 1 || message.EventId != message.Payment.EventId || message.BankTransactionId != message.Payment.BankTransactionId) throw new ArgumentException("Unsupported event or inconsistent identity");
        ActivityContext.TryParse(message.TraceParent, null, out var parent);
        using var activity = Contracts.Traces.StartActivity("loan.allocate", ActivityKind.Consumer, parent);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({message.BankId + ":" + message.BankTransactionId},0))",ct);
        var existing = await db.Payments.Include(x => x.Allocations).SingleOrDefaultAsync(x => x.BankId == message.BankId && x.BankTransactionId == message.BankTransactionId,ct);
        var previousStatus = existing?.Status ?? "NotProcessed";
        if (existing is not null)
        {
            existing.Attempts++;
            if (existing.Fingerprint != Contracts.Fingerprint(message.Payment))
            {
                if (!await db.Conflicts.AnyAsync(x => x.BankTransactionId == message.BankTransactionId && x.EventId == message.EventId,ct)) db.Conflicts.Add(new() { BankTransactionId = message.BankTransactionId, EventId = message.EventId });
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return existing;
            }
            if (!replay || existing.Status is "Applied" or "AppliedWithCredit" or "BankDeclined") { if (actor is not null) db.Audits.Add(new() { Actor = actor, Action = "Replay", Identity = message.BankTransactionId, Details = previousStatus + " → " + existing.Status }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return existing; }
        }
        var p = existing ?? new AppliedPayment { BankId = message.BankId, BankTransactionId = message.BankTransactionId, EventId = message.EventId, OriginalReference = message.Payment.PaymentReference, AmountMinor = long.Parse(message.Payment.AmountMinor, CultureInfo.InvariantCulture), BankOutcome = message.Payment.Status, PaidAt = message.Payment.PaidAt.ToUniversalTime(), ReceivedAt = message.ReceivedAt, Fingerprint = Contracts.Fingerprint(message.Payment), Envelope = JsonSerializer.Serialize(message, Contracts.Json), CorrelationId = message.CorrelationId, Attempts = 1 };
        if (existing is null) db.Payments.Add(p);
        if (p.BankOutcome == "FAILED") { p.Status = "BankDeclined"; p.Reason = "Bank reported failure; no financial entries"; }
        else
        {
            var reference = p.ResolvedReference ?? p.OriginalReference;
            var loan = await db.Loans.FromSqlInterpolated($"SELECT * FROM loans WHERE \"Reference\"={reference} FOR UPDATE").SingleOrDefaultAsync(ct);
            if (loan is null) { p.Status = "Unmatched"; p.Reason = "Unknown loan reference; Admin resolution required"; }
            else
            {
                await db.Entry(loan).Collection(x => x.Installments).LoadAsync(ct);
                p.LoanId = loan.Id; var date = Finance.BusinessDate(p.PaidAt);
                p.LateReview = Finance.BusinessDate(p.ReceivedAt).DayNumber - date.DayNumber > 7;
                foreach (var period in loan.Installments.Where(x => x.DueDate <= date && !x.InterestRecognized))
                {
                    period.InterestRecognized = true;
                    db.Journals.Add(new() { LoanId = loan.Id, Identity = $"interest:{loan.Id}:{period.Number}", Kind = "ScheduledInterestRecognition", Postings = [new() { Account = "InterestReceivable", Debit = period.Interest }, new() { Account = "InterestIncome", Credit = period.Interest }] });
                }
                var (allocations, credit) = Finance.Allocate(loan, p.AmountMinor, date); db.Set<Allocation>().AddRange(allocations); p.Allocations = allocations;
                db.Journals.Add(new() { LoanId = loan.Id, PaymentId = p.Id, Identity = $"payment:{p.BankId}:{p.BankTransactionId}", Kind = "Payment", Postings = [new() { Account = "BankClearing", Debit = p.AmountMinor }, new() { Account = "PrincipalReceivable", Credit = allocations.Sum(x => x.Principal) }, new() { Account = "InterestReceivable", Credit = allocations.Sum(x => x.Interest) }, new() { Account = "UnappliedCustomerCredit", Credit = credit }] });
                p.Status = credit > 0 ? "AppliedWithCredit" : "Applied"; p.Reason = credit > 0 ? "Excess or payment before due date held as unapplied credit for review" : p.LateReview ? "Materially late bank timestamp retained for review" : null;
                p.AppliedAt = DateTimeOffset.UtcNow;
            }
        }
        db.Outbox.Add(OutboxRow.Create(Contracts.ResultsQueue, $"result:{p.Id}:{p.Status}", new PaymentResult($"result:{p.Id}:{p.Status}", p.BankTransactionId, p.BankId, DateTimeOffset.UtcNow, 1, p.CorrelationId, activity?.Id, p.ResolvedReference ?? p.OriginalReference, p.Status, Contracts.Money(p.AmountMinor),message.RunId ?? message.Payment.RunId)));
        if (actor is not null) db.Audits.Add(new() { Actor = actor, Action = "Replay", Identity = message.BankTransactionId, Details = previousStatus + " → " + p.Status });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        if (p.AppliedAt is not null) metrics?.Applied((p.AppliedAt.Value-p.ReceivedAt).TotalSeconds);
        return p;
    }
}
public sealed class LoanConsumer(IServiceScopeFactory scopes, Broker broker, ILogger<LoanConsumer> log, IConfiguration config) : QueueConsumer<LoanDb>(scopes, broker, log)
{
    protected override string Queue => Contracts.PaymentsQueue;
    protected override async Task Process(IServiceProvider services, string payload, CancellationToken ct)
    {
        var message = JsonSerializer.Deserialize<PaymentReceived>(payload, Contracts.Json) ?? throw new ArgumentException("Invalid event");
        await services.GetRequiredService<PaymentProcessor>().Apply(message, ct: ct);
        if (config["CRASH_AFTER_COMMIT"] == "true") Environment.FailFast("Demo crash injection after commit, before ack");
    }
}
