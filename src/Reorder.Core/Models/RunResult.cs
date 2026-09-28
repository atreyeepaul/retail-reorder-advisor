namespace Reorder.Core.Models;

/// <summary>
/// The complete advisor output for one run: the summary plus the ranked recommendations.
/// Matches the top level of <c>latest.json</c> (<c>summary</c> + <c>recommendations</c>).
/// </summary>
public sealed record RunResult(RunSummary Summary, IReadOnlyList<Recommendation> Recommendations);
