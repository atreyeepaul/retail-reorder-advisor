using Reorder.Core.Models;

namespace Reorder.Web.Infrastructure;

/// <summary>
/// Posts run notifications to a chat webhook. Implementations MUST never throw: a webhook failure is
/// logged and swallowed so it can't fail the advisor or ingestion (spec §7.2).
/// </summary>
public interface INotifier
{
    /// <summary>Posts the post-run digest: flag counts, the top urgent SKUs, and a dashboard link.</summary>
    Task PostDigestAsync(RunSummary summary, IReadOnlyList<Recommendation> topUrgent, CancellationToken ct = default);

    /// <summary>Posts the alert sent instead of a digest when ingestion aborts on too many rejects.</summary>
    Task PostAlertAsync(IngestionAlert alert, CancellationToken ct = default);
}

/// <summary>
/// The facts an ingestion-abort alert needs. Defined here (not in <c>Services</c>) so
/// <see cref="INotifier"/> has no dependency on the ingestion pipeline.
/// </summary>
public sealed record IngestionAlert(string AsOf, int SalesTotal, int SalesRejected, double RejectRatio, double Threshold);
