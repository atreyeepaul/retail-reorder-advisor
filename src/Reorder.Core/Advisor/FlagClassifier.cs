using Reorder.Core.Models;

namespace Reorder.Core.Advisor;

/// <summary>
/// Assigns exactly one <see cref="Flag"/> per SKU, checked in priority order:
/// DEAD_STOCK → URGENT → STOCKOUT_RISK → OVERSTOCK → HEALTHY. Uses full-precision reorder figures.
/// </summary>
public static class FlagClassifier
{
    public static Flag Classify(
        DemandStats stats, int onHand, double reorderPoint, double? daysOfCover,
        int leadTimeDays, AdvisorOptions options, DateOnly asOf)
    {
        double v = stats.Velocity;

        // 1. DEAD_STOCK: stock on the shelf that has sold before but not within the last
        // DeadStockDays. A never-sold SKU (no LastSaleDate) is brand-new, not dead.
        var deadCutoff = asOf.AddDays(-options.DeadStockDays);
        if (onHand > 0 && stats.LastSaleDate is DateOnly last && last <= deadCutoff)
        {
            return Flag.DeadStock;
        }

        // 2. URGENT: will run out before a reorder can arrive.
        if (v > 0d && onHand < v * leadTimeDays)
        {
            return Flag.Urgent;
        }

        // 3. STOCKOUT_RISK: below the reorder point (strict; onHand == ROP is not at risk).
        if (onHand < reorderPoint)
        {
            return Flag.StockoutRisk;
        }

        // 4. OVERSTOCK: cover beyond the overstock threshold (null cover cannot be overstock).
        if (daysOfCover is double cover && cover > options.OverstockDays)
        {
            return Flag.Overstock;
        }

        // 5. HEALTHY.
        return Flag.Healthy;
    }
}
