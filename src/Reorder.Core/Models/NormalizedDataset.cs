namespace Reorder.Core.Models;

/// <summary>
/// The validated output of ingestion for one as-of date, serialized to
/// <c>normalized/{asOf}.json</c> and consumed by the advisor job.
/// </summary>
public sealed record NormalizedDataset(
    string AsOf,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<SalesRecord> Sales,
    IReadOnlyList<InventoryItem> Inventory,
    IReadOnlyList<Supplier> Suppliers);
