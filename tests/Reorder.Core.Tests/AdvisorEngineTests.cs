using Reorder.Core.Advisor;
using Reorder.Core.Models;

namespace Reorder.Core.Tests;

public sealed class AdvisorEngineTests
{
    private static readonly AdvisorOptions Options = new();
    private static readonly DateOnly AsOf = new(2026, 1, 28);

    [Fact]
    public void Produces_ranked_recommendations_counts_and_summary_metrics()
    {
        var suppliers = new[] { new Supplier("SUP-1", "Acme", 5, 1, 1) };

        var inventory = new[]
        {
            new InventoryItem("U", "Urgent Item",    "Grocery", 10,  1m, "SUP-1"),
            new InventoryItem("O", "Overstock Item", "Grocery", 200, 2m, "SUP-1"),
            new InventoryItem("D", "Dead Item",      "Grocery", 50,  3m, "SUP-1"),
            new InventoryItem("H", "Healthy Item",   "Grocery", 100, 4m, "SUP-1"),
        };

        var sales = new List<SalesRecord>();
        sales.AddRange(DailySales("U", qty: 6, price: 10m));   // v 6 → urgent (10 < 6×5)
        sales.AddRange(DailySales("O", qty: 1, price: 5m));    // v 1, onHand 200 → overstock
        sales.Add(new SalesRecord(AsOf.AddDays(-70), "D", 5, 8m));   // last sale 70d ago → dead
        sales.AddRange(DailySales("H", qty: 2, price: 6m));    // v 2, onHand 100 → healthy

        var dataset = new NormalizedDataset(AsOf.ToString("yyyy-MM-dd"), default, sales, inventory, suppliers);

        var result = AdvisorEngine.Run(dataset, Options, AsOf);

        // Ranking: severity (URGENT, STOCKOUT_RISK, DEAD_STOCK, OVERSTOCK, HEALTHY) then cover.
        Assert.Equal(
            [Flag.Urgent, Flag.DeadStock, Flag.Overstock, Flag.Healthy],
            result.Recommendations.Select(r => r.Flag).ToArray());

        // Counts — all five keys present.
        Assert.Equal(1, result.Summary.CountsByFlag["URGENT"]);
        Assert.Equal(0, result.Summary.CountsByFlag["STOCKOUT_RISK"]);
        Assert.Equal(1, result.Summary.CountsByFlag["DEAD_STOCK"]);
        Assert.Equal(1, result.Summary.CountsByFlag["OVERSTOCK"]);
        Assert.Equal(1, result.Summary.CountsByFlag["HEALTHY"]);
        Assert.Equal(4, result.Summary.SkuCount);

        // Summary metrics (hand-computed).
        Assert.Equal(220.00m, result.Summary.OverstockValue);       // (200 − 1×90) × 2
        Assert.Equal(150.00m, result.Summary.DeadStockValue);       // 50 × 3
        Assert.Equal(200.00m, result.Summary.EstLostSalesAvoided);  // max(0, 5 − 10/6) × 6 × 10

        // Spot-check the top (urgent) recommendation.
        var urgent = result.Recommendations[0];
        Assert.Equal("U", urgent.Sku);
        Assert.Equal("Acme", urgent.SupplierName);
        Assert.Equal(Confidence.High, urgent.Confidence);
        Assert.Equal(200, urgent.SuggestedQty);
        Assert.Equal(AsOf.AddDays(1), urgent.EstimatedStockoutDate);   // floor(10/6) = 1
    }

    private static IEnumerable<SalesRecord> DailySales(string sku, int qty, decimal price) =>
        Enumerable.Range(0, 28).Select(i => new SalesRecord(AsOf.AddDays(-i), sku, qty, price));
}
