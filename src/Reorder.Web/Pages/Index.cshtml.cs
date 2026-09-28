using Microsoft.AspNetCore.Mvc.RazorPages;
using Reorder.Core.Models;
using Reorder.Web.Services;

namespace Reorder.Web.Pages;

/// <summary>
/// The action list at <c>/</c>: the ranked recommendations from the latest advisor run, optionally
/// filtered to one flag via <c>?flag=</c>. Reads from <see cref="RecommendationCache"/>.
/// </summary>
public sealed class IndexModel(RecommendationCache cache) : PageModel
{
    public RunSummary? Summary { get; private set; }

    public IReadOnlyList<Recommendation> Recommendations { get; private set; } = [];

    /// <summary>The active flag code (e.g. "URGENT"), or null for "all".</summary>
    public string? ActiveFlag { get; private set; }

    public async Task OnGetAsync(string? flag, CancellationToken ct)
    {
        var result = await cache.GetAsync(ct);
        if (result is null)
        {
            return;   // no run yet → the view shows the placeholder
        }

        Summary = result.Summary;

        if (!string.IsNullOrWhiteSpace(flag) && FlagExtensions.TryParse(flag, out var parsed))
        {
            ActiveFlag = parsed.ToCode();
            Recommendations = result.Recommendations.Where(r => r.Flag == parsed).ToList();
        }
        else
        {
            Recommendations = result.Recommendations;   // unknown flag → show all
        }
    }
}
