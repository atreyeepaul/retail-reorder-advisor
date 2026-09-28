using System.Globalization;

namespace Reorder.DataGen;

internal sealed record Supplier(
    string Id,
    string Name,
    string Category,
    int ActualLeadTimeDays,
    int? PublishedLeadTimeDays,   // null => written as blank, exercising the default-lead-time path
    int Moq,
    int PackSize);

internal enum Scenario
{
    Normal,
    DeadStock,
    Overstock,
    PlannedStockout,
    DemandSpike,
}

internal sealed class Sku
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required Supplier Supplier { get; init; }
    public required decimal UnitCost { get; init; }
    public required decimal UnitPrice { get; init; }
    public required double WeekendFactor { get; init; }
    public required double SeasonPhase { get; init; }

    public double BaseDemand { get; set; }
    public Scenario Scenario { get; set; } = Scenario.Normal;
    public int? PlannedStockoutIndex { get; set; }
}

/// <summary>Builds the product catalog, suppliers and seeded scenarios.</summary>
internal static class Catalog
{
    private sealed record CategorySpec(string Name, string[] Items, string[] Sizes, double CostMin, double CostMax);

    // 8 categories x 9 items x 3 sizes = 216 unique product names.
    private static readonly CategorySpec[] Categories =
    [
        new("Grocery", ["Basmati Rice", "Toor Dal", "Wheat Atta", "Sugar", "Rock Salt", "Poha", "Rolled Oats", "Moong Dal", "Besan"], ["500g", "1kg", "5kg"], 40, 180),
        new("Beverages", ["Assam Tea", "Filter Coffee", "Mango Juice", "Cola", "Soda Water", "Green Tea", "Lemon Drink", "Coconut Water", "Rose Syrup"], ["250ml", "500ml", "1L"], 20, 160),
        new("Snacks", ["Salted Chips", "Masala Peanuts", "Cream Biscuits", "Aloo Bhujia", "Nachos", "Marie Biscuits", "Popcorn", "Choco Cookies", "Khakhra"], ["75g", "150g", "400g"], 10, 90),
        new("Dairy", ["Toned Milk", "Curd", "Paneer", "Butter", "Cheese Slices", "Ghee", "Buttermilk", "Sweet Lassi", "Fresh Cream"], ["200g", "500g", "1kg"], 25, 220),
        new("Personal Care", ["Bath Soap", "Shampoo", "Toothpaste", "Face Wash", "Hair Oil", "Body Lotion", "Talc", "Deodorant", "Hand Wash"], ["50ml", "100ml", "200ml"], 30, 200),
        new("Household", ["Dish Wash Liquid", "Detergent Powder", "Floor Cleaner", "Toilet Cleaner", "Garbage Bags", "Scrub Pads", "Room Freshener", "Glass Cleaner", "Fabric Softener"], ["500ml", "1L", "2L"], 35, 190),
        new("Frozen", ["Green Peas", "French Fries", "Veg Nuggets", "Ice Cream Tub", "Frozen Parathas", "Sweet Corn", "Chicken Sausages", "Mixed Veg", "Aloo Tikki"], ["200g", "500g", "1kg"], 50, 240),
        new("Baby Care", ["Baby Diapers", "Baby Wipes", "Baby Lotion", "Baby Powder", "Baby Shampoo", "Rash Cream", "Baby Soap", "Baby Oil", "Baby Cereal"], ["S", "M", "L"], 60, 320),
    ];

    /// <summary>One supplier per category. Lead times are all 3+ days; one supplier omits its lead time.</summary>
    public static IReadOnlyList<Supplier> Suppliers { get; } =
    [
        new("SUP-01", "Annapurna Foods", "Grocery", 5, 5, 12, 12),
        new("SUP-02", "Himalaya Beverages", "Beverages", 4, 4, 24, 12),
        new("SUP-03", "Crunch & Co", "Snacks", 3, 3, 12, 6),
        new("SUP-04", "Gokul Dairy", "Dairy", 3, 3, 6, 6),
        new("SUP-05", "Lotus Personal Care", "Personal Care", 7, 7, 12, 6),
        new("SUP-06", "Sparkle Home", "Household", 10, 10, 12, 12),
        new("SUP-07", "Polar Frozen", "Frozen", 6, null, 6, 6),
        new("SUP-08", "Little Steps", "Baby Care", 14, 14, 1, 1),
    ];

