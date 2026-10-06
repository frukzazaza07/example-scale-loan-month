using Microsoft.EntityFrameworkCore;
using Shared;

namespace BankSimulator;
public record RunConfig(string Preset = "Normal", int TargetRps = 5, int DurationSeconds = 10, int Concurrency = 20, int RandomSeed = 42, double DuplicatePercent = 0, double InvalidSignaturePercent = 0, double BankFailedPercent = 0, double UnknownReferencePercent = 0, double MissingPercent = 0, string? LoanReference = null, string AmountMinor = "900000");
public sealed class SimulationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ConfigJson { get; set; } = "";
    public string Preset { get; set; } = "";
    public string Status { get; set; } = "Running";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public int PlannedUnique { get; set; }
    public double MaximumLagMs { get; set; }
    public string BusinessDate { get; set; } = "2026-10-31";
}
public sealed class BankTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public int Sequence { get; set; }
    public string EventId { get; set; } = "";
    public string BankTransactionId { get; set; } = "";
    public string PaymentReference { get; set; } = "";
    public string AmountMinor { get; set; } = "";
    public string BankOutcome { get; set; } = "SUCCEEDED";
    public string Body { get; set; } = "";
    public bool Missing { get; set; }
    public bool InvalidSignature { get; set; }
    public int PlannedCopies { get; set; } = 1;
    public int CompletedCopies { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    public DateTimeOffset? LastSentAt { get; set; }
}
public sealed class DeliveryAttempt
{
    public long Id { get; set; }
    public Guid RunId { get; set; }
    public string BankTransactionId { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public double DurationMs { get; set; }
    public double SchedulingLagMs { get; set; }
    public int? ResponseCode { get; set; }
    public bool Timeout { get; set; }
    public string? Error { get; set; }
    public bool Recovery { get; set; }
}
public sealed class SimulatorAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Identity { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class SimulatorDb(DbContextOptions<SimulatorDb> options) : DbContext(options)
{
    public DbSet<SimulationRun> Runs => Set<SimulationRun>();
    public DbSet<BankTransaction> Transactions => Set<BankTransaction>();
    public DbSet<DeliveryAttempt> Attempts => Set<DeliveryAttempt>();
    public DbSet<SimulatorAudit> Audits => Set<SimulatorAudit>();
    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<SimulationRun>().ToTable("runs");
        // A constant indexed expression in the migration prevents two active runs globally.
        m.Entity<BankTransaction>().ToTable("bank_transactions"); m.Entity<BankTransaction>().HasIndex(x => x.BankTransactionId).IsUnique(); m.Entity<BankTransaction>().HasIndex(x => new { x.RunId, x.Sequence }).IsUnique();
        m.Entity<DeliveryAttempt>().ToTable("delivery_attempts"); m.Entity<DeliveryAttempt>().HasIndex(x => new { x.RunId, x.StartedAt });
        m.Entity<SimulatorAudit>().ToTable("simulator_audits");
    }
}
