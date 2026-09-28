using System.Globalization;
using System.Text;
using CsvHelper;
using Reorder.Core.Models;
using Reorder.Web.Services;

namespace Reorder.Web.Endpoints;

/// <summary>
/// Unauthenticated, read-only endpoints that serve the latest advisor output from
/// <see cref="RecommendationCache"/>: the summary, the (filterable/sortable) recommendations, and a
/// CSV export. Return 404 until the first advisor run has produced <c>latest.json</c>.
/// </summary>
public static class ReadEndpoints
{
    private static readonly string[] CsvHeaders =
    [
        "sku", "name", "category", "supplierId", "supplierName", "onHand", "velocity", "sigma",
        "leadTimeDays", "safetyStock", "reorderPoint", "daysOfCover", "suggestedQty", "flag",
        "confidence", "estimatedStockoutDate", "reason",
    ];

    public static void MapReadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/summary", GetSummaryAsync);
        app.MapGet("/api/recommendations", GetRecommendationsAsync);
        app.MapGet("/api/recommendations/export.csv", ExportCsvAsync);
    }

    private static async Task<IResult> GetSummaryAsync(RecommendationCache cache, CancellationToken ct)
    {
        var result = await cache.GetAsync(ct);
        return result is null ? NoData() : Results.Ok(result.Summary);
    }

    private static async Task<IResult> GetRecommendationsAsync(
        string? flag, string? sort, RecommendationCache cache, CancellationToken ct)
    {
        var result = await cache.GetAsync(ct);
        if (result is null)
        {
            return NoData();
        }

        IEnumerable<Recommendation> recs = result.Recommendations;

        if (!string.IsNullOrWhiteSpace(flag))
        {
            if (!TryParseFlag(flag, out var parsed))
            {
                return Results.BadRequest(new { error = $"Unknown flag '{flag}'." });
            }

            recs = recs.Where(r => r.Flag == parsed);
        }

        if (!TrySort(recs, sort, out var sorted))
        {
            return Results.BadRequest(new { error = $"Unknown sort '{sort}'. Use rank, cover, qty, or sku." });
        }

        return Results.Ok(sorted.ToList());
    }

    private static async Task<IResult> ExportCsvAsync(string? flag, RecommendationCache cache, CancellationToken ct)
    {
        var result = await cache.GetAsync(ct);
        if (result is null)
        {
            return NoData();
        }

        IEnumerable<Recommendation> recs = result.Recommendations;
        if (!string.IsNullOrWhiteSpace(flag))
        {
            if (!TryParseFlag(flag, out var parsed))
            {
                return Results.BadRequest(new { error = $"Unknown flag '{flag}'." });
            }

            recs = recs.Where(r => r.Flag == parsed);
        }

        byte[] bytes = Encoding.UTF8.GetBytes(BuildCsv(recs));
        return Results.File(bytes, "text/csv", $"recommendations-{result.Summary.AsOf}.csv");
    }

    private static string BuildCsv(IEnumerable<Recommendation> recs)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        foreach (string header in CsvHeaders)
        {
            csv.WriteField(header);
        }

        csv.NextRecord();

        foreach (var r in recs)
        {
            csv.WriteField(r.Sku);
            csv.WriteField(r.Name);
            csv.WriteField(r.Category);
            csv.WriteField(r.SupplierId);
            csv.WriteField(r.SupplierName);
            csv.WriteField(r.OnHand);
            csv.WriteField(r.Velocity);
            csv.WriteField(r.Sigma);
            csv.WriteField(r.LeadTimeDays);
            csv.WriteField(r.SafetyStock);
            csv.WriteField(r.ReorderPoint);
            csv.WriteField(r.DaysOfCover);
            csv.WriteField(r.SuggestedQty);
            csv.WriteField(r.Flag.ToCode());
            csv.WriteField(r.Confidence.ToString());
            csv.WriteField(r.EstimatedStockoutDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            csv.WriteField(r.Reason);
            csv.NextRecord();
        }

        return writer.ToString();
    }

    private static bool TryParseFlag(string value, out Flag flag)
    {
        switch (value.Trim().ToUpperInvariant())
        {
            case "URGENT": flag = Flag.Urgent; return true;
            case "STOCKOUT_RISK": flag = Flag.StockoutRisk; return true;
            case "DEAD_STOCK": flag = Flag.DeadStock; return true;
            case "OVERSTOCK": flag = Flag.Overstock; return true;
            case "HEALTHY": flag = Flag.Healthy; return true;
            default: flag = default; return false;
        }
    }

    // Default order is the ranking already baked into latest.json (severity, then days of cover).
    private static bool TrySort(IEnumerable<Recommendation> recs, string? sort, out IEnumerable<Recommendation> sorted)
    {
        switch (sort?.Trim().ToLowerInvariant())
        {
            case null or "" or "rank": sorted = recs; return true;
            case "cover": sorted = recs.OrderBy(r => r.DaysOfCover ?? double.PositiveInfinity); return true;
            case "qty": sorted = recs.OrderByDescending(r => r.SuggestedQty); return true;
            case "sku": sorted = recs.OrderBy(r => r.Sku, StringComparer.Ordinal); return true;
            default: sorted = recs; return false;
        }
    }

    private static IResult NoData() => Results.NotFound(new { error = "No advisor run yet." });
}
