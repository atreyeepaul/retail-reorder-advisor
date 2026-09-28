using System.Globalization;

namespace Reorder.DataGen;

internal sealed record WriteSummary(
    IReadOnlyList<DateOnly> AsOfDates,
    string LatestFolder,
    int CleanRowsInLatest,
    IReadOnlyDictionary<string, int> DirtyRowsInLatest);

/// <summary>
/// Writes one folder per as-of date. Each folder is a complete upload: the full sales
/// history up to that date, the end-of-day inventory snapshot, and the supplier list.
/// </summary>
internal static class DatasetWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static WriteSummary Write(IReadOnlyList<Sku> skus, SimulationResult sim, GeneratorOptions options)
    {
        PrepareOutputDirectory(options.OutputDirectory);

        // Build clean and dirty rows once, per day, so every folder is consistent with the others.
        var cleanByDay = new List<SalesRow>[options.TotalDays];
        var dirtyByDay = new List<SalesRow>[options.TotalDays];
        var dirtyRng = new Random(options.Seed ^ 0x5EED);

        for (int d = 0; d < options.TotalDays; d++)
        {
            string date = options.DateOf(d).ToString("yyyy-MM-dd", Inv);
            cleanByDay[d] = [];
            dirtyByDay[d] = [];

            for (int i = 0; i < skus.Count; i++)
            {
                int sold = sim.Sales[i, d];
                if (sold <= 0)
                {
                    continue; // days with no sales are simply absent, as in a real POS export
                }

                var row = new SalesRow(date, skus[i].Code, sold.ToString(Inv), skus[i].UnitPrice.ToString("0.00", Inv));
                cleanByDay[d].Add(row);

                if (dirtyRng.NextDouble() < options.DirtyRowRate)
                {
                    dirtyByDay[d].Add(DirtyRows.From(row, dirtyRng));
                }
            }
        }

        var asOfDates = new List<DateOnly>();
        string latestFolder = string.Empty;
        int cleanInLatest = 0;
        var dirtyInLatest = new Dictionary<string, int>();

        for (int a = options.HistoryEndIndex; a < options.TotalDays; a++)
        {
            var asOf = options.DateOf(a);
            string folder = Path.Combine(options.OutputDirectory, asOf.ToString("yyyy-MM-dd", Inv));
            Directory.CreateDirectory(folder);

            WriteSuppliers(Path.Combine(folder, "suppliers.csv"));
            WriteInventory(Path.Combine(folder, "inventory.csv"), skus, sim, a);

            var salesRows = new List<SalesRow>();
            for (int d = 0; d <= a; d++)
            {
                salesRows.AddRange(cleanByDay[d]);
                salesRows.AddRange(dirtyByDay[d]);
            }

            var futureRng = new Random(unchecked(options.Seed + (a * 7919)));
            for (int f = 0; f < 2; f++)
            {
                salesRows.Add(DirtyRows.FutureDated(asOf, futureRng.Pick(skus), futureRng));
            }

            Csv.Write(
                Path.Combine(folder, "sales.csv"),
                "date,sku,qty_sold,unit_price",
                salesRows.Select(r => r.ToFields()));

            asOfDates.Add(asOf);
            latestFolder = folder;
            cleanInLatest = salesRows.Count(r => r.Dirty is null);
            dirtyInLatest = salesRows
                .Where(r => r.Dirty is not null)
                .GroupBy(r => r.Dirty!.Value.ToString())
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        return new WriteSummary(asOfDates, latestFolder, cleanInLatest, dirtyInLatest);
    }

    private static void WriteSuppliers(string path) => Csv.Write(
        path,
        "supplier_id,name,lead_time_days,moq,pack_size",
        Catalog.Suppliers.Select(s => new[]
        {
            s.Id,
            s.Name,
            s.PublishedLeadTimeDays?.ToString(Inv) ?? string.Empty,
            s.Moq.ToString(Inv),
            s.PackSize.ToString(Inv),
        }));

    private static void WriteInventory(string path, IReadOnlyList<Sku> skus, SimulationResult sim, int dayIndex) => Csv.Write(
        path,
        "sku,name,category,on_hand,unit_cost,supplier_id",
        skus.Select((s, i) => new[]
        {
            s.Code,
            s.Name,
            s.Category,
            sim.OnHandEndOfDay[i, dayIndex].ToString(Inv),
            s.UnitCost.ToString("0.00", Inv),
            s.Supplier.Id,
        }));

    /// <summary>Removes folders from a previous run (only yyyy-MM-dd folders and ground-truth.json).</summary>
    private static void PrepareOutputDirectory(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        foreach (string dir in Directory.GetDirectories(outputDirectory))
        {
            if (DateOnly.TryParseExact(Path.GetFileName(dir), "yyyy-MM-dd", Inv, DateTimeStyles.None, out _))
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        string groundTruth = Path.Combine(outputDirectory, GroundTruthWriter.FileName);
        if (File.Exists(groundTruth))
        {
            File.Delete(groundTruth);
        }
    }
}
