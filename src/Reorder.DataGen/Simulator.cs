namespace Reorder.DataGen;

internal sealed record StockoutEvent(string Sku, int DayIndex, string Cause, int LeadTimeDays);

internal sealed class SimulationResult
{
    /// <summary>Units actually sold, indexed [sku, day]. Censored by stock on hand.</summary>
    public required int[,] Sales { get; init; }

    /// <summary>Stock on hand at the end of each day, indexed [sku, day].</summary>
    public required int[,] OnHandEndOfDay { get; init; }

    /// <summary>Every transition to zero stock inside the simulation window.</summary>
    public required List<StockoutEvent> Stockouts { get; init; }
}

/// <summary>
/// Day-by-day simulation of true demand, sales and the store's current (naive)
/// replenishment habit. The advisor never sees true demand — only the sales it produces.
/// </summary>
internal static class Simulator
{
    private const double SeasonalAmplitude = 0.10;
    private const double SpikeMultiplier = 3.0;

    public static SimulationResult Run(IReadOnlyList<Sku> skus, GeneratorOptions options, Random rng)
    {
        int skuCount = skus.Count;
        int totalDays = options.TotalDays;
        int historyDays = options.HistoryDays;

        // Dead-stock SKUs stop selling early enough to be 60+ days without a sale by the first as-of date.
        int deadStockCutoff = Math.Max(5, historyDays - 70);

        // Demand spikes cover the last ~2 weeks of history and the first days of the simulation.
        int spikeStart = historyDays - 12;
        int spikeEnd = historyDays + 1;

        var demand = new int[skuCount, totalDays];
        for (int i = 0; i < skuCount; i++)
        {
            for (int d = 0; d < totalDays; d++)
            {
                double lambda = ExpectedDemand(skus[i], d, options.DateOf(d), deadStockCutoff, spikeStart, spikeEnd);
                demand[i, d] = rng.NextPoisson(lambda);
            }
        }

        var sales = new int[skuCount, totalDays];
        var onHandEnd = new int[skuCount, totalDays];
        var stockouts = new List<StockoutEvent>();

        for (int i = 0; i < skuCount; i++)
        {
            var sku = skus[i];
            var supplier = sku.Supplier;
            bool replenishes = sku.Scenario is Scenario.Normal or Scenario.DemandSpike;

            int onHand = InitialOnHand(sku, i, demand, options, deadStockCutoff, rng);
            int previousEnd = onHand;
            var pipeline = new List<(int ArrivalDay, int Qty)>();

            for (int d = 0; d < totalDays; d++)
            {
                // 1. Receive deliveries due today.
                for (int p = pipeline.Count - 1; p >= 0; p--)
                {
                    if (pipeline[p].ArrivalDay == d)
                    {
                        onHand += pipeline[p].Qty;
                        pipeline.RemoveAt(p);
                    }
                }

                // 2. Sell. Sales can't exceed stock, so stockouts hide true demand (censored demand).
                int sold = Math.Min(demand[i, d], onHand);
                onHand -= sold;
                sales[i, d] = sold;
                onHandEnd[i, d] = onHand;

                // 3. Record stockouts that happen inside the simulation window.
                if (d >= historyDays && onHand == 0 && previousEnd > 0)
                {
                    stockouts.Add(new StockoutEvent(sku.Code, d, CauseOf(sku), supplier.ActualLeadTimeDays));
                }

                previousEnd = onHand;

                // 4. The store's current habit: reorder when stock looks low against *planned*
                //    demand. It ignores variability and recent trends — the gap the advisor fills.
                if (replenishes)
                {
                    int onOrder = pipeline.Sum(x => x.Qty);
                    double trigger = sku.BaseDemand * (supplier.ActualLeadTimeDays + 7);
                    if (onHand + onOrder <= trigger)
                    {
                        int wanted = Math.Max(supplier.Moq, (int)Math.Ceiling(sku.BaseDemand * 21));
                        pipeline.Add((d + supplier.ActualLeadTimeDays, RoundUpToPack(wanted, supplier.PackSize)));
                    }
                }
            }
        }

        return new SimulationResult
        {
            Sales = sales,
            OnHandEndOfDay = onHandEnd,
            Stockouts = stockouts,
        };
    }

    private static double ExpectedDemand(Sku sku, int day, DateOnly date, int deadStockCutoff, int spikeStart, int spikeEnd)
    {
        if (sku.Scenario == Scenario.DeadStock && day >= deadStockCutoff)
        {
            return 0;
        }

        double lambda = sku.BaseDemand;

        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            lambda *= sku.WeekendFactor;
        }

        lambda *= 1.0 + (SeasonalAmplitude * Math.Sin((2.0 * Math.PI * day / 30.0) + sku.SeasonPhase));

        if (sku.Scenario == Scenario.DemandSpike && day >= spikeStart && day <= spikeEnd)
        {
            lambda *= SpikeMultiplier;
        }

        return lambda;
    }

    private static int InitialOnHand(Sku sku, int skuIndex, int[,] demand, GeneratorOptions options, int deadStockCutoff, Random rng)
    {
        switch (sku.Scenario)
        {
            case Scenario.PlannedStockout:
            {
                // Just enough stock to run out exactly on the planned day.
                int stockoutDay = sku.PlannedStockoutIndex
                    ?? throw new InvalidOperationException($"SKU {sku.Code} has no planned stockout day.");
                if (demand[skuIndex, stockoutDay] < 1)
                {
                    demand[skuIndex, stockoutDay] = 1;
                }

                return SumDemand(demand, skuIndex, 0, stockoutDay - 1)
                    + (int)Math.Ceiling(demand[skuIndex, stockoutDay] / 2.0);
            }

            case Scenario.Overstock:
                // Covers all of history plus another 130–220 days of demand.
                return SumDemand(demand, skuIndex, 0, options.HistoryDays - 1)
                    + (int)Math.Round(sku.BaseDemand * rng.NextDouble(130, 220));

            case Scenario.DeadStock:
                // Sells through its early demand, then sits on 30–120 unsold units.
                return SumDemand(demand, skuIndex, 0, deadStockCutoff - 1) + rng.Next(30, 121);

            default:
                return (int)Math.Ceiling(sku.BaseDemand * rng.NextDouble(15, 35));
        }
    }

    private static int SumDemand(int[,] demand, int skuIndex, int fromDay, int toDayInclusive)
    {
        int total = 0;
        for (int d = fromDay; d <= toDayInclusive; d++)
        {
            total += demand[skuIndex, d];
        }

        return total;
    }

    private static int RoundUpToPack(int quantity, int packSize) =>
        (quantity + packSize - 1) / packSize * packSize;

    private static string CauseOf(Sku sku) => sku.Scenario switch
    {
        Scenario.PlannedStockout => "planned",
        Scenario.DemandSpike => "demand-spike",
        _ => "natural",
    };
}
