using Reorder.Core.Models;

namespace Reorder.Web.Services;

/// <summary>
/// The result of an advisor run. <see cref="Status"/> is <c>"ok"</c> (summary attached),
/// <c>"locked"</c> (another run holds the lease), or <c>"not_found"</c> (no normalized data).
/// </summary>
public sealed record AdvisorRunOutcome(string Status, RunSummary? Summary, string? Message)
{
    public const string StatusOk = "ok";
    public const string StatusLocked = "locked";
    public const string StatusNotFound = "not_found";

    public static AdvisorRunOutcome Ok(RunSummary summary) => new(StatusOk, summary, null);

    public static AdvisorRunOutcome Locked() =>
        new(StatusLocked, null, "An advisor run is already in progress.");

    public static AdvisorRunOutcome NotFound(string message) => new(StatusNotFound, null, message);
}
