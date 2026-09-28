using System.Text.Json.Serialization;
using Reorder.Core.Models;

namespace Reorder.Core.Advisor;

/// <summary>
/// Source-generated serialization for the advisor output (<c>recommendations/latest.json</c> and
/// the dated snapshot). camelCase property names per the data contract; enums emit their string
/// codes. <see cref="System.Text.Json.JsonSourceGenerationOptions"/> deliberately leaves
/// <c>DictionaryKeyPolicy</c> unset so <c>countsByFlag</c> keys stay upper-case (URGENT, …).
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(RunResult))]
public sealed partial class AdvisorJsonContext : JsonSerializerContext;
