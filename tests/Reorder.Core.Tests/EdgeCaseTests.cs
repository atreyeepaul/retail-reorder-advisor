using Reorder.Core.Advisor;
using Reorder.Core.Models;

namespace Reorder.Core.Tests;

/// <summary>
/// The section 6.5 edge cases exercised end-to-end through <see cref="AdvisorEngine"/>, where the
/// flag + confidence + reason interplay matters. Cases 2, 7, 8 (and the calc/flag halves of 6 and 9)
/// are already covered by the component tests; these fill the gaps — notably the Confidence.Low paths.
/// </summary>
public sealed class EdgeCaseTests
{
    private static readonly AdvisorOptions Options = new();
    private static readonly DateOnly AsOf = new(2026, 1, 28);
    private static readonly Supplier DefaultSupplier = new("SUP-1", "Acme", 5, 1, 1);

    private static Recommendation RunSingle(InventoryItem item, IEnumerable<SalesRecord> sales, Supplier? supplier = null)
    {
        var dataset = new NormalizedDataset(
            AsOf.ToString("yyyy-MM-dd"), default, sales.ToList(), [item], [supplier ?? DefaultSupplier]);
        return AdvisorEngine.Run(dataset, Options, AsOf).Recommendations.Single();
    }

    private static IEnumerable<SalesRecord> DailySales(string sku, int qty, decimal price) =>
        Enumerable.Range(0, 28).Select(i => new SalesRecord(AsOf.AddDays(-i), sku, qty, price));

    [Fact]
    public void Case1_zero_velocity_with_stock_and_sale_within_60_days_is_healthy()
    {
        // Last sale 40 days ago: outside the 28-day velocity window (v = 0) but within DeadStockDays.
        var rec = RunSingle(
            new InventoryItem("Q", "Quiet", "Grocery", 30, 5m, "SUP-1"),
            [new SalesRecord(AsOf.AddDays(-40), "Q", 3, 10m)]);

        Assert.Equal(Flag.Healthy, rec.Flag);
        Assert.Null(rec.DaysOfCover);
        Assert.Equal(0, rec.SuggestedQty);
    }

    [Fact]
    public void Case3_zero_velocity_and_zero_on_hand_is_healthy_low_confidence_no_demand()
    {
        var rec = RunSingle(new InventoryItem("Z", "Zero", "Grocery", 0, 5m, "SUP-1"), []);

        Assert.Equal(Flag.Healthy, rec.Flag);
        Assert.Equal(Confidence.Low, rec.Confidence);
        Assert.Contains("No recent demand", rec.Reason);
        Assert.Equal(0, rec.SuggestedQty);
    }

    [Fact]
    public void Case4_brand_new_sku_with_stock_is_healthy_not_dead_stock()
    {
        // No sales history at all + stock on hand → HEALTHY (not DEAD_STOCK). Validates the fix.
        var rec = RunSingle(new InventoryItem("N", "New Arrival", "Grocery", 50, 5m, "SUP-1"), []);

        Assert.Equal(Flag.Healthy, rec.Flag);
        Assert.Equal(Confidence.Low, rec.Confidence);
        Assert.Equal(0, rec.SuggestedQty);
    }

    [Fact]
    public void Case5_single_day_of_history_has_zero_sigma_zero_ss_low_confidence()
    {
        var rec = RunSingle(
            new InventoryItem("S", "Single", "Grocery", 10, 5m, "SUP-1"),
            [new SalesRecord(AsOf, "S", 5, 10m)]);

        Assert.Equal(0.0, rec.Sigma, 2);
        Assert.Equal(0.0, rec.SafetyStock, 2);
        Assert.Equal(Confidence.Low, rec.Confidence);
    }

    [Fact]
    public void Case6_missing_supplier_lead_time_uses_default_and_reason_says_so()
    {
        var noLeadSupplier = new Supplier("SUP-7", "Polar Frozen", null, 1, 1);
        var rec = RunSingle(
            new InventoryItem("F", "Frozen Peas", "Frozen", 5, 5m, "SUP-7"),
            DailySales("F", 6, 10m),
            noLeadSupplier);

        Assert.Equal(Options.DefaultLeadTimeDays, rec.LeadTimeDays);   // 7
        Assert.Contains("default of 7 days", rec.Reason);
    }

    [Fact]
    public void Case9_on_hand_equal_to_reorder_point_orders_but_is_not_stockout_risk()
    {
        // Constant daily sales → sigma 0 → ROP = v*L = 2*5 = 10; onHand == 10.
        var rec = RunSingle(
            new InventoryItem("B", "Boundary", "Grocery", 10, 5m, "SUP-1"),
            DailySales("B", 2, 10m));

        Assert.Equal(10.0, rec.ReorderPoint, 2);
        Assert.True(rec.SuggestedQty > 0);                 // boundary is <=
        Assert.NotEqual(Flag.StockoutRisk, rec.Flag);      // strict < for the flag
    }
}
