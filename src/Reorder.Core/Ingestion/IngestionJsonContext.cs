using System.Text.Json.Serialization;
using Reorder.Core.Models;

namespace Reorder.Core.Ingestion;

/// <summary>
/// Source-generated serialization for the ingestion blob outputs. Indented for
/// human-readable blobs; enums emit their screaming-snake codes.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(NormalizedDataset))]
[JsonSerializable(typeof(QuarantineReport))]
public sealed partial class IngestionJsonContext : JsonSerializerContext;
