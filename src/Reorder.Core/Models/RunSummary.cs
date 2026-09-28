namespace Reorder.Core.Models;

/// <summary>
/// The <c>summary</c> block of <c>latest.json</c>. The advisor engine fills the analytical fields;
/// <see cref="GeneratedAtUtc"/>, <see cref="QuarantinedRows"/>, and <see cref="RunDurationMs"/> are
/// run metadata the Day 4 job sets when it assembles the output.
/// </summary>
public sealed record RunSummary(
    string AsOf,
    DateTimeOffset GeneratedAtUtc,
    int SkuCount,
    IReadOnlyDictionary<string, int> CountsByFlag,
    decimal OverstockValue,
    decimal DeadStockValue,
    decimal EstLostSalesAvoided,
    int QuarantinedRows,
    long RunDurationMs);
