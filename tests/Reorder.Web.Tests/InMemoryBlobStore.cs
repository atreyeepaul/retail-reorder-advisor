using System.Collections.Concurrent;
using Reorder.Web.Infrastructure;

namespace Reorder.Web.Tests;

/// <summary>A dictionary-backed <see cref="IBlobStore"/> for tests: no Azurite required.</summary>
public sealed class InMemoryBlobStore : IBlobStore
{
    private readonly ConcurrentDictionary<string, string> _blobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _leases = new(StringComparer.Ordinal);

    public Task SaveRawAsync(string asOf, string fileName, Stream content, CancellationToken ct = default)
    {
        using var reader = new StreamReader(content);
        _blobs[Key(Containers.Raw, $"{asOf}/{fileName}")] = reader.ReadToEnd();
        return Task.CompletedTask;
    }

    public Task WriteTextAsync(string container, string path, string content,
        string contentType = "application/json", CancellationToken ct = default)
    {
        _blobs[Key(container, path)] = content;
        return Task.CompletedTask;
    }

    public Task<string?> ReadTextAsync(string container, string path, CancellationToken ct = default) =>
        Task.FromResult(_blobs.TryGetValue(Key(container, path), out var value) ? value : null);

    public Task<bool> ExistsAsync(string container, string path, CancellationToken ct = default) =>
        Task.FromResult(_blobs.ContainsKey(Key(container, path)));

    public Task<IReadOnlyList<string>> ListAsync(string container, CancellationToken ct = default)
    {
        string prefix = container + "/";
        IReadOnlyList<string> names = _blobs.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => k[prefix.Length..])
            .ToList();
        return Task.FromResult(names);
    }

    public Task<IBlobLease?> TryAcquireLeaseAsync(
        string container, string path, TimeSpan duration, CancellationToken ct = default)
    {
        string key = Key(container, path);
        IBlobLease? lease = _leases.TryAdd(key, 0) ? new Lease(this, key) : null;
        return Task.FromResult(lease);
    }

    public bool Has(string container, string path) => _blobs.ContainsKey(Key(container, path));

    public string? Get(string container, string path) =>
        _blobs.TryGetValue(Key(container, path), out var value) ? value : null;

    public bool IsLeased(string container, string path) => _leases.ContainsKey(Key(container, path));

    private static string Key(string container, string path) => $"{container}/{path}";

    private sealed class Lease(InMemoryBlobStore store, string key) : IBlobLease
    {
        public ValueTask DisposeAsync()
        {
            store._leases.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }
    }
}
