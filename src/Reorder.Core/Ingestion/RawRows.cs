namespace Reorder.Core.Ingestion;

// Raw, unparsed rows straight from the CSV reader. Every field is a string so the
// validator (not the reader) owns type parsing and can emit MISSING_FIELD / BAD_TYPE.
// Line is the 1-based file line (header = 1); Raw is the original line text.

/// <summary>A raw sales.csv row: <c>date,sku,qty_sold,unit_price</c>.</summary>
public sealed record RawSalesRow(int Line, string Raw, string Date, string Sku, string QtySold, string UnitPrice);

/// <summary>A raw inventory.csv row: <c>sku,name,category,on_hand,unit_cost,supplier_id</c>.</summary>
public sealed record RawInventoryRow(
    int Line, string Raw, string Sku, string Name, string Category, string OnHand, string UnitCost, string SupplierId);

/// <summary>A raw suppliers.csv row: <c>supplier_id,name,lead_time_days,moq,pack_size</c>.</summary>
public sealed record RawSupplierRow(
    int Line, string Raw, string SupplierId, string Name, string LeadTimeDays, string Moq, string PackSize);
