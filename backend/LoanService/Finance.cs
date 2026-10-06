namespace LoanService;
public static class Finance
{
    public static readonly DateOnly DemoDate = new(2026,10,31);
    public static DateOnly BusinessDate(DateTimeOffset time) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(time, "Asia/Bangkok").DateTime);
    public static List<Installment> Schedule(long principal, decimal annualRate, long payment, DateOnly firstDue)
    {
        if (principal is <= 0 or > Shared.Contracts.MaxAmount || annualRate is < 0 or > 1 || payment <= 0) throw new ArgumentOutOfRangeException(nameof(principal));
        var result = new List<Installment>(); var opening = principal;
        for (var number = 1; opening > 0; number++)
        {
            if (number > 1200) throw new ArgumentException("Schedule exceeds 100 years");
            var interest = checked((long)decimal.Round(opening * annualRate / 12m, 0, MidpointRounding.AwayFromZero));
            if (payment <= interest) throw new ArgumentException("Payment does not cover interest");
            var principalPart = Math.Min(opening, payment - interest); var month = firstDue.AddMonths(number-1);
            result.Add(new() { Number = number, DueDate = new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month)), ScheduledOpeningPrincipal = opening, Interest = interest, Principal = principalPart, ScheduledClosingPrincipal = opening - principalPart });
            opening -= principalPart;
        }
        return result;
    }
    public static (List<Allocation> allocations, long credit) Allocate(Loan loan, long amount, DateOnly date)
    {
        if (amount is <= 0 or > Shared.Contracts.MaxAmount) throw new ArgumentOutOfRangeException(nameof(amount));
        var remaining = amount; var allocations = new List<Allocation>();
        foreach (var installment in loan.Installments.Where(x => x.DueDate <= date).OrderBy(x => x.Number))
        {
            var interest = Math.Min(remaining, installment.Interest - installment.PaidInterest); remaining -= interest;
            var principal = Math.Min(remaining, installment.Principal - installment.PaidPrincipal); remaining -= principal;
            installment.PaidInterest = checked(installment.PaidInterest + interest); installment.PaidPrincipal = checked(installment.PaidPrincipal + principal);
            loan.OutstandingPrincipal = checked(loan.OutstandingPrincipal - principal); loan.PaidInterest = checked(loan.PaidInterest + interest);
            if (interest + principal > 0) allocations.Add(new() { InstallmentId = installment.Id, InstallmentNumber = installment.Number, Interest = interest, Principal = principal });
            if (remaining == 0) break;
        }
        loan.UnappliedCredit = checked(loan.UnappliedCredit + remaining);
        return (allocations, remaining);
    }
}
