namespace Reorder.Core.Models;

/// <summary>A validated, typed daily sales aggregate: one row per (date, sku).</summary>
public sealed record SalesRecord(DateOnly Date, string Sku, int QtySold, decimal UnitPrice);
