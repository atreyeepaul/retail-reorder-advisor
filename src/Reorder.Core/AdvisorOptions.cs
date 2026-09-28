using System.ComponentModel.DataAnnotations;

namespace Reorder.Core;

/// <summary>
/// Tunable parameters for the reorder calculation. Bound from the "Advisor"
/// configuration section and validated at application startup.
/// </summary>
public sealed class AdvisorOptions
{
    public const string SectionName = "Advisor";

    /// <summary>Trailing window, in days, used for velocity and σ.</summary>
    [Range(7, 365)]
    public int WindowDays { get; set; } = 28;

    /// <summary>Z-score for the target service level (1.65 ≈ 95%).</summary>
    [Range(0.0, 4.0)]
    public double ServiceLevelZ { get; set; } = 1.65;

    /// <summary>Target days of cover after a reorder arrives.</summary>
    [Range(1, 365)]
    public int CoverDays { get; set; } = 30;

    /// <summary>Days of cover above which a SKU is flagged OVERSTOCK.</summary>
    [Range(1, 3650)]
    public int OverstockDays { get; set; } = 90;

    /// <summary>Days without a sale after which a SKU is flagged DEAD_STOCK.</summary>
    [Range(1, 3650)]
    public int DeadStockDays { get; set; } = 60;

    /// <summary>Lead time used when a supplier record has none.</summary>
    [Range(1, 365)]
    public int DefaultLeadTimeDays { get; set; } = 7;

    /// <summary>History shorter than this marks a recommendation as low confidence.</summary>
    [Range(1, 365)]
    public int LowConfidenceHistoryDays { get; set; } = 14;

    /// <summary>Share of rejected sales rows above which ingestion aborts.</summary>
    [Range(0.0, 1.0)]
    public double MaxRejectRatio { get; set; } = 0.20;
}
