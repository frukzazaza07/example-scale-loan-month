using LoanService;
using Xunit;
namespace Finance.Tests;
public class FinanceTests
{
    private static Loan Example() => new() { OpeningPrincipal=10_000_000,OutstandingPrincipal=10_000_000,AnnualRate=.12m,MonthlyPayment=900_000,Installments=LoanService.Finance.Schedule(10_000_000,.12m,900_000,new(2026,10,31)) };
    [Fact] public void First_payment_has_expected_financial_result() { var l=Example();var (a,c)=LoanService.Finance.Allocate(l,900_000,new(2026,10,31));Assert.Equal(100_000,a[0].Interest);Assert.Equal(800_000,a[0].Principal);Assert.Equal(9_200_000,l.OutstandingPrincipal);Assert.Equal(0,c); }
    [Fact] public void Partial_payments_pay_interest_before_principal() {var l=Example();LoanService.Finance.Allocate(l,50_000,new(2026,10,31));Assert.Equal(10_000_000,l.OutstandingPrincipal);var (a,_)=LoanService.Finance.Allocate(l,100_000,new(2026,10,31));Assert.Equal(50_000,a[0].Interest);Assert.Equal(50_000,a[0].Principal);Assert.Equal(9_950_000,l.OutstandingPrincipal);}
    [Fact] public void Excess_is_credit_and_future_principal_is_unchanged(){var l=Example();var (_,credit)=LoanService.Finance.Allocate(l,1_000_000,new(2026,10,31));Assert.Equal(100_000,credit);Assert.Equal(9_200_000,l.OutstandingPrincipal);Assert.Equal(0,l.Installments[1].PaidPrincipal);}
    [Fact] public void Before_due_date_entire_payment_is_credit(){var l=Example();var(a,c)=LoanService.Finance.Allocate(l,900_000,new(2026,10,30));Assert.Empty(a);Assert.Equal(900_000,c);Assert.Equal(10_000_000,l.OutstandingPrincipal);}
    [Fact] public void Schedule_preserves_month_ends_and_clears_principal(){var s=Example().Installments;Assert.Equal(new DateOnly(2027,2,28),s[4].DueDate);Assert.Equal(0,s[^1].ScheduledClosingPrincipal);Assert.Equal(10_000_000,s.Sum(x=>x.Principal));Assert.True(s[^1].Principal+s[^1].Interest<=900_000);}
    [Fact] public void Half_satang_rounds_away_from_zero(){var s=LoanService.Finance.Schedule(50,.12m,10,new(2026,10,31));Assert.Equal(1,s[0].Interest);}
    [Fact] public void Leap_year_month_end(){var s=LoanService.Finance.Schedule(100000,.12m,9000,new(2028,1,31));Assert.Equal(new DateOnly(2028,2,29),s[1].DueDate);}
    [Fact] public void Bangkok_eligibility_crosses_UTC_day(){Assert.Equal(new DateOnly(2026,10,31),LoanService.Finance.BusinessDate(new DateTimeOffset(2026,10,30,17,0,0,TimeSpan.Zero)));}
    [Theory][InlineData(0)][InlineData(-1)][InlineData(long.MaxValue)]public void Invalid_payment_amount_is_rejected(long amount){Assert.Throws<ArgumentOutOfRangeException>(()=>LoanService.Finance.Allocate(Example(),amount,new(2026,10,31)));}
    [Fact]public void Retry_does_not_recompute_contractual_interest(){var l=Example();var before=l.Installments.Select(x=>x.Interest).ToArray();LoanService.Finance.Allocate(l,1000,new(2026,10,31));Assert.Equal(before,l.Installments.Select(x=>x.Interest));}
}
