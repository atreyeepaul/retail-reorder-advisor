using System.Globalization;

namespace Reorder.DataGen;

internal sealed class CliException(string message) : Exception(message)
{
}

internal static class Cli
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const string Usage = """
        Retail Reorder Advisor — synthetic data tool

        Usage:
          dotnet run --project src/Reorder.DataGen -- generate [options]
          dotnet run --project src/Reorder.DataGen -- backtest [options]

        Options for generate:
          --out <dir>            Output directory                  (default: data)
          --seed <int>           Random seed; same seed = same data (default: 42)
          --skus <int>           Number of SKUs, 40–2000           (default: 200)
          --history-days <int>   Days of history, 70–730           (default: 90)
          --sim-days <int>       Simulation days, 4–60             (default: 7)
          --start <yyyy-MM-dd>   First history date                (default: 2026-06-01)
          --dirty-rate <0-0.15>  Share of sales rows made dirty    (default: 0.015)

        backtest takes the same options and must use the same seed/sizes as the generated
        data. It reconstructs the dataset in memory and scores the advisor's early warnings.
        """;

    public static int Run(string[] args)
    {
        try
        {
            bool hasCommand = args.Length > 0 && !args[0].StartsWith('-');
            string command = hasCommand ? args[0].ToLowerInvariant() : "generate";
            string[] rest = hasCommand ? args[1..] : args;

            if (rest.Any(a => a is "-h" or "--help") || command is "help")
            {
                Console.WriteLine(Usage);
                return 0;
            }

            return command switch
            {
                "generate" => Generate(ParseGenerateOptions(rest)),
                "backtest" => Backtest.Run(ParseGenerateOptions(rest)),
                _ => throw new CliException($"Unknown command '{command}'."),
            };
        }
        catch (CliException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(Usage);
            return 2;
        }
    }

    private static int Generate(GeneratorOptions options)
    {
        var errors = options.Validate().ToList();
        if (errors.Count > 0)
        {
            throw new CliException(string.Join(Environment.NewLine, errors));
        }

        var rng = new Random(options.Seed);
        var skus = Catalog.BuildSkus(options.SkuCount, rng);
        Catalog.AssignScenarios(skus, options, rng);

        var simulation = Simulator.Run(skus, options, rng);
        var written = DatasetWriter.Write(skus, simulation, options);
        var truth = GroundTruthWriter.Build(skus, simulation, written, options);
        string truthPath = GroundTruthWriter.Write(truth, options.OutputDirectory);

        PrintSummary(options, skus, written, truth, truthPath);
        return 0;
    }

    private static void PrintSummary(
        GeneratorOptions o, List<Sku> skus, WriteSummary written, GroundTruth truth, string truthPath)
    {
        int CountOf(Scenario s) => skus.Count(x => x.Scenario == s);
        int dirty = written.DirtyRowsInLatest.Values.Sum();
        double dirtyShare = written.CleanRowsInLatest + dirty == 0 ? 0 : (double)dirty / (written.CleanRowsInLatest + dirty);
        var byCause = truth.Stockouts.GroupBy(s => s.Cause).Select(g => $"{g.Count()} {g.Key}");

        Console.WriteLine("Retail Reorder Advisor — synthetic data generated");
        Console.WriteLine($"  Seed ............... {o.Seed}");
        Console.WriteLine($"  SKUs ............... {skus.Count} (dead {CountOf(Scenario.DeadStock)}, overstock {CountOf(Scenario.Overstock)}, " +
                          $"planned stockout {CountOf(Scenario.PlannedStockout)}, demand spike {CountOf(Scenario.DemandSpike)})");
        Console.WriteLine($"  History ............ {Fmt(truth.StartDate)} to {Fmt(truth.HistoryEndDate)} ({o.HistoryDays} days)");
        Console.WriteLine($"  Simulation ......... {Fmt(o.DateOf(o.HistoryDays))} to {Fmt(truth.SimulationEndDate)} ({o.SimulationDays} days)");
        Console.WriteLine($"  As-of folders ...... {written.AsOfDates.Count} in {Path.GetFullPath(o.OutputDirectory)}");
        Console.WriteLine($"  Latest sales.csv ... {written.CleanRowsInLatest.ToString("N0", Inv)} clean + {dirty.ToString("N0", Inv)} dirty rows ({dirtyShare.ToString("P1", Inv)})");
        Console.WriteLine($"  Dirty rows by kind . {string.Join(", ", written.DirtyRowsInLatest.Select(kv => $"{kv.Key} {kv.Value}"))}");
        Console.WriteLine($"  Stockouts in sim ... {truth.Stockouts.Count} ({string.Join(", ", byCause)}); " +
                          $"{truth.Stockouts.Count(s => s.EvaluableThreeDaysAhead)} evaluable 3 days ahead");
        Console.WriteLine($"  Ground truth ....... {Path.GetFullPath(truthPath)}");
    }

    private static GeneratorOptions ParseGenerateOptions(string[] args)
    {
        var options = new GeneratorOptions();

        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (i + 1 >= args.Length)
            {
                throw new CliException($"Missing value for '{key}'.");
            }

            string value = args[++i];
            options = key switch
            {
                "--out" => options with { OutputDirectory = value },
                "--seed" => options with { Seed = ParseInt(key, value) },
                "--skus" => options with { SkuCount = ParseInt(key, value) },
                "--history-days" => options with { HistoryDays = ParseInt(key, value) },
                "--sim-days" => options with { SimulationDays = ParseInt(key, value) },
                "--start" => options with { StartDate = ParseDate(key, value) },
                "--dirty-rate" => options with { DirtyRowRate = ParseDouble(key, value) },
                _ => throw new CliException($"Unknown option '{key}'."),
            };
        }

        return options;
    }

    private static int ParseInt(string key, string value) =>
        int.TryParse(value, NumberStyles.Integer, Inv, out int result)
            ? result
            : throw new CliException($"'{key}' expects a whole number, got '{value}'.");

    private static double ParseDouble(string key, string value) =>
        double.TryParse(value, NumberStyles.Float, Inv, out double result)
            ? result
            : throw new CliException($"'{key}' expects a number, got '{value}'.");

    private static DateOnly ParseDate(string key, string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var result)
            ? result
            : throw new CliException($"'{key}' expects a date as yyyy-MM-dd, got '{value}'.");

    private static string Fmt(DateOnly date) => date.ToString("yyyy-MM-dd", Inv);
}
