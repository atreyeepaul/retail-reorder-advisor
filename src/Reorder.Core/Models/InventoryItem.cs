namespace Reorder.Core.Models;

/// <summary>A validated, typed inventory snapshot row for one SKU.</summary>
public sealed record InventoryItem(
    string Sku,
    string Name,
    string? Category,
    int OnHand,
    decimal UnitCost,
    string SupplierId);
