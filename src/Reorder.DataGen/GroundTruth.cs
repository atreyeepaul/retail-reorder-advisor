using System.Text.Json;

namespace Reorder.DataGen;

internal sealed record GroundTruth(
    int Seed,
    DateOnly StartDate,
    DateOnly HistoryEndDate,
    DateOnly SimulationEndDate,
    IReadOnlyList<DateOnly> AsOfDates,
    ScenarioLists Scenarios,
    IReadOnlyList<GroundTruthStockout> Stockouts,
    IReadOnlyDictionary<string, int> DirtyRowsInLatestFolder,
    string Notes);

internal sealed record ScenarioLists(
    IReadOnlyList<string> DeadStock,
    IReadOnlyList<string> Overstock,
    IReadOnlyList<PlannedStockout> PlannedStockouts,
    IReadOnlyList<string> DemandSpike);

internal sealed record PlannedStockout(string Sku, DateOnly TargetDate);

internal sealed record GroundTruthStockout(
    string Sku,
    DateOnly Date,
    string Cause,
    int LeadTimeDays,
    bool EvaluableThreeDaysAhead);

/// <summary>Writes ground-truth.json, used by the Day 5 backtest to score the advisor.</summary>
internal static class GroundTruthWriter
{
    public const string FileName = "ground-truth.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
    };

    public static GroundTruth Build(IReadOnlyList<Sku> skus, SimulationResult sim, WriteSummary written, GeneratorOptions options)
    {
        List<string> CodesOf(Scenario scenario) =>
            skus.Where(s => s.Scenario == scenario).Select(s => s.Code).ToList();

        var planned = skus
            .Where(s => s.Scenario == Scenario.PlannedStockout && s.PlannedStockoutIndex is not null)
            .Select(s => new PlannedStockout(s.Code, options.DateOf(s.PlannedStockoutIndex!.Value)))
            .ToList();

        // A stockout on day d can only be "flagged 3+ days ahead" if an as-of folder exists at d - 3 or earlier.
        var stockouts = sim.Stockouts
            .OrderBy(e => e.DayIndex)
            .ThenBy(e => e.Sku, StringComparer.Ordinal)
            .Select(e => new GroundTruthStockout(
                e.Sku,
                options.DateOf(e.DayIndex),
                e.Cause,
                e.LeadTimeDays,
                EvaluableThreeDaysAhead: e.DayIndex - 3 >= options.HistoryEndIndex))
            .ToList();

        return new GroundTruth(
            options.Seed,
            options.StartDate,
            options.DateOf(options.HistoryEndIndex),
            options.DateOf(options.TotalDays - 1),
            written.AsOfDates,
            new ScenarioLists(
                CodesOf(Scenario.DeadStock),
                CodesOf(Scenario.Overstock),
                planned,
                CodesOf(Scenario.DemandSpike)),
            stockouts,
            written.DirtyRowsInLatest,
            "Stockout = first day on hand reaches 0 inside the simulation window. " +
            "Success criterion: >= 80% of evaluable stockouts flagged URGENT or STOCKOUT_RISK " +
            "on an as-of date at least 3 days before the stockout.");
    }

    public static string Write(GroundTruth truth, string outputDirectory)
    {
        string path = Path.Combine(outputDirectory, FileName);
        File.WriteAllText(path, JsonSerializer.Serialize(truth, JsonOptions));
        return path;
    }
}
