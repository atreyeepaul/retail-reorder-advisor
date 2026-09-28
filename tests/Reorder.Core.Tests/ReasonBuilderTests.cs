using Reorder.Core.Advisor;
using Reorder.Core.Models;

namespace Reorder.Core.Tests;

public sealed class ReasonBuilderTests
{
    private static readonly AdvisorOptions Options = new();

    [Fact]
    public void Urgent_reason_matches_the_plan_example()
    {
        string reason = ReasonBuilder.Build(
            Flag.Urgent, onHand: 18, velocity: 6.2, daysOfCover: 18.0 / 6.2, leadTimeDays: 5,
            reorderPoint: 38.75, suggestedQty: 210, defaultLeadTimeUsed: false,
            confidence: Confidence.High, Options);

        Assert.Equal(
            "On hand 18 covers ~2.9 days but supplier lead time is 5 days. Order 210 now.",
            reason);
    }

    [Fact]
    public void Same_inputs_produce_identical_text()
    {
        static string Build() => ReasonBuilder.Build(
            Flag.StockoutRisk, 30, 2.0, 15.0, 5, 38.7, 48, false, Confidence.High, Options);

        Assert.Equal(Build(), Build());
    }

    [Fact]
    public void Default_lead_time_is_called_out()
    {
        string reason = ReasonBuilder.Build(
            Flag.Urgent, 5, 2.0, 2.5, 7, 14.0, 20, defaultLeadTimeUsed: true,
            confidence: Confidence.High, Options);

        Assert.Contains("default of 7 days", reason);
    }

    [Fact]
    public void Zero_velocity_healthy_notes_no_recent_demand_and_low_confidence()
    {
        string reason = ReasonBuilder.Build(
            Flag.Healthy, onHand: 0, velocity: 0d, daysOfCover: null, leadTimeDays: 7,
            reorderPoint: 0d, suggestedQty: 0, defaultLeadTimeUsed: false,
            confidence: Confidence.Low, Options);

        Assert.Contains("No recent demand", reason);
        Assert.EndsWith("Low confidence.", reason);
    }
}
