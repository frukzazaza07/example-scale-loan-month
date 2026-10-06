using Microsoft.EntityFrameworkCore;
using Shared;

namespace WebhookApi;
public sealed class Receipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BankId { get; set; } = "demo-bank";
    public string EventId { get; set; } = "";
    public string BankTransactionId { get; set; } = "";
    public string PaymentReference { get; set; } = "";
    public string BankOutcome { get; set; } = "";
    public string AmountMinor { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public DateTimeOffset PaidAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public int Attempts { get; set; } = 1;
    public string Status { get; set; } = "Received";
    public string? Reason { get; set; }
    public string CorrelationId { get; set; } = "";
}
public sealed class ValidationFailure
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string Reason { get; set; } = "";
}
public sealed class ReceiptDb(DbContextOptions<ReceiptDb> options) : MessagingDb(options)
{
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ValidationFailure> ValidationFailures => Set<ValidationFailure>();
    protected override void OnModelCreating(ModelBuilder m) { base.OnModelCreating(m); m.Entity<Receipt>().ToTable("receipts"); m.Entity<Receipt>().HasIndex(x => new { x.BankId, x.EventId }).IsUnique(); m.Entity<Receipt>().HasIndex(x => new { x.BankId, x.BankTransactionId }); m.Entity<ValidationFailure>().ToTable("validation_failures"); }
}
