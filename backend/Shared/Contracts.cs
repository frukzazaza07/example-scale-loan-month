using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Shared;

public record BankPayment(string EventId, string BankTransactionId, string PaymentReference, string AmountMinor, string Currency, string Status, DateTimeOffset PaidAt, Guid? RunId = null);
public record PaymentReceived(string EventId, string BankTransactionId, string BankId, DateTimeOffset OccurredAt, int SchemaVersion, string CorrelationId, string? TraceParent, BankPayment Payment, DateTimeOffset ReceivedAt, Guid? RunId = null);
public record PaymentResult(string EventId, string BankTransactionId, string BankId, DateTimeOffset OccurredAt, int SchemaVersion, string CorrelationId, string? TraceParent, string PaymentReference, string Status, string AmountMinor, Guid? RunId = null);
public static class Contracts
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly ActivitySource Traces = new("MonthEndLms");
    public const string PaymentsQueue = "lms.payments";
    public const string ResultsQueue = "lms.notifications";
    public const long MaxAmount = 100_000_000_000_000;
    public static string Money(long minor) => minor.ToString(CultureInfo.InvariantCulture);
    public static bool Valid(BankPayment p) => !string.IsNullOrWhiteSpace(p.EventId) && p.EventId.Length <= 128 && !string.IsNullOrWhiteSpace(p.BankTransactionId) && p.BankTransactionId.Length <= 128 && !string.IsNullOrWhiteSpace(p.PaymentReference) && p.PaymentReference.Length <= 128 && p.Currency == "THB" && p.Status is "SUCCEEDED" or "FAILED" && long.TryParse(p.AmountMinor, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) && amount is > 0 and <= MaxAmount && p.PaidAt != default;
    public static string Fingerprint(BankPayment p) => JsonSerializer.Serialize(new { p.BankTransactionId, p.PaymentReference, amount = long.Parse(p.AmountMinor, CultureInfo.InvariantCulture), p.Currency, p.Status, paidAt = p.PaidAt.ToUniversalTime() }, Json);
}
