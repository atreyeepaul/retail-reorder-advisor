using Microsoft.AspNetCore.Mvc.RazorPages;
using Reorder.Core.Models;
using Reorder.Web.Services;

namespace Reorder.Web.Pages;

/// <summary>
/// The health summary at <c>/health-summary</c>: portfolio-level tiles and a flag-distribution bar
/// built entirely from the latest run's <see cref="RunSummary"/>. Reads from <see cref="RecommendationCache"/>.
/// </summary>
public sealed class HealthSummaryModel(RecommendationCache cache) : PageModel
{
    public RunSummary? Summary { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) =>
        Summary = (await cache.GetAsync(ct))?.Summary;   // null → the view shows the placeholder
}
