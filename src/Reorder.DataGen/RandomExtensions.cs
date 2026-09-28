namespace Reorder.DataGen;

/// <summary>Small set of distributions built on a seeded <see cref="Random"/>.</summary>
internal static class RandomExtensions
{
    public static double NextDouble(this Random rng, double min, double max) =>
        min + (rng.NextDouble() * (max - min));

    /// <summary>Normal distribution via the Box–Muller transform.</summary>
    public static double NextNormal(this Random rng, double mean, double stdDev)
    {
        double u1 = 1.0 - rng.NextDouble(); // (0, 1], avoids log(0)
        double u2 = rng.NextDouble();
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return mean + (stdDev * z);
    }

    /// <summary>Log-normal distribution parameterised by its median.</summary>
    public static double NextLogNormal(this Random rng, double median, double sigma) =>
        median * Math.Exp(rng.NextNormal(0.0, sigma));

    /// <summary>Poisson draw: Knuth's method for small λ, normal approximation above 30.</summary>
    public static int NextPoisson(this Random rng, double lambda)
    {
        if (lambda <= 0)
        {
            return 0;
        }

        if (lambda > 30)
        {
            return Math.Max(0, (int)Math.Round(rng.NextNormal(lambda, Math.Sqrt(lambda))));
        }

        double limit = Math.Exp(-lambda);
        double product = 1.0;
        int k = 0;
        do
        {
            k++;
            product *= rng.NextDouble();
        }
        while (product > limit);

        return k - 1;
    }

    public static T Pick<T>(this Random rng, IReadOnlyList<T> items) => items[rng.Next(items.Count)];
}
