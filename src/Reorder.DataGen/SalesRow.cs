namespace Reorder.DataGen;

/// <summary>Kinds of deliberately bad rows. Each maps to a validator reject reason on Day 2.</summary>
internal enum DirtyKind
{
    NegativeQuantity,   // NEGATIVE_QTY
    UnknownSku,         // UNKNOWN_SKU
    MissingDate,        // MISSING_FIELD
    MalformedDate,      // BAD_TYPE
    BadQuantity,        // BAD_TYPE
    DuplicateKey,       // DUPLICATE_KEY
    MissingPrice,       // MISSING_FIELD
    FutureDate,         // FUTURE_DATE
}

/// <summary>A sales.csv row kept as raw strings, so dirty values can be written verbatim.</summary>
internal sealed record SalesRow(string Date, string Sku, string QtySold, string UnitPrice, DirtyKind? Dirty = null)
{
    public string[] ToFields() => [Date, Sku, QtySold, UnitPrice];
}
