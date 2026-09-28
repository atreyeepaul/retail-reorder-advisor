namespace Reorder.Core.Models;

/// <summary>
/// One SKU's reorder recommendation — the shape of each item in <c>latest.json</c>'s
/// <c>recommendations</c> array. Numeric display fields are rounded to 2 decimals; the flag,
/// suggested quantity, and stockout date are decided from full-precision values.
/// </summary>
public sealed record Recommendation(
    string Sku,
    string Name,
    string? Category,
    string SupplierId,
    string SupplierName,
    int OnHand,
    double Velocity,
    double Sigma,
    int LeadTimeDays,
    double SafetyStock,
    double ReorderPoint,
    double? DaysOfCover,
    int SuggestedQty,
    Flag Flag,
    Confidence Confidence,
    DateOnly? EstimatedStockoutDate,
    string Reason);
