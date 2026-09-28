using System.Globalization;
using Reorder.Core.Models;

namespace Reorder.Core.Advisor;

/// <summary>
/// Builds one deterministic, plain-English sentence per SKU from the computed figures. The same
/// inputs always produce the same text (unit-tested). Numbers are formatted invariantly.
/// </summary>
public static class ReasonBuilder
{
    public static string Build(
        Flag flag, int onHand, double velocity, double? daysOfCover, int leadTimeDays,
        double reorderPoint, int suggestedQty, bool defaultLeadTimeUsed, Confidence confidence,
        AdvisorOptions options)
    {
        string cover = daysOfCover is double c ? c.ToString("0.0", CultureInfo.InvariantCulture) : "n/a";
        string rop = reorderPoint.ToString("0.0", CultureInfo.InvariantCulture);

        string core = flag switch
        {
            Flag.Urgent =>
                $"On hand {onHand} covers ~{cover} days but supplier lead time is {leadTimeDays} days. Order {suggestedQty} now.",
            Flag.StockoutRisk =>
                $"On hand {onHand} is below the reorder point {rop}. Order {suggestedQty}.",
            Flag.DeadStock =>
                $"No sales in {options.DeadStockDays}+ days with {onHand} units on hand. Consider clearing.",
            Flag.Overstock =>
                $"{cover} days of cover exceeds the {options.OverstockDays}-day overstock threshold. No reorder needed.",
            Flag.Healthy when velocity == 0d =>
                $"No recent demand; {onHand} units on hand.",
            Flag.Healthy =>
                $"Healthy: ~{cover} days of cover.",
            _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, "Unknown flag"),
        };

        if (defaultLeadTimeUsed)
        {
            core += $" Supplier lead time missing; used the default of {leadTimeDays} days.";
        }

        if (confidence == Confidence.Low)
        {
            core += " Low confidence.";
        }

        return core;
    }
}
