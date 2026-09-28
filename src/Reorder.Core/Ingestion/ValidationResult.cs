namespace Reorder.Core.Ingestion;

/// <summary>
/// The outcome of validating one file: the accepted typed rows and the rejected ones.
/// </summary>
public sealed record ValidationResult<T>(IReadOnlyList<T> Accepted, IReadOnlyList<Reject> Rejected)
{
    /// <summary>Total input rows considered (accepted + rejected).</summary>
    public int Total => Accepted.Count + Rejected.Count;

    /// <summary>Share of rows rejected, in [0, 1]. Zero when there were no rows.</summary>
    public double RejectRatio => Total == 0 ? 0d : (double)Rejected.Count / Total;

    /// <summary>True when the reject share is strictly greater than <paramref name="maxRatio"/>.</summary>
    public bool ExceedsThreshold(double maxRatio) => RejectRatio > maxRatio;
}
