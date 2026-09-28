using Reorder.Core.Models;

namespace Reorder.Core.Advisor;

/// <summary>Full-precision reorder figures for one SKU. Rounding for display happens later.</summary>
public sealed record ReorderCalc(
    int LeadTimeDays,
    double SafetyStock,
    double ReorderPoint,
    double? DaysOfCover,
    double OrderUpTo,
    int SuggestedQty,
    DateOnly? EstimatedStockoutDate);

/// <summary>
/// Computes safety stock, reorder point, days of cover, order-up-to level, suggested quantity,
/// and estimated stockout date from demand stats and supplier terms. Pure; no rounding.
/// </summary>
public static class ReorderCalculator
{
    public static ReorderCalc Compute(
        DemandStats stats, int onHand, Supplier supplier, AdvisorOptions options, DateOnly asOf)
    {
        int leadTime = supplier.LeadTimeDays ?? options.DefaultLeadTimeDays;
        double v = stats.Velocity;

        double safetyStock = options.ServiceLevelZ * stats.Sigma * Math.Sqrt(leadTime);
        double reorderPoint = v * leadTime + safetyStock;
        double? daysOfCover = v > 0d ? onHand / v : null;
        double orderUpTo = reorderPoint + v * options.CoverDays;

        // Only order when there is demand and stock has fallen to the reorder point (inclusive).
        int suggestedQty = 0;
        if (v > 0d && onHand <= reorderPoint)
        {
            int need = (int)Math.Ceiling(orderUpTo - onHand);
            int candidate = Math.Max(supplier.Moq, need);
            suggestedQty = RoundUpToMultiple(candidate, supplier.PackSize);
        }

        DateOnly? estimatedStockoutDate = null;
        if (daysOfCover is double cover)
        {
            estimatedStockoutDate = asOf.AddDays((int)Math.Floor(cover));
        }

        return new ReorderCalc(leadTime, safetyStock, reorderPoint, daysOfCover, orderUpTo,
            suggestedQty, estimatedStockoutDate);
    }

    private static int RoundUpToMultiple(int value, int multiple)
    {
        if (multiple <= 1)
        {
            return value;
        }

        int remainder = value % multiple;
        return remainder == 0 ? value : value + (multiple - remainder);
    }
}
