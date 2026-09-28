using System.Collections.Concurrent;
using Reorder.Web.Infrastructure;

namespace Reorder.Web.Tests;

/// <summary>A dictionary-backed <see cref="IBlobStore"/> for tests: no Azurite required.</summary>
public sealed class InMemoryBlobStore : IBlobStore
{
    private readonly ConcurrentDictionary<string, string> _blobs = new(StringComparer.Ordinal);

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

    public bool Has(string container, string path) => _blobs.ContainsKey(Key(container, path));

    public string? Get(string container, string path) =>
        _blobs.TryGetValue(Key(container, path), out var value) ? value : null;

    private static string Key(string container, string path) => $"{container}/{path}";
}
