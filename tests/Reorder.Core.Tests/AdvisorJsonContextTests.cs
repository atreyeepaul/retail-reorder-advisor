using System.Text.Json;
using Reorder.Core.Advisor;
using Reorder.Core.Models;

namespace Reorder.Core.Tests;

public sealed class AdvisorJsonContextTests
{
    [Fact]
    public void Run_result_serializes_camel_case_with_string_codes_and_round_trips()
    {
        var summary = new RunSummary(
            AsOf: "2026-10-07",
            GeneratedAtUtc: DateTimeOffset.Parse("2026-10-06T20:30:12Z"),
            SkuCount: 1,
            CountsByFlag: new Dictionary<string, int>
            {
                ["URGENT"] = 1,
                ["STOCKOUT_RISK"] = 0,
                ["DEAD_STOCK"] = 0,
                ["OVERSTOCK"] = 0,
                ["HEALTHY"] = 0,
            },
            OverstockValue: 10m,
            DeadStockValue: 20m,
            EstLostSalesAvoided: 30m,
            QuarantinedRows: 5,
            RunDurationMs: 412);
        var rec = new Recommendation(
            "4471", "Basmati Rice 5kg", "Grocery", "SUP-03", "Delta Foods",
            18, 6.2, 2.1, 5, 7.75, 38.75, 2.9, 210, Flag.Urgent, Confidence.High,
            new DateOnly(2026, 10, 9), "Order 210 now.");
        var result = new RunResult(summary, [rec]);

        string json = JsonSerializer.Serialize(result, AdvisorJsonContext.Default.RunResult);

        // camelCase property names
        Assert.Contains("\"summary\"", json);
        Assert.Contains("\"recommendations\"", json);
        Assert.Contains("\"supplierId\"", json);
        Assert.DoesNotContain("\"SupplierId\"", json);
        // dictionary keys stay upper-case; enums emit their codes
        Assert.Contains("\"countsByFlag\"", json);
        Assert.Contains("\"URGENT\"", json);
        Assert.Contains("\"flag\": \"URGENT\"", json);
        Assert.Contains("\"confidence\": \"High\"", json);

        var back = JsonSerializer.Deserialize(json, AdvisorJsonContext.Default.RunResult)!;
        Assert.Equal(1, back.Summary.SkuCount);
        Assert.Equal(1, back.Summary.CountsByFlag["URGENT"]);
        Assert.Equal(Flag.Urgent, back.Recommendations[0].Flag);
        Assert.Equal(Confidence.High, back.Recommendations[0].Confidence);
        Assert.Equal(new DateOnly(2026, 10, 9), back.Recommendations[0].EstimatedStockoutDate);
    }
}
