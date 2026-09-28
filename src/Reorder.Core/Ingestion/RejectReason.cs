using System.Text.Json.Serialization;

namespace Reorder.Core.Ingestion;

/// <summary>
/// Why a row was quarantined. Serialized as the screaming-snake codes from the
/// data contract (e.g. <c>MISSING_FIELD</c>) while staying idiomatic in C#.
/// </summary>
public enum RejectReason
{
    /// <summary>A required field was empty.</summary>
    [JsonStringEnumMemberName("MISSING_FIELD")]
    MissingField,

    /// <summary>A field could not be parsed as its declared type, or violated its value rule.</summary>
    [JsonStringEnumMemberName("BAD_TYPE")]
    BadType,

    /// <summary>A sales quantity was negative.</summary>
    [JsonStringEnumMemberName("NEGATIVE_QTY")]
    NegativeQty,

    /// <summary>A sales date was after the upload's as-of date.</summary>
    [JsonStringEnumMemberName("FUTURE_DATE")]
    FutureDate,

    /// <summary>A sales SKU was not present in the inventory snapshot.</summary>
    [JsonStringEnumMemberName("UNKNOWN_SKU")]
    UnknownSku,

    /// <summary>A later row repeated a key already seen (the first is kept).</summary>
    [JsonStringEnumMemberName("DUPLICATE_KEY")]
    DuplicateKey,

    /// <summary>An inventory row referenced a supplier not present in suppliers.csv.</summary>
    [JsonStringEnumMemberName("UNKNOWN_SUPPLIER")]
    UnknownSupplier,
}
