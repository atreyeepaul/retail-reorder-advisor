using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Reorder.Core.Advisor;
using Reorder.Core.Models;
using Reorder.Web.Infrastructure;

namespace Reorder.Web.Services;

/// <summary>
/// Serves the latest advisor output (<c>recommendations/latest.json</c>) from an in-memory cache,
/// so the read endpoints and dashboard don't hit blob storage on every request. Held for 10 minutes
/// and cleared by <see cref="AdvisorJob"/> after each run. A missing blob returns null (not cached).
/// </summary>
public sealed class RecommendationCache(IBlobStore blobs, IMemoryCache cache)
{
    private const string CacheKey = "recommendations:latest";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public async Task<RunResult?> GetAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out RunResult? cached))
        {
            return cached;
        }

        string? json = await blobs.ReadTextAsync(Containers.Recommendations, "latest.json", ct);
        if (json is null)
        {
            return null;   // no run yet — don't cache the absence
        }

        var result = JsonSerializer.Deserialize(json, AdvisorJsonContext.Default.RunResult);
        if (result is not null)
        {
            cache.Set(CacheKey, result, Ttl);
        }

        return result;
    }

    public void Invalidate() => cache.Remove(CacheKey);
}