    public static List<Sku> BuildSkus(int count, Random rng)
    {
        var combos = new List<(CategorySpec Spec, string Item, int SizeIndex)>();
        foreach (var spec in Categories)
        {
            foreach (var item in spec.Items)
            {
                for (int s = 0; s < spec.Sizes.Length; s++)
                {
                    combos.Add((spec, item, s));
                }
            }
        }

        var shuffled = combos.ToArray();
        rng.Shuffle(shuffled);

        var skus = new List<Sku>(count);
        for (int i = 0; i < count; i++)
        {
            var (spec, item, sizeIndex) = shuffled[i % shuffled.Length];
            int round = i / shuffled.Length;
            string name = $"{item} {spec.Sizes[sizeIndex]}" + (round > 0 ? $" (Pack {round + 1})" : string.Empty);
            var supplier = Suppliers.First(s => s.Category == spec.Name);

            double sizeMultiplier = 1.0 + (0.9 * sizeIndex);
            decimal unitCost = Math.Round((decimal)(rng.NextDouble(spec.CostMin, spec.CostMax) * sizeMultiplier), 2);
            decimal unitPrice = Math.Round(unitCost * (decimal)rng.NextDouble(1.2, 1.6), 2);

            // Most items sell slowly, a few sell fast; bigger pack sizes sell less often.
            double baseDemand = rng.NextLogNormal(median: 2.5, sigma: 0.9) / (1.0 + (0.4 * sizeIndex));

            skus.Add(new Sku
            {
                Code = (1001 + i).ToString(CultureInfo.InvariantCulture),
                Name = name,
                Category = spec.Name,
                Supplier = supplier,
                UnitCost = unitCost,
                UnitPrice = unitPrice,
                WeekendFactor = rng.NextDouble(1.15, 1.40),
                SeasonPhase = rng.NextDouble(0, 2 * Math.PI),
                BaseDemand = Math.Clamp(baseDemand, 0.1, 40.0),
            });
        }

        return skus;
    }

    /// <summary>
    /// Seeds the scenarios that make every flag fire in the demo. Counts scale with
    /// the SKU count; at 200 SKUs: 5 dead, 15 overstock, 10 planned stockouts, 3 spikes.
    /// </summary>
    public static void AssignScenarios(List<Sku> skus, GeneratorOptions options, Random rng)
    {
        var order = Enumerable.Range(0, skus.Count).ToArray();
        rng.Shuffle(order);
        int cursor = 0;

        int Share(double perTwoHundred) => Math.Max(1, (int)Math.Round(skus.Count * perTwoHundred / 200.0));

        List<Sku> NextBatch(int n)
        {
            var batch = order.Skip(cursor).Take(n).Select(i => skus[i]).ToList();
            cursor += n;
            return batch;
        }

        foreach (var sku in NextBatch(Share(5)))
        {
            sku.Scenario = Scenario.DeadStock;
            sku.BaseDemand = rng.NextDouble(0.3, 1.2);
        }

        foreach (var sku in NextBatch(Share(15)))
        {
            sku.Scenario = Scenario.Overstock;
            sku.BaseDemand = Math.Max(sku.BaseDemand, 0.5);
        }

        // Planned stockouts land 3+ days into the simulation window so they can be
        // evaluated "flagged at least 3 days in advance".
        int earliest = options.HistoryDays + 3;
        int latest = options.TotalDays - 1;
        foreach (var sku in NextBatch(Share(10)))
        {
            sku.Scenario = Scenario.PlannedStockout;
            sku.BaseDemand = rng.NextDouble(2.0, 8.0);
            sku.PlannedStockoutIndex = rng.Next(earliest, latest + 1);
        }

        foreach (var sku in NextBatch(Share(3)))
        {
            sku.Scenario = Scenario.DemandSpike;
            sku.BaseDemand = Math.Max(sku.BaseDemand, 1.0);
        }
    }
}
