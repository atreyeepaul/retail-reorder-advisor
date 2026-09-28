namespace Reorder.Core.Models;

/// <summary>
/// How much history the recommendation is based on. <c>Low</c> when history is shorter than
/// <c>AdvisorOptions.LowConfidenceHistoryDays</c> or there is no recent demand.
/// </summary>
public enum Confidence
{
    High,
    Low,
}
