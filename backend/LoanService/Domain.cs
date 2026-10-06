using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace LoanService;
public sealed class Loan
{
    public long Id { get; set; }
    public string Reference { get; set; } = "";
    public string Customer { get; set; } = "";
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long OpeningPrincipal { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long OutstandingPrincipal { get; set; }
    public decimal AnnualRate { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long MonthlyPayment { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long PaidInterest { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long UnappliedCredit { get; set; }
    public List<Installment> Installments { get; set; } = [];
}
public sealed class Installment
{
    public long Id { get; set; }
    public long LoanId { get; set; }
    public int Number { get; set; }
    public DateOnly DueDate { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long ScheduledOpeningPrincipal { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long Interest { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long Principal { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long ScheduledClosingPrincipal { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long PaidInterest { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long PaidPrincipal { get; set; }
    public bool InterestRecognized { get; set; }
}
public sealed class AppliedPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BankId { get; set; } = "";
    public string BankTransactionId { get; set; } = "";
    public string EventId { get; set; } = "";
    public string OriginalReference { get; set; } = "";
    public string? ResolvedReference { get; set; }
    public long? LoanId { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long AmountMinor { get; set; }
    public string BankOutcome { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string? Reason { get; set; }
    public string Fingerprint { get; set; } = "";
    public string Envelope { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset PaidAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public int Attempts { get; set; }
    public bool LateReview { get; set; }
    public List<Allocation> Allocations { get; set; } = [];
}
public sealed class Allocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AppliedPaymentId { get; set; }
    public long InstallmentId { get; set; }
    public int InstallmentNumber { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long Interest { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long Principal { get; set; }
}
public sealed class Journal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long LoanId { get; set; }
    public Guid? PaymentId { get; set; }
    public string Identity { get; set; } = "";
    public string Kind { get; set; } = "";
    public DateTimeOffset PostedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Posting> Postings { get; set; } = [];
}
public sealed class Posting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JournalId { get; set; }
    public string Account { get; set; } = "";
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long Debit { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long Credit { get; set; }
}
public sealed class ReviewConflict
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BankTransactionId { get; set; } = "";
    public string EventId { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string Reason { get; set; } = "Conflicting immutable fields or bank outcome; original financial result preserved";
}
public sealed class Audit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Identity { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class LoanDb(DbContextOptions<LoanDb> options) : MessagingDb(options)
{
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<Installment> Installments => Set<Installment>();
    public DbSet<AppliedPayment> Payments => Set<AppliedPayment>();
    public DbSet<Journal> Journals => Set<Journal>();
    public DbSet<ReviewConflict> Conflicts => Set<ReviewConflict>();
    public DbSet<Audit> Audits => Set<Audit>();
    protected override void OnModelCreating(ModelBuilder m)
    {
        base.OnModelCreating(m);
        m.Entity<Loan>().ToTable("loans", t => t.HasCheckConstraint("loan_balances", "\"OutstandingPrincipal\" >= 0 AND \"OutstandingPrincipal\" <= \"OpeningPrincipal\" AND \"UnappliedCredit\" >= 0")); m.Entity<Loan>().HasIndex(x => x.Reference).IsUnique(); m.Entity<Loan>().Property(x => x.AnnualRate).HasPrecision(10,6);
        m.Entity<Installment>().ToTable("installments", t => t.HasCheckConstraint("installment_paid", "\"PaidInterest\" >= 0 AND \"PaidInterest\" <= \"Interest\" AND \"PaidPrincipal\" >= 0 AND \"PaidPrincipal\" <= \"Principal\"")); m.Entity<Installment>().HasIndex(x => new { x.LoanId, x.Number }).IsUnique();
        m.Entity<AppliedPayment>().ToTable("payments"); m.Entity<AppliedPayment>().HasIndex(x => new { x.BankId, x.BankTransactionId }).IsUnique();
        m.Entity<Allocation>().ToTable("allocations");
        m.Entity<Journal>().ToTable("journals"); m.Entity<Journal>().HasIndex(x => x.Identity).IsUnique();
        m.Entity<Posting>().ToTable("postings", t => t.HasCheckConstraint("posting_positive", "\"Debit\" >= 0 AND \"Credit\" >= 0 AND (\"Debit\" = 0 OR \"Credit\" = 0)"));
        m.Entity<ReviewConflict>().ToTable("review_conflicts"); m.Entity<ReviewConflict>().HasIndex(x => new { x.BankTransactionId, x.EventId }).IsUnique(); m.Entity<Audit>().ToTable("audits");
    }
    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (ChangeTracker.Entries<Journal>().Any(e => e.State is EntityState.Modified or EntityState.Deleted) || ChangeTracker.Entries<Posting>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)) throw new InvalidOperationException("Posted journals are immutable");
        foreach (var e in ChangeTracker.Entries<Journal>().Where(e => e.State == EntityState.Added))
            if (e.Entity.Postings.Sum(p => p.Debit) != e.Entity.Postings.Sum(p => p.Credit)) throw new InvalidOperationException("Unbalanced journal");
        return base.SaveChangesAsync(ct);
    }
}
