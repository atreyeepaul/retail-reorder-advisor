using System.Text.Json;
using Microsoft.Extensions.Options;
using Reorder.Core;
using Reorder.Core.Ingestion;
using Reorder.Core.Models;
using Reorder.Web.Infrastructure;

namespace Reorder.Web.Services;

/// <summary>
/// Runs one ingestion: stores the raw uploads, validates all three CSVs, writes the quarantine
/// report, and — unless the sales reject ratio exceeds the threshold — the normalized dataset.
/// </summary>
public sealed class IngestionService(IBlobStore blobs, IOptions<AdvisorOptions> options)
{
    private const int RejectsSampleSize = 50;
    private readonly AdvisorOptions _options = options.Value;

    public async Task<IngestionResult> IngestAsync(
        string asOf, DateOnly asOfDate,
        IFormFile sales, IFormFile inventory, IFormFile suppliers,
        CancellationToken ct = default)
    {
        byte[] salesBytes = await BufferAsync(sales, ct);
        byte[] inventoryBytes = await BufferAsync(inventory, ct);
        byte[] supplierBytes = await BufferAsync(suppliers, ct);

        await SaveRawAsync(asOf, "sales.csv", salesBytes, ct);
        await SaveRawAsync(asOf, "inventory.csv", inventoryBytes, ct);
        await SaveRawAsync(asOf, "suppliers.csv", supplierBytes, ct);

        var supplierResult = RowValidator.ValidateSuppliers(CsvReaders.ReadSuppliers(new MemoryStream(supplierBytes)));
        var supplierIds = supplierResult.Accepted.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        var inventoryResult = RowValidator.ValidateInventory(
            CsvReaders.ReadInventory(new MemoryStream(inventoryBytes)), supplierIds);
        var knownSkus = inventoryResult.Accepted.Select(i => i.Sku).ToHashSet(StringComparer.Ordinal);

        var salesResult = RowValidator.ValidateSales(
            CsvReaders.ReadSales(new MemoryStream(salesBytes)), knownSkus, asOfDate);

        var allRejects = new List<Reject>(
            salesResult.Rejected.Count + inventoryResult.Rejected.Count + supplierResult.Rejected.Count);
        allRejects.AddRange(supplierResult.Rejected);
        allRejects.AddRange(inventoryResult.Rejected);
        allRejects.AddRange(salesResult.Rejected);

        // Quarantine is always written — even on abort — so a bad batch can be diagnosed.
        var quarantine = new QuarantineReport(asOf, allRejects.Count, allRejects);
        await blobs.WriteTextAsync(Containers.Quarantine, $"{asOf}.json",
            JsonSerializer.Serialize(quarantine, IngestionJsonContext.Default.QuarantineReport), ct: ct);

        bool aborted = salesResult.ExceedsThreshold(_options.MaxRejectRatio);
        if (!aborted)
        {
            var dataset = new NormalizedDataset(
                asOf, DateTimeOffset.UtcNow,
                salesResult.Accepted, inventoryResult.Accepted, supplierResult.Accepted);
            await blobs.WriteTextAsync(Containers.Normalized, $"{asOf}.json",
                JsonSerializer.Serialize(dataset, IngestionJsonContext.Default.NormalizedDataset), ct: ct);
        }

        return new IngestionResult(
            asOf,
            aborted ? IngestionResult.StatusAborted : IngestionResult.StatusOk,
            Counts(salesResult),
            Counts(inventoryResult),
            Counts(supplierResult),
            _options.MaxRejectRatio,
            allRejects.Take(RejectsSampleSize).ToList());
    }

    private async Task SaveRawAsync(string asOf, string fileName, byte[] content, CancellationToken ct)
    {
        using var stream = new MemoryStream(content);
        await blobs.SaveRawAsync(asOf, fileName, stream, ct);
    }

    private static FileCounts Counts<T>(ValidationResult<T> result) =>
        new(result.Total, result.Accepted.Count, result.Rejected.Count, result.RejectRatio);

    private static async Task<byte[]> BufferAsync(IFormFile file, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return ms.ToArray();
    }
}
