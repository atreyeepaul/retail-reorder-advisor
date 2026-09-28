using System.Globalization;

namespace Reorder.DataGen;

/// <summary>Turns clean rows into realistic bad ones so ingestion validation has work to do.</summary>
internal static class DirtyRows
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // FutureDate is generated separately per as-of folder, because "future" depends on the as-of date.
    private static readonly DirtyKind[] RandomKinds =
        Enum.GetValues<DirtyKind>().Where(k => k != DirtyKind.FutureDate).ToArray();

    private static readonly string[] BadQuantities = ["abc", "3.5", "twelve", "1,000"];

    public static SalesRow From(SalesRow clean, Random rng)
    {
        var kind = RandomKinds[rng.Next(RandomKinds.Length)];
        return kind switch
        {
            DirtyKind.NegativeQuantity => clean with { QtySold = (-rng.Next(1, 6)).ToString(Inv), Dirty = kind },
            DirtyKind.UnknownSku => clean with { Sku = rng.Next(9001, 9999).ToString(Inv), Dirty = kind },
            DirtyKind.MissingDate => clean with { Date = string.Empty, Dirty = kind },
            DirtyKind.MalformedDate => clean with { Date = MalformedDate(clean.Date, rng), Dirty = kind },
            DirtyKind.BadQuantity => clean with { QtySold = rng.Pick(BadQuantities), Dirty = kind },
            DirtyKind.DuplicateKey => clean with
            {
                QtySold = (int.Parse(clean.QtySold, Inv) + rng.Next(0, 2)).ToString(Inv),
                Dirty = kind,
            },
            DirtyKind.MissingPrice => clean with { UnitPrice = string.Empty, Dirty = kind },
            _ => throw new ArgumentOutOfRangeException(nameof(clean), kind, "Unexpected dirty row kind."),
        };
    }

    public static SalesRow FutureDated(DateOnly asOf, Sku sku, Random rng) => new(
        asOf.AddDays(rng.Next(1, 11)).ToString("yyyy-MM-dd", Inv),
        sku.Code,
        rng.Next(1, 6).ToString(Inv),
        sku.UnitPrice.ToString("0.00", Inv),
        DirtyKind.FutureDate);

    private static string MalformedDate(string isoDate, Random rng)
    {
        var date = DateOnly.ParseExact(isoDate, "yyyy-MM-dd", Inv);
        string[] variants =
        [
            date.ToString("dd/MM/yyyy", Inv),
            date.ToString("MMM d yyyy", Inv),
            $"{date.Year}-13-{date.Day:00}",
            "N/A",
        ];
        return rng.Pick(variants);
    }
}
