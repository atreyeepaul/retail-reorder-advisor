using Reorder.Core.Ingestion;

namespace Reorder.Web.Services;

/// <summary>Accepted/rejected counts for one file.</summary>
public sealed record FileCounts(int Total, int Accepted, int Rejected, double RejectRatio);

/// <summary>
/// The response body of <c>POST /api/uploads</c>. <see cref="Status"/> is <c>"ok"</c> when the
/// normalized dataset was written, or <c>"aborted"</c> when the sales reject ratio exceeded the
/// threshold (normalized left unwritten). <see cref="RejectsSample"/> holds the first 50 rejects.
/// </summary>
public sealed record IngestionResult(
    string AsOf,
    string Status,
    FileCounts Sales,
    FileCounts Inventory,
    FileCounts Suppliers,
    double RejectThreshold,
    IReadOnlyList<Reject> RejectsSample)
{
    public const string StatusOk = "ok";
    public const string StatusAborted = "aborted";

    public bool Aborted => Status == StatusAborted;
}
