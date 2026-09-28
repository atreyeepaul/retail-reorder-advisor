using System.Globalization;
using Reorder.Core.Models;

namespace Reorder.Core.Ingestion;

/// <summary>
/// Pure validation of raw CSV rows into typed records, with one reject reason per bad row.
/// No I/O, no Azure, no ASP.NET — every rule here is unit-tested directly.
/// </summary>
/// <remarks>
/// Each row is assigned exactly one <see cref="RejectReason"/> in this precedence:
/// MISSING_FIELD → BAD_TYPE → NEGATIVE_QTY → FUTURE_DATE → UNKNOWN_SKU → DUPLICATE_KEY
/// (inventory adds UNKNOWN_SUPPLIER before DUPLICATE_KEY). Duplicate detection runs only
/// over rows that pass every field check, keeping the first occurrence.
/// </remarks>
public static class RowValidator
{
    private const string SalesFile = "sales.csv";
    private const string InventoryFile = "inventory.csv";
    private const string SuppliersFile = "suppliers.csv";

    private const NumberStyles IntStyles = NumberStyles.Integer;
    private const NumberStyles MoneyStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint
        | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

    private const int MinLeadTimeDays = 1;
    private const int MaxLeadTimeDays = 120;

    /// <summary>Validate suppliers.csv. Blank lead time → null; blank moq/pack size → 1.</summary>
    public static ValidationResult<Supplier> ValidateSuppliers(IEnumerable<RawSupplierRow> rows)
    {
        var accepted = new List<Supplier>();
        var rejects = new List<Reject>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            string id = row.SupplierId.Trim();
            string name = row.Name.Trim();

            if (id.Length == 0 || name.Length == 0)
            {
                rejects.Add(new Reject(SuppliersFile, row.Line, RejectReason.MissingField, row.Raw));
                continue;
            }

            // lead_time_days: optional; blank → null; otherwise int within 1..120.
            int? leadTime = null;
            if (!IsBlank(row.LeadTimeDays))
            {
                if (!TryInt(row.LeadTimeDays, out int lt) || lt < MinLeadTimeDays || lt > MaxLeadTimeDays)
                {
                    rejects.Add(new Reject(SuppliersFile, row.Line, RejectReason.BadType, row.Raw));
                    continue;
                }
                leadTime = lt;
            }

            // moq / pack_size: optional; blank → 1; otherwise int ≥ 1.
            if (!TryOptionalPositiveInt(row.Moq, out int moq)
                || !TryOptionalPositiveInt(row.PackSize, out int packSize))
            {
                rejects.Add(new Reject(SuppliersFile, row.Line, RejectReason.BadType, row.Raw));
                continue;
            }

            if (!seenIds.Add(id))
            {
                rejects.Add(new Reject(SuppliersFile, row.Line, RejectReason.DuplicateKey, row.Raw));
                continue;
            }

            accepted.Add(new Supplier(id, name, leadTime, moq, packSize));
        }

        return new ValidationResult<Supplier>(accepted, rejects);
    }

    /// <summary>Validate inventory.csv against the known supplier ids.</summary>
    public static ValidationResult<InventoryItem> ValidateInventory(
        IEnumerable<RawInventoryRow> rows, ISet<string> supplierIds)
    {
        var accepted = new List<InventoryItem>();
        var rejects = new List<Reject>();
        var seenSkus = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            string sku = row.Sku.Trim();
            string name = row.Name.Trim();
            string supplierId = row.SupplierId.Trim();
            string? category = IsBlank(row.Category) ? null : row.Category.Trim();

            if (sku.Length == 0 || name.Length == 0 || IsBlank(row.OnHand)
                || IsBlank(row.UnitCost) || supplierId.Length == 0)
            {
                rejects.Add(new Reject(InventoryFile, row.Line, RejectReason.MissingField, row.Raw));
                continue;
            }

            if (!TryInt(row.OnHand, out int onHand) || onHand < 0
                || !TryDecimal(row.UnitCost, out decimal unitCost) || unitCost <= 0m)
            {
                rejects.Add(new Reject(InventoryFile, row.Line, RejectReason.BadType, row.Raw));
                continue;
            }

            if (!supplierIds.Contains(supplierId))
            {
                rejects.Add(new Reject(InventoryFile, row.Line, RejectReason.UnknownSupplier, row.Raw));
                continue;
            }

            if (!seenSkus.Add(sku))
            {
                rejects.Add(new Reject(InventoryFile, row.Line, RejectReason.DuplicateKey, row.Raw));
                continue;
            }

            accepted.Add(new InventoryItem(sku, name, category, onHand, unitCost, supplierId));
        }

        return new ValidationResult<InventoryItem>(accepted, rejects);
    }

    /// <summary>Validate sales.csv against the known SKUs and the upload's as-of date.</summary>
    public static ValidationResult<SalesRecord> ValidateSales(
        IEnumerable<RawSalesRow> rows, ISet<string> knownSkus, DateOnly asOf)
    {
        var accepted = new List<SalesRecord>();
        var rejects = new List<Reject>();
        var seenKeys = new HashSet<(DateOnly, string)>();

        foreach (var row in rows)
        {
            string sku = row.Sku.Trim();

            if (IsBlank(row.Date) || sku.Length == 0 || IsBlank(row.QtySold) || IsBlank(row.UnitPrice))
            {
                rejects.Add(new Reject(SalesFile, row.Line, RejectReason.MissingField, row.Raw));
                continue;
            }

            if (!TryDateIso(row.Date, out DateOnly date)
                || !TryInt(row.QtySold, out int qty)
                || !TryDecimal(row.UnitPrice, out decimal price) || price <= 0m)
            {
                rejects.Add(new Reject(SalesFile, row.Line, RejectReason.BadType, row.Raw));
                continue;
            }

            if (qty < 0)
            {
                rejects.Add(new Reject(SalesFile, row.Line, RejectReason.NegativeQty, row.Raw));
                continue;
            }

            if (date > asOf)
            {
                rejects.Add(new Reject(SalesFile, row.Line, RejectReason.FutureDate, row.Raw));
                continue;
            }

            if (!knownSkus.Contains(sku))
            {
                rejects.Add(new Reject(SalesFile, row.Line, RejectReason.UnknownSku, row.Raw));
                continue;
            }

            if (!seenKeys.Add((date, sku)))
            {
                rejects.Add(new Reject(SalesFile, row.Line, RejectReason.DuplicateKey, row.Raw));
                continue;
            }

            accepted.Add(new SalesRecord(date, sku, qty, price));
        }

        return new ValidationResult<SalesRecord>(accepted, rejects);
    }

    private static bool IsBlank(string value) => string.IsNullOrWhiteSpace(value);

    private static bool TryDateIso(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);

    private static bool TryInt(string value, out int result) =>
        int.TryParse(value, IntStyles, CultureInfo.InvariantCulture, out result);

    private static bool TryDecimal(string value, out decimal result) =>
        decimal.TryParse(value, MoneyStyles, CultureInfo.InvariantCulture, out result);

    // Blank → default 1; otherwise must be an int ≥ 1.
    private static bool TryOptionalPositiveInt(string value, out int result)
    {
        if (IsBlank(value))
        {
            result = 1;
            return true;
        }

        return TryInt(value, out result) && result >= 1;
    }
}
