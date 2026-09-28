using System.Globalization;
using Reorder.Web.Infrastructure;
using Reorder.Web.Services;

namespace Reorder.Web.Endpoints;

/// <summary>
/// <c>POST /api/jobs/run-advisor?asOf=yyyy-MM-dd</c>: runs the advisor on the given (or latest)
/// normalized data, guarded by the <c>X-Job-Key</c> header. Returns the run summary.
/// </summary>
public static class JobEndpoints
{
    public static void MapJobEndpoints(this IEndpointRouteBuilder app, string jobKey)
    {
        app.MapPost("/api/jobs/run-advisor", HandleAsync)
            .AddEndpointFilter(new ApiKeyFilter("X-Job-Key", jobKey))
            .DisableAntiforgery();
    }

    private static async Task<IResult> HandleAsync(string? asOf, AdvisorJob job, CancellationToken ct)
    {
        DateOnly? asOfDate = null;
        if (!string.IsNullOrWhiteSpace(asOf))
        {
            if (!DateOnly.TryParseExact(asOf, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return Results.BadRequest(new { error = "Query parameter 'asOf' must be in yyyy-MM-dd format." });
            }

            asOfDate = parsed;
        }

        var outcome = await job.RunAsync(asOfDate, ct);
        return outcome.Status switch
        {
            AdvisorRunOutcome.StatusOk => Results.Ok(outcome.Summary),
            AdvisorRunOutcome.StatusLocked => Results.Conflict(new { error = outcome.Message }),
            AdvisorRunOutcome.StatusNotFound => Results.NotFound(new { error = outcome.Message }),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }
}
