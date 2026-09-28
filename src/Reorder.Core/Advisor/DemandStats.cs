using Reorder.Core.Models;

namespace Reorder.Core.Advisor;

/// <summary>
/// Trailing-window demand statistics for one SKU. Velocity is the mean of a zero-filled daily
/// series; sigma is the sample standard deviation. Missing days count as zero sales.
/// </summary>
public sealed record DemandStats(double Velocity, double Sigma, int HistoryDays, DateOnly? LastSaleDate)
{
    /// <summary>
    /// Build stats from a SKU's sales (all history for that SKU). The series runs from
    /// <c>max(windowStart, firstEverSale)</c> to <paramref name="asOf"/>, so a SKU whose first
    /// sale falls inside the window gets a shorter series; an established SKU uses the full window
    /// with gaps counted as zero. <see cref="LastSaleDate"/> spans all history (for dead-stock).
    /// </summary>
    public static DemandStats Compute(IReadOnlyList<SalesRecord> skuSales, DateOnly asOf, int windowDays)
    {
        DateOnly? firstEver = null;
        DateOnly? lastSale = null;

        foreach (var sale in skuSales)
        {
            if (sale.Date > asOf)
            {
                continue;   // defensive: ingestion already rejects future-dated rows
            }

            if (firstEver is null || sale.Date < firstEver)
            {
                firstEver = sale.Date;
            }

            if (lastSale is null || sale.Date > lastSale)
            {
                lastSale = sale.Date;
            }
        }

        if (firstEver is null)
        {
            return new DemandStats(0d, 0d, 0, null);
        }

        var windowStart = asOf.AddDays(-(windowDays - 1));
        var effectiveStart = firstEver.Value > windowStart ? firstEver.Value : windowStart;
        int n = asOf.DayNumber - effectiveStart.DayNumber + 1;

        var daily = new double[n];
        foreach (var sale in skuSales)
        {
            if (sale.Date < effectiveStart || sale.Date > asOf)
            {
                continue;
            }

            daily[sale.Date.DayNumber - effectiveStart.DayNumber] += sale.QtySold;
        }

        double mean = daily.Average();
        double sigma = 0d;
        if (n >= 2)
        {
            double sumSquares = 0d;
            foreach (double value in daily)
            {
                double diff = value - mean;
                sumSquares += diff * diff;
            }

            sigma = Math.Sqrt(sumSquares / (n - 1));
        }

        return new DemandStats(mean, sigma, n, lastSale);
    }
}
