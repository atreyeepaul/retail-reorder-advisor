using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Reorder.Core.Advisor;
using Reorder.Core.Models;
using Reorder.Web.Infrastructure;
using Reorder.Web.Services;

namespace Reorder.Web.Tests;

public sealed class RecommendationCacheTests
{
    private static RunResult Sample(int skuCount) => new(
        new RunSummary(
            "2026-09-05", default, skuCount,
            new Dictionary<string, int>
            {
                ["URGENT"] = 0,
                ["STOCKOUT_RISK"] = 0,
                ["DEAD_STOCK"] = 0,
                ["OVERSTOCK"] = 0,
                ["HEALTHY"] = skuCount,
            },
            0m, 0m, 0m, 0, 0),
        []);

    private static void WriteLatest(InMemoryBlobStore blobs, RunResult result) =>
        blobs.WriteTextAsync(
            Containers.Recommendations, "latest.json",
            JsonSerializer.Serialize(result, AdvisorJsonContext.Default.RunResult)).GetAwaiter().GetResult();

    private static RecommendationCache NewCache(InMemoryBlobStore blobs) =>
        new(blobs, new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task Returns_null_when_no_latest_json()
    {
        var cache = NewCache(new InMemoryBlobStore());

        Assert.Null(await cache.GetAsync());
    }

    [Fact]
    public async Task Caches_until_invalidated()
    {
        var blobs = new InMemoryBlobStore();
        var cache = NewCache(blobs);

        WriteLatest(blobs, Sample(1));
        Assert.Equal(1, (await cache.GetAsync())!.Summary.SkuCount);

        // Overwrite the blob without invalidating → the cache still serves the old value.
        WriteLatest(blobs, Sample(2));
        Assert.Equal(1, (await cache.GetAsync())!.Summary.SkuCount);

        // Invalidate → the next read reflects the new blob.
        cache.Invalidate();
        Assert.Equal(2, (await cache.GetAsync())!.Summary.SkuCount);
    }
}
