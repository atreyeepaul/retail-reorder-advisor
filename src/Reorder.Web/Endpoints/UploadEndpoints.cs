using System.Globalization;
using Reorder.Web.Infrastructure;
using Reorder.Web.Services;

namespace Reorder.Web.Endpoints;

/// <summary>
/// <c>POST /api/uploads?asOf=yyyy-MM-dd</c>: a multipart upload of the three CSVs, guarded by
/// the <c>X-Admin-Key</c> header. Stores raw files, runs ingestion, returns the counts.
/// </summary>
public static class UploadEndpoints
{
    public static void MapUploadEndpoints(this IEndpointRouteBuilder app, string adminKey)
    {
        app.MapPost("/api/uploads", HandleAsync)
            .AddEndpointFilter(new ApiKeyFilter("X-Admin-Key", adminKey))
            .DisableAntiforgery();   // programmatic multipart clients (curl, CI) send no antiforgery token
    }

    private static async Task<IResult> HandleAsync(
        string? asOf,
        IFormFile? sales,
        IFormFile? inventory,
        IFormFile? suppliers,
        IngestionService ingestion,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(asOf)
            || !DateOnly.TryParseExact(asOf, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var asOfDate))
        {
            return Results.BadRequest(new { error = "Query parameter 'asOf' is required in yyyy-MM-dd format." });
        }

        if (sales is null || inventory is null || suppliers is null)
        {
            return Results.BadRequest(new { error = "Three form files are required: sales, inventory, suppliers." });
        }

        var result = await ingestion.IngestAsync(asOf, asOfDate, sales, inventory, suppliers, ct);
        return Results.Ok(result);
    }
}
