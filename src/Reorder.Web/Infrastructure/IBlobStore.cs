namespace Reorder.Web.Infrastructure;

/// <summary>
/// Blob storage access, abstracted so tests can substitute an in-memory fake.
/// Paths are relative to a container; container names are on <see cref="Containers"/>.
/// </summary>
public interface IBlobStore
{
    /// <summary>Store an uploaded file verbatim under <c>raw/{asOf}/{fileName}</c>.</summary>
    Task SaveRawAsync(string asOf, string fileName, Stream content, CancellationToken ct = default);

    /// <summary>Write (overwriting) a text blob at <paramref name="path"/> in <paramref name="container"/>.</summary>
    Task WriteTextAsync(string container, string path, string content,
        string contentType = "application/json", CancellationToken ct = default);

    /// <summary>Read a text blob, or null when it does not exist.</summary>
    Task<string?> ReadTextAsync(string container, string path, CancellationToken ct = default);

    /// <summary>True when a blob exists at <paramref name="path"/> in <paramref name="container"/>.</summary>
    Task<bool> ExistsAsync(string container, string path, CancellationToken ct = default);

    /// <summary>The blob paths (names) present in <paramref name="container"/>.</summary>
    Task<IReadOnlyList<string>> ListAsync(string container, CancellationToken ct = default);

    /// <summary>
    /// Try to take an exclusive lease on a blob (creating it if absent). Returns a handle that
    /// releases the lease when disposed, or <c>null</c> when the blob is already leased.
    /// </summary>
    Task<IBlobLease?> TryAcquireLeaseAsync(
        string container, string path, TimeSpan duration, CancellationToken ct = default);
}

/// <summary>A held blob lease; disposing releases it.</summary>
public interface IBlobLease : IAsyncDisposable;

/// <summary>The five blob containers provisioned by the Bicep infrastructure.</summary>
public static class Containers
{
    public const string Raw = "raw";
    public const string Normalized = "normalized";
    public const string Quarantine = "quarantine";
    public const string Recommendations = "recommendations";
    public const string Locks = "locks";

    public static readonly IReadOnlyList<string> All =
        [Raw, Normalized, Quarantine, Recommendations, Locks];
}
