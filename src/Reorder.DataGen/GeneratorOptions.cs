namespace Reorder.DataGen;

/// <summary>Command-line options for the <c>generate</c> command.</summary>
internal sealed record GeneratorOptions
{
    public string OutputDirectory { get; init; } = "data";
    public int Seed { get; init; } = 42;
    public int SkuCount { get; init; } = 200;
    public int HistoryDays { get; init; } = 90;
    public int SimulationDays { get; init; } = 7;
    public DateOnly StartDate { get; init; } = new(2026, 6, 1);
    public double DirtyRowRate { get; init; } = 0.015;

    public int TotalDays => HistoryDays + SimulationDays;

    /// <summary>Zero-based index of the last history day (the first as-of date).</summary>
    public int HistoryEndIndex => HistoryDays - 1;

    public DateOnly DateOf(int dayIndex) => StartDate.AddDays(dayIndex);

    public IEnumerable<string> Validate()
    {
        if (SkuCount is < 40 or > 2000)
        {
            yield return "--skus must be between 40 and 2000.";
        }

        if (HistoryDays is < 70 or > 730)
        {
            yield return "--history-days must be between 70 and 730 (dead-stock detection needs 60+ days of history).";
        }

        if (SimulationDays is < 4 or > 60)
        {
            yield return "--sim-days must be between 4 and 60 (planned stockouts need at least 4 simulation days).";
        }

        if (DirtyRowRate is < 0 or > 0.15)
        {
            yield return "--dirty-rate must be between 0 and 0.15.";
        }
    }
}
