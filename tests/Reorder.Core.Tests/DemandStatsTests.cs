using Reorder.Core.Advisor;
using Reorder.Core.Models;

namespace Reorder.Core.Tests;

public sealed class DemandStatsTests
{
    private static readonly DateOnly AsOf = new(2026, 1, 28);

    private static SalesRecord Sale(DateOnly date, int qty) => new(date, "1001", qty, 10m);

    [Fact]
    public void Zero_filled_series_computes_mean_and_sample_sigma()
    {
        // 5-day window ending 2026-01-28; sales only on two of the five days.
        var stats = DemandStats.Compute(
            [Sale(new(2026, 1, 24), 4), Sale(new(2026, 1, 28), 6)], AsOf, windowDays: 5);

        // series [4,0,0,0,6]: mean 2; sample sd = sqrt(32/4) = 2.8284
        Assert.Equal(2.0, stats.Velocity, 2);
        Assert.Equal(2.83, stats.Sigma, 2);
        Assert.Equal(5, stats.HistoryDays);
        Assert.Equal(new DateOnly(2026, 1, 28), stats.LastSaleDate);
    }

    [Fact]
    public void Single_day_of_history_has_zero_sigma()
    {
        var stats = DemandStats.Compute([Sale(AsOf, 5)], AsOf, windowDays: 28);

        Assert.Equal(5.0, stats.Velocity, 2);
        Assert.Equal(0.0, stats.Sigma, 2);
        Assert.Equal(1, stats.HistoryDays);
    }

    [Fact]
    public void First_sale_inside_window_shortens_the_series()
    {
        var stats = DemandStats.Compute(
            [Sale(new(2026, 1, 25), 3), Sale(new(2026, 1, 28), 3)], AsOf, windowDays: 28);

        // first-ever sale (25th) is after windowStart (1st) → series is 25..28 = 4 days, mean 6/4
        Assert.Equal(1.5, stats.Velocity, 2);
        Assert.Equal(4, stats.HistoryDays);
    }

    [Fact]
    public void Established_sku_uses_full_window_and_ignores_pre_window_sales()
    {
        var stats = DemandStats.Compute(
            [Sale(new(2025, 12, 20), 100), Sale(new(2026, 1, 15), 5)], AsOf, windowDays: 28);

        // first-ever sale (2025-12-20) precedes windowStart (2026-01-01) → full 28-day window;
        // only the Jan-15 sale falls inside it.
        Assert.Equal(28, stats.HistoryDays);
        Assert.Equal(0.18, stats.Velocity, 2);   // 5 / 28
        Assert.Equal(new DateOnly(2026, 1, 15), stats.LastSaleDate);
    }

    [Fact]
    public void No_sales_yields_zeroes_and_null_last_sale()
    {
        var stats = DemandStats.Compute([], AsOf, windowDays: 28);

        Assert.Equal(0.0, stats.Velocity, 2);
        Assert.Equal(0.0, stats.Sigma, 2);
        Assert.Equal(0, stats.HistoryDays);
        Assert.Null(stats.LastSaleDate);
    }
}
