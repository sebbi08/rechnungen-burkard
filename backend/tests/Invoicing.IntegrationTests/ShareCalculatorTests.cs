using Burkard.Invoicing.Domain;
using Xunit;

namespace Burkard.Invoicing.IntegrationTests;

public class ShareCalculatorTests
{
    [Fact]
    public void Even_split_has_no_remainder()
    {
        Assert.Equal(new long[] { 500, 500 }, ShareCalculator.SplitCents(1000, 2));
    }

    [Fact]
    public void Remainder_is_distributed_one_cent_at_a_time_from_the_front()
    {
        Assert.Equal(new long[] { 334, 333, 333 }, ShareCalculator.SplitCents(1000, 3));
    }

    [Fact]
    public void Shares_always_sum_to_the_total()
    {
        var shares = ShareCalculator.SplitCents(1, 3);
        Assert.Equal(1, shares.Sum());
    }

    [Fact]
    public void Zero_recipients_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShareCalculator.SplitCents(100, 0));
    }
}
