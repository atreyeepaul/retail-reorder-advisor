using Reorder.Core.Ingestion;
using Reorder.Core.Models;

namespace Reorder.Core.Tests;

public sealed class RejectThresholdTests
{
    private static ValidationResult<SalesRecord> Result(int accepted, int rejected)
    {
        var ok = Enumerable.Range(0, accepted)
            .Select(i => new SalesRecord(new DateOnly(2026, 6, 1), i.ToString(), 1, 1m))
            .ToList();
        var bad = Enumerable.Range(0, rejected)
            .Select(i => new Reject("sales.csv", i + 2, RejectReason.BadType, "raw"))
            .ToList();
        return new ValidationResult<SalesRecord>(ok, bad);
    }

    [Fact]
    public void Ratio_and_total_are_computed()
    {
        var result = Result(accepted: 80, rejected: 20);

        Assert.Equal(100, result.Total);
        Assert.Equal(0.20, result.RejectRatio, precision: 10);
    }

    [Fact]
    public void Empty_input_has_zero_ratio()
    {
        var result = Result(accepted: 0, rejected: 0);

        Assert.Equal(0, result.Total);
        Assert.Equal(0d, result.RejectRatio);
        Assert.False(result.ExceedsThreshold(0.20));
    }

    [Fact]
    public void Ratio_exactly_at_threshold_does_not_exceed()
    {
        // 20% rejects at a 0.20 cap: the rule is "more than 20%", so this is allowed.
        var result = Result(accepted: 80, rejected: 20);

        Assert.False(result.ExceedsThreshold(0.20));
    }

    [Fact]
    public void Ratio_above_threshold_exceeds()
    {
        var result = Result(accepted: 79, rejected: 21);

        Assert.True(result.ExceedsThreshold(0.20));
    }
}
