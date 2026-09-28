using System.Globalization;
using Reorder.Core;
using Reorder.Core.Advisor;
using Reorder.Core.Models;
using CoreSupplier = Reorder.Core.Models.Supplier;

namespace Reorder.DataGen;

/// <summary>
/// Replays the pure advisor over the simulation window and scores early-warning coverage:
/// the share of evaluable stockouts flagged URGENT or STOCKOUT_RISK on an as-of date at least
/// 3 days before the stockout. Target ≥ 80%. Deterministic — reconstructs the same dataset the
/// generator produced from the seed, so no files are read.
/// </summary>
internal static class Backtest
{
    private const double Target = 0.80;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int Run(GeneratorOptions options)
    {
        var errors = options.Validate().ToList();
        if (errors.Count > 0)
        {
            throw new CliException(string.Join(Environment.NewLine, errors));
        }

        // Reconstruct the exact dataset the generator produced (deterministic from the seed).
        var rng = new Random(options.Seed);
        var skus = Catalog.BuildSkus(options.SkuCount, rng);
        Catalog.AssignScenarios(skus, options, rng);
        var sim = Simulator.Run(skus, options, rng);

        // Suppliers as the advisor sees them: PUBLISHED lead time (blank → default lead time).
        var suppliers = Catalog.Suppliers
            .Select(s => new CoreSupplier(s.Id, s.Name, s.PublishedLeadTimeDays, s.Moq, s.PackSize))
            .ToList();

        // For each as-of day, the set of SKUs the advisor flagged URGENT or STOCKOUT_RISK.
        var flaggedByAsOf = new Dictionary<int, HashSet<string>>();
        var advisorOptions = new AdvisorOptions();
        for (int a = options.HistoryEndIndex; a < options.TotalDays; a++)
        {
            var dataset = BuildDataset(skus, sim, suppliers, options, a);
            var result = AdvisorEngine.Run(dataset, advisorOptions, options.DateOf(a));
            flaggedByAsOf[a] = result.Recommendations
                .Where(r => r.Flag is Flag.Urgent or Flag.StockoutRisk)
                .Select(r => r.Sku)
                .ToHashSet(StringComparer.Ordinal);
        }

        // Evaluable stockouts: those with an as-of date at least 3 days before them
        // (same rule the ground truth uses: DayIndex - 3 >= HistoryEndIndex).
        var evaluable = sim.Stockouts
            .Where(e => e.DayIndex - 3 >= options.HistoryEndIndex)
            .OrderBy(e => e.DayIndex)
            .ThenBy(e => e.Sku, StringComparer.Ordinal)
            .ToList();

        int hits = 0;
        var detail = new List<string>();
        foreach (var stockout in evaluable)
        {
            int? firstFlaggedAsOf = null;
            for (int a = options.HistoryEndIndex; a <= stockout.DayIndex - 3; a++)
            {
                if (flaggedByAsOf[a].Contains(stockout.Sku))
                {
                    firstFlaggedAsOf = a;
                    break;
                }
            }

            bool hit = firstFlaggedAsOf is not null;
            if (hit)
            {
                hits++;
            }

            string when = firstFlaggedAsOf is int f
                ? $"first flagged {Fmt(options.DateOf(f))}"
                : "never flagged ≥3 days ahead";
            detail.Add($"    {(hit ? "HIT " : "MISS")} {stockout.Sku} stockout {Fmt(options.DateOf(stockout.DayIndex))} ({stockout.Cause}); {when}");
        }

        double coverage = evaluable.Count == 0 ? 0d : (double)hits / evaluable.Count;
        bool pass = coverage >= Target;

        Console.WriteLine("Retail Reorder Advisor — backtest");
        Console.WriteLine($"  Seed ............... {options.Seed}");
        Console.WriteLine($"  As-of dates ........ {options.SimulationDays + 1} ({Fmt(options.DateOf(options.HistoryEndIndex))} .. {Fmt(options.DateOf(options.TotalDays - 1))})");
        Console.WriteLine($"  Evaluable stockouts  {evaluable.Count}");
        Console.WriteLine($"  Flagged ≥3d ahead .. {hits}");
        Console.WriteLine($"  Coverage ........... {coverage.ToString("P1", Inv)} (target ≥ {Target.ToString("P0", Inv)})  {(pass ? "PASS" : "FAIL")}");
        foreach (string line in detail)
        {
            Console.WriteLine(line);
        }

        return pass ? 0 : 1;
    }

    private static NormalizedDataset BuildDataset(
        IReadOnlyList<Sku> skus, SimulationResult sim, IReadOnlyList<CoreSupplier> suppliers,
        GeneratorOptions options, int asOfIndex)
    {
        var inventory = new List<InventoryItem>(skus.Count);
        var sales = new List<SalesRecord>();

        for (int i = 0; i < skus.Count; i++)
        {
            var sku = skus[i];
            inventory.Add(new InventoryItem(
                sku.Code, sku.Name, sku.Category, sim.OnHandEndOfDay[i, asOfIndex], sku.UnitCost, sku.Supplier.Id));

            for (int d = 0; d <= asOfIndex; d++)
            {
                int sold = sim.Sales[i, d];
                if (sold > 0)
                {
                    sales.Add(new SalesRecord(options.DateOf(d), sku.Code, sold, sku.UnitPrice));
                }
            }
        }

        return new NormalizedDataset(
            options.DateOf(asOfIndex).ToString("yyyy-MM-dd", Inv), default, sales, inventory, suppliers);
    }

    private static string Fmt(DateOnly date) => date.ToString("yyyy-MM-dd", Inv);
}
