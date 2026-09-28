using System.Collections.Concurrent;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace Reorder.Web.Infrastructure;

/// <summary>
/// <see cref="IBlobStore"/> backed by Azure Blob Storage. Uses the Azurite connection string
/// when one is configured (local dev), otherwise the account URI plus the app's managed identity.
/// </summary>
public sealed class BlobStore : IBlobStore
{
    private readonly BlobServiceClient _service;
    private readonly ConcurrentDictionary<string, bool> _ensuredContainers = new(StringComparer.Ordinal);

    public BlobStore(IOptions<StorageOptions> options)
    {
        var storage = options.Value;

        _service = !string.IsNullOrWhiteSpace(storage.ConnectionString)
            ? new BlobServiceClient(storage.ConnectionString)
            : new BlobServiceClient(new Uri(storage.BlobServiceUri), new DefaultAzureCredential());
    }

    public async Task SaveRawAsync(string asOf, string fileName, Stream content, CancellationToken ct = default)
    {
        var container = await GetContainerAsync(Containers.Raw, ct);
        var blob = container.GetBlobClient($"{asOf}/{fileName}");
        await blob.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "text/csv" },
        }, ct);
    }

    public async Task WriteTextAsync(string container, string path, string content,
        string contentType = "application/json", CancellationToken ct = default)
    {
        var client = await GetContainerAsync(container, ct);
        var blob = client.GetBlobClient(path);
        await blob.UploadAsync(BinaryData.FromString(content), new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        }, ct);
    }

    public async Task<string?> ReadTextAsync(string container, string path, CancellationToken ct = default)
    {
        var blob = _service.GetBlobContainerClient(container).GetBlobClient(path);
        if (!await blob.ExistsAsync(ct))
        {
            return null;
        }

        var response = await blob.DownloadContentAsync(ct);
        return response.Value.Content.ToString();
    }

    public async Task<bool> ExistsAsync(string container, string path, CancellationToken ct = default)
    {
        var blob = _service.GetBlobContainerClient(container).GetBlobClient(path);
        return await blob.ExistsAsync(ct);
    }

    // Containers exist in Azure (created by Bicep); locally against Azurite they must be created.
    // CreateIfNotExists is attempted once per container name per process.
    private async Task<BlobContainerClient> GetContainerAsync(string container, CancellationToken ct)
    {
        var client = _service.GetBlobContainerClient(container);
        if (_ensuredContainers.TryAdd(container, true))
        {
            await client.CreateIfNotExistsAsync(cancellationToken: ct);
        }

        return client;
    }
}
