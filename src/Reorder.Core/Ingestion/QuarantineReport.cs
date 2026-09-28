namespace Reorder.Core.Ingestion;

/// <summary>
/// The rejected rows for one as-of date, serialized to <c>quarantine/{asOf}.json</c>.
/// </summary>
public sealed record QuarantineReport(string AsOf, int TotalRejected, IReadOnlyList<Reject> Rejects);
