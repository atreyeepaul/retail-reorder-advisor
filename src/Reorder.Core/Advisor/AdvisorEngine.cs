using Reorder.Core.Models;

namespace Reorder.Core.Advisor;

/// <summary>
/// The pure advisor: turns a <see cref="NormalizedDataset"/> into ranked recommendations plus a
/// run summary. No I/O — the Day 4 job loads the dataset, calls this, and writes the output.
/// Run metadata (generated time, quarantined rows, duration) is left defaulted for the caller.
/// </summary>
public static class AdvisorEngine
{
    public static RunResult Run(NormalizedDataset dataset, AdvisorOptions options, DateOnly asOf)
    {
        var salesBySku = dataset.Sales
            .GroupBy(s => s.Sku, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<SalesRecord>)[.. g], StringComparer.Ordinal);
        var suppliersById = dataset.Suppliers.ToDictionary(s => s.Id, StringComparer.Ordinal);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["URGENT"] = 0,
            ["STOCKOUT_RISK"] = 0,
            ["DEAD_STOCK"] = 0,
            ["OVERSTOCK"] = 0,
            ["HEALTHY"] = 0,
        };
        decimal overstockValue = 0m;
        decimal deadStockValue = 0m;
        decimal estLostSalesAvoided = 0m;

        var recommendations = new List<Recommendation>(dataset.Inventory.Count);
        foreach (var item in dataset.Inventory)
        {
            var skuSales = salesBySku.TryGetValue(item.Sku, out var found) ? found : [];
            var stats = DemandStats.Compute(skuSales, asOf, options.WindowDays);

            var supplier = suppliersById.TryGetValue(item.SupplierId, out var s)
                ? s
                : new Supplier(item.SupplierId, item.SupplierId, null, 1, 1);   // defensive fallback
            bool defaultLeadTimeUsed = supplier.LeadTimeDays is null;

            var calc = ReorderCalculator.Compute(stats, item.OnHand, supplier, options, asOf);
            var flag = FlagClassifier.Classify(
                stats, item.OnHand, calc.ReorderPoint, calc.DaysOfCover, calc.LeadTimeDays, options, asOf);
            var confidence = stats.HistoryDays < options.LowConfidenceHistoryDays || stats.Velocity == 0d
                ? Confidence.Low
                : Confidence.High;
            string reason = ReasonBuilder.Build(
                flag, item.OnHand, stats.Velocity, calc.DaysOfCover, calc.LeadTimeDays,
                calc.ReorderPoint, calc.SuggestedQty, defaultLeadTimeUsed, confidence, options);

            counts[flag.ToCode()]++;
            AccumulateMetrics(
                flag, item, stats, calc, skuSales, options,
                ref overstockValue, ref deadStockValue, ref estLostSalesAvoided);

            recommendations.Add(new Recommendation(
                item.Sku, item.Name, item.Category, item.SupplierId, supplier.Name,
                item.OnHand, Round2(stats.Velocity), Round2(stats.Sigma), calc.LeadTimeDays,
                Round2(calc.SafetyStock), Round2(calc.ReorderPoint),
                calc.DaysOfCover is double cover ? Round2(cover) : null,
                calc.SuggestedQty, flag, confidence, calc.EstimatedStockoutDate, reason));
        }

        var ranked = recommendations
            .OrderBy(r => (int)r.Flag)
            .ThenBy(r => r.DaysOfCover ?? double.PositiveInfinity)
            .ToList();

        var summary = new RunSummary(
            AsOf: dataset.AsOf,
            GeneratedAtUtc: default,
            SkuCount: dataset.Inventory.Count,
            CountsByFlag: counts,
            OverstockValue: Round2(overstockValue),
            DeadStockValue: Round2(deadStockValue),
            EstLostSalesAvoided: Round2(estLostSalesAvoided),
            QuarantinedRows: 0,
            RunDurationMs: 0);

        return new RunResult(summary, ranked);
    }

    private static void AccumulateMetrics(
        Flag flag, InventoryItem item, DemandStats stats, ReorderCalc calc,
        IReadOnlyList<SalesRecord> skuSales, AdvisorOptions options,
        ref decimal overstockValue, ref decimal deadStockValue, ref decimal estLostSalesAvoided)
    {
        switch (flag)
        {
            case Flag.Overstock:
                double excessUnits = item.OnHand - stats.Velocity * options.OverstockDays;
                overstockValue += (decimal)excessUnits * item.UnitCost;
                break;

            case Flag.DeadStock:
                deadStockValue += item.OnHand * item.UnitCost;
                break;

            case Flag.Urgent:
                double coverGap = Math.Max(0d, calc.LeadTimeDays - (calc.DaysOfCover ?? 0d));
                double lostUnits = coverGap * stats.Velocity;
                estLostSalesAvoided += (decimal)lostUnits * LatestUnitPrice(skuSales);
                break;
        }
    }

    private static decimal LatestUnitPrice(IReadOnlyList<SalesRecord> skuSales)
    {
        decimal price = 0m;
        DateOnly latest = DateOnly.MinValue;
        foreach (var sale in skuSales)
        {
            if (sale.Date >= latest)
            {
                latest = sale.Date;
                price = sale.UnitPrice;
            }
        }

        return price;
    }

    private static double Round2(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
