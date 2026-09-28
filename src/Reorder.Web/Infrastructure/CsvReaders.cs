using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Reorder.Core.Ingestion;

namespace Reorder.Web.Infrastructure;

/// <summary>
/// Reads the three ingestion CSVs into raw string rows. Parsing/typing is deliberately
/// left to <see cref="RowValidator"/>; this layer only turns CSV text into fields, capturing
/// each row's file line and original text for quarantine reporting.
/// </summary>
public static class CsvReaders
{
    // Lenient reading: never throw on a short/ragged row — a missing field becomes "" and the
    // validator classifies it. Blank lines (e.g. a trailing newline) are skipped.
    private static readonly CsvConfiguration Config = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        IgnoreBlankLines = true,
        MissingFieldFound = null,
        BadDataFound = null,
        DetectColumnCountChanges = false,
    };

    public static List<RawSalesRow> ReadSales(Stream stream)
    {
        var rows = new List<RawSalesRow>();
        foreach (var (csv, line, raw) in Records(stream))
        {
            rows.Add(new RawSalesRow(line, raw,
                Field(csv, "date"), Field(csv, "sku"), Field(csv, "qty_sold"), Field(csv, "unit_price")));
        }

        return rows;
    }

    public static List<RawInventoryRow> ReadInventory(Stream stream)
    {
        var rows = new List<RawInventoryRow>();
        foreach (var (csv, line, raw) in Records(stream))
        {
            rows.Add(new RawInventoryRow(line, raw,
                Field(csv, "sku"), Field(csv, "name"), Field(csv, "category"),
                Field(csv, "on_hand"), Field(csv, "unit_cost"), Field(csv, "supplier_id")));
        }

        return rows;
    }

    public static List<RawSupplierRow> ReadSuppliers(Stream stream)
    {
        var rows = new List<RawSupplierRow>();
        foreach (var (csv, line, raw) in Records(stream))
        {
            rows.Add(new RawSupplierRow(line, raw,
                Field(csv, "supplier_id"), Field(csv, "name"), Field(csv, "lead_time_days"),
                Field(csv, "moq"), Field(csv, "pack_size")));
        }

        return rows;
    }

    private static IEnumerable<(CsvReader Csv, int Line, string Raw)> Records(Stream stream)
    {
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, Config);

        csv.Read();
        csv.ReadHeader();
        while (csv.Read())
        {
            int line = csv.Parser.RawRow;                          // 1-based physical file line (header = 1)
            string raw = csv.Parser.RawRecord.TrimEnd('\r', '\n');
            yield return (csv, line, raw);
        }
    }

    private static string Field(CsvReader csv, string name) => csv.GetField(name) ?? string.Empty;
}
