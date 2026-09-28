using System.Text.Json.Serialization;

namespace Reorder.Core.Models;

/// <summary>
/// The single classification a SKU receives. Declared in <b>severity order</b> (most urgent
/// first) so the output ranking can sort by the enum value; the classification *check* order is
/// different and lives in <c>FlagClassifier</c>. Serialized as the screaming-snake codes.
/// </summary>
public enum Flag
{
    [JsonStringEnumMemberName("URGENT")]
    Urgent,

    [JsonStringEnumMemberName("STOCKOUT_RISK")]
    StockoutRisk,

    [JsonStringEnumMemberName("DEAD_STOCK")]
    DeadStock,

    [JsonStringEnumMemberName("OVERSTOCK")]
    Overstock,

    [JsonStringEnumMemberName("HEALTHY")]
    Healthy,
}

/// <summary>Helpers for the string codes used as JSON keys (e.g. in <c>countsByFlag</c>).</summary>
public static class FlagExtensions
{
    public static string ToCode(this Flag flag) => flag switch
    {
        Flag.Urgent => "URGENT",
        Flag.StockoutRisk => "STOCKOUT_RISK",
        Flag.DeadStock => "DEAD_STOCK",
        Flag.Overstock => "OVERSTOCK",
        Flag.Healthy => "HEALTHY",
        _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, "Unknown flag"),
    };
}
