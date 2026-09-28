using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Reorder.Core;
using Reorder.Core.Advisor;
using Reorder.Core.Ingestion;
using Reorder.Web.Infrastructure;

namespace Reorder.Web.Services;

/// <summary>
/// Runs one advisor pass: under a blob-lease lock, loads the normalized dataset, runs the pure
/// <see cref="AdvisorEngine"/>, and writes the dated snapshot plus <c>recommendations/latest.json</c>.
/// </summary>
public sealed class AdvisorJob(IBlobStore blobs, IOptions<AdvisorOptions> options, ILogger<AdvisorJob> logger)
{
    private const string LockPath = "advisor.lock";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);
    private readonly AdvisorOptions _options = options.Value;

    public async Task<AdvisorRunOutcome> RunAsync(DateOnly? asOf, CancellationToken ct = default)
    {
        var lease = await blobs.TryAcquireLeaseAsync(Containers.Locks, LockPath, LeaseDuration, ct);
        if (lease is null)
        {
            logger.LogWarning("Advisor run skipped: a run is already in progress.");
            return AdvisorRunOutcome.Locked();
        }

        await using (lease)
        {
            string? asOfStr = asOf?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                ?? await FindLatestAsOfAsync(ct);
            if (asOfStr is null)
            {
                return AdvisorRunOutcome.NotFound("No normalized data has been ingested yet.");
            }

            string? normalizedJson = await blobs.ReadTextAsync(Containers.Normalized, $"{asOfStr}.json", ct);
            if (normalizedJson is null)
            {
                return AdvisorRunOutcome.NotFound($"No normalized data for {asOfStr}.");
            }

            var dataset = JsonSerializer.Deserialize(normalizedJson, IngestionJsonContext.Default.NormalizedDataset)!;

            int quarantinedRows = 0;
            string? quarantineJson = await blobs.ReadTextAsync(Containers.Quarantine, $"{asOfStr}.json", ct);
            if (quarantineJson is not null)
            {
                var report = JsonSerializer.Deserialize(quarantineJson, IngestionJsonContext.Default.QuarantineReport);
                quarantinedRows = report?.TotalRejected ?? 0;
            }

            var asOfDate = DateOnly.ParseExact(dataset.AsOf, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            var stopwatch = Stopwatch.StartNew();
            var result = AdvisorEngine.Run(dataset, _options, asOfDate);
            stopwatch.Stop();

            var summary = result.Summary with
            {
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                QuarantinedRows = quarantinedRows,
                RunDurationMs = stopwatch.ElapsedMilliseconds,
            };
            var final = result with { Summary = summary };

            string json = JsonSerializer.Serialize(final, AdvisorJsonContext.Default.RunResult);
            await blobs.WriteTextAsync(Containers.Recommendations, $"{asOfStr}.json", json, ct: ct);
            await blobs.WriteTextAsync(Containers.Recommendations, "latest.json", json, ct: ct);

            logger.LogInformation(
                "AdvisorRunCompleted asOf={AsOf} skuCount={SkuCount} countsByFlag={CountsByFlag} quarantinedRows={QuarantinedRows} durationMs={DurationMs}",
                summary.AsOf, summary.SkuCount, FormatCounts(summary.CountsByFlag),
                summary.QuarantinedRows, summary.RunDurationMs);

            return AdvisorRunOutcome.Ok(summary);
        }
    }

    // Flatten counts-by-flag to a compact string so it lands as one App Insights custom dimension.
    private static string FormatCounts(IReadOnlyDictionary<string, int> counts) =>
        string.Join(" ", counts.Select(kv => $"{kv.Key}={kv.Value}"));

    // Picks the newest normalized/{yyyy-MM-dd}.json when no explicit asOf is given.
    private async Task<string?> FindLatestAsOfAsync(CancellationToken ct)
    {
        var names = await blobs.ListAsync(Containers.Normalized, ct);
        DateOnly? latest = null;
        foreach (var name in names)
        {
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string stem = name[..^".json".Length];
            if (DateOnly.TryParseExact(stem, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && (latest is null || date > latest))
            {
                latest = date;
            }
        }

        return latest?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
