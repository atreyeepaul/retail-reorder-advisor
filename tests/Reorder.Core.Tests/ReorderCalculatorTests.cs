using Reorder.Core.Advisor;
using Reorder.Core.Models;

namespace Reorder.Core.Tests;

public sealed class ReorderCalculatorTests
{
    private static readonly AdvisorOptions Options = new();
    private static readonly DateOnly AsOf = new(2026, 10, 7);

    private static Supplier Supplier(int? leadTime = 5, int moq = 12, int packSize = 6) =>
        new("SUP-03", "Delta Foods", leadTime, moq, packSize);

    [Fact]
    public void Sku_4471_hand_calculation_matches_to_two_decimals()
    {
        var stats = new DemandStats(Velocity: 6.2, Sigma: 2.1, HistoryDays: 28, LastSaleDate: AsOf);

        var calc = ReorderCalculator.Compute(stats, onHand: 18, Supplier(), Options, AsOf);

        Assert.Equal(5, calc.LeadTimeDays);
        Assert.Equal(7.75, calc.SafetyStock, 2);        // 1.65 × 2.1 × √5
        Assert.Equal(38.75, calc.ReorderPoint, 2);      // 6.2 × 5 + SS
        Assert.Equal(2.90, calc.DaysOfCover!.Value, 2); // 18 / 6.2
        Assert.Equal(224.75, calc.OrderUpTo, 2);        // ROP + 6.2 × 30
        Assert.Equal(210, calc.SuggestedQty);           // ceil(224.75-18)=207 → up to pack 6
        Assert.Equal(new DateOnly(2026, 10, 9), calc.EstimatedStockoutDate);   // asOf + floor(2.9)
    }

    [Fact]
    public void Zero_velocity_gives_null_cover_and_no_order()
    {
        var calc = ReorderCalculator.Compute(new DemandStats(0, 0, 28, null), 10, Supplier(), Options, AsOf);

        Assert.Null(calc.DaysOfCover);
        Assert.Equal(0, calc.SuggestedQty);
        Assert.Null(calc.EstimatedStockoutDate);
    }

    [Fact]
    public void On_hand_above_reorder_point_suggests_nothing()
    {
        var calc = ReorderCalculator.Compute(
            new DemandStats(2, 0, 28, AsOf), onHand: 1000, Supplier(5, 1, 1), Options, AsOf);

        Assert.Equal(0, calc.SuggestedQty);
    }

    [Fact]
    public void On_hand_equal_to_reorder_point_still_orders()
    {
        // sigma 0 → ROP = v × L = 2 × 5 = 10; onHand == 10 (boundary is ≤)
        var calc = ReorderCalculator.Compute(
            new DemandStats(2, 0, 28, AsOf), onHand: 10, Supplier(5, 1, 1), Options, AsOf);

        Assert.Equal(10.0, calc.ReorderPoint, 2);
        Assert.True(calc.SuggestedQty > 0);
    }

    [Fact]
    public void Moq_larger_than_need_wins()
    {
        var calc = ReorderCalculator.Compute(
            new DemandStats(2, 0, 28, AsOf), onHand: 8, Supplier(5, moq: 100, packSize: 1), Options, AsOf);

        Assert.Equal(100, calc.SuggestedQty);   // need 62, MOQ 100 dominates
    }

    [Fact]
    public void Suggested_quantity_rounds_up_to_pack_size()
    {
        // CoverDays 43, v 1, L 1, sigma 0 → ROP 1, orderUpTo 44; onHand 1 → need 43; pack 12 → 48
        var options = new AdvisorOptions { CoverDays = 43 };

        var calc = ReorderCalculator.Compute(
            new DemandStats(1, 0, 28, AsOf), onHand: 1, Supplier(1, moq: 1, packSize: 12), options, AsOf);

        Assert.Equal(48, calc.SuggestedQty);
    }

    [Fact]
    public void Missing_supplier_lead_time_uses_the_default()
    {
        var calc = ReorderCalculator.Compute(
            new DemandStats(2, 0, 28, AsOf), onHand: 5, Supplier(leadTime: null, moq: 1, packSize: 1), Options, AsOf);

        Assert.Equal(Options.DefaultLeadTimeDays, calc.LeadTimeDays);   // 7
        Assert.Equal(14.0, calc.ReorderPoint, 2);                       // 2 × 7 + 0
    }
}
