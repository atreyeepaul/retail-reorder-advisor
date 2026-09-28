using Reorder.Core.Advisor;
using Reorder.Core.Models;

namespace Reorder.Core.Tests;

public sealed class FlagClassifierTests
{
    private static readonly AdvisorOptions Options = new();
    private static readonly DateOnly AsOf = new(2026, 3, 1);

    private static DemandStats Stats(double velocity, DateOnly? lastSale) => new(velocity, 0d, 28, lastSale);

    private static Flag Classify(DemandStats stats, int onHand, double rop, double? doc, int leadTime = 5) =>
        FlagClassifier.Classify(stats, onHand, rop, doc, leadTime, Options, AsOf);

    [Fact]
    public void Dead_stock_when_stock_on_hand_and_no_recent_sales()
    {
        var flag = Classify(Stats(0, AsOf.AddDays(-70)), onHand: 50, rop: 0, doc: null);
        Assert.Equal(Flag.DeadStock, flag);
    }

    [Fact]
    public void Sale_within_dead_stock_window_is_not_dead()
    {
        // 59 days ago is within the 60-day window → zero velocity + stock on hand → HEALTHY
        var flag = Classify(Stats(0, AsOf.AddDays(-59)), onHand: 50, rop: 0, doc: null);
        Assert.Equal(Flag.Healthy, flag);
    }

    [Fact]
    public void Sale_exactly_at_dead_stock_threshold_is_dead()
    {
        var flag = Classify(Stats(0, AsOf.AddDays(-60)), onHand: 50, rop: 0, doc: null);
        Assert.Equal(Flag.DeadStock, flag);
    }

    [Fact]
    public void Urgent_when_stock_below_lead_time_demand()
    {
        var flag = Classify(Stats(6, AsOf.AddDays(-1)), onHand: 18, rop: 38, doc: 3.0, leadTime: 5);
        Assert.Equal(Flag.Urgent, flag);   // 18 < 6 × 5 = 30
    }

    [Fact]
    public void Urgent_takes_priority_over_stockout_risk()
    {
        var flag = Classify(Stats(6, AsOf.AddDays(-1)), onHand: 10, rop: 38, doc: 1.67, leadTime: 5);
        Assert.Equal(Flag.Urgent, flag);   // below both v×L and ROP → URGENT wins
    }

    [Fact]
    public void Stockout_risk_when_below_reorder_point_but_not_urgent()
    {
        var flag = Classify(Stats(2, AsOf.AddDays(-1)), onHand: 30, rop: 38, doc: 15.0, leadTime: 5);
        Assert.Equal(Flag.StockoutRisk, flag);   // 30 ≥ 2×5 but < 38
    }

    [Fact]
    public void On_hand_equal_to_reorder_point_is_not_stockout_risk()
    {
        var flag = Classify(Stats(2, AsOf.AddDays(-1)), onHand: 38, rop: 38, doc: 19.0, leadTime: 5);
        Assert.Equal(Flag.Healthy, flag);
    }

    [Fact]
    public void Overstock_when_cover_exceeds_threshold()
    {
        var flag = Classify(Stats(1, AsOf.AddDays(-1)), onHand: 200, rop: 10, doc: 200.0, leadTime: 5);
        Assert.Equal(Flag.Overstock, flag);
    }

    [Fact]
    public void Cover_exactly_at_overstock_threshold_is_not_overstock()
    {
        var flag = Classify(Stats(1, AsOf.AddDays(-1)), onHand: 90, rop: 10, doc: 90.0, leadTime: 5);
        Assert.Equal(Flag.Healthy, flag);   // 90 is not > 90
    }

    [Fact]
    public void Healthy_when_nothing_else_applies()
    {
        var flag = Classify(Stats(2, AsOf.AddDays(-1)), onHand: 100, rop: 20, doc: 50.0, leadTime: 5);
        Assert.Equal(Flag.Healthy, flag);
    }
}
