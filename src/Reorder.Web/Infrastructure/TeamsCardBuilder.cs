using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Reorder.Core.Models;

namespace Reorder.Web.Infrastructure;

/// <summary>
/// Builds the JSON for a Microsoft Teams (Workflows) incoming webhook: an Adaptive Card wrapped in
/// the <c>{ "type": "message", "attachments": [...] }</c> envelope. Pure and deterministic, so it is
/// unit-tested without any HTTP. Money is shown in INR (₹) with thousands grouping.
/// </summary>
public static class TeamsCardBuilder
{
    private const string CardContentType = "application/vnd.microsoft.card.adaptive";
    private const string CardSchema = "http://adaptivecards.io/schemas/adaptive-card.json";

    // Emit the card text (₹, ·, •, —, ⚠) as literal UTF-8 rather than \uXXXX escapes. This is a
    // webhook JSON body, never embedded in HTML, so relaxed escaping is safe and keeps it readable.
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The post-run digest: flag counts, the money tiles, the top urgent SKUs, and a dashboard link.</summary>
    public static string BuildDigest(RunSummary summary, IReadOnlyList<Recommendation> topUrgent, string? dashboardUrl)
    {
        string generated = summary.GeneratedAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        var body = new JsonArray
        {
            TextBlock($"Reorder digest — {summary.AsOf}", size: "Large", weight: "Bolder"),
            TextBlock($"{summary.SkuCount} SKUs · generated {generated} UTC", isSubtle: true, spacing: "None"),
            FactSet(
                Fact("Urgent", Count(summary, Flag.Urgent)),
                Fact("Stockout risk", Count(summary, Flag.StockoutRisk)),
                Fact("Dead stock", Count(summary, Flag.DeadStock)),
                Fact("Overstock", Count(summary, Flag.Overstock)),
                Fact("Healthy", Count(summary, Flag.Healthy)),
                Fact("Overstock value", Inr(summary.OverstockValue)),
                Fact("Dead-stock value", Inr(summary.DeadStockValue)),
                Fact("Lost sales avoided", Inr(summary.EstLostSalesAvoided))),
            TextBlock("Top urgent", weight: "Bolder", spacing: "Medium"),
            TextBlock(UrgentLines(topUrgent), wrap: true, spacing: "None"),
        };

        return Envelope(body, dashboardUrl);
    }

    /// <summary>The alert sent instead of a digest when ingestion aborts on too many rejected sales rows.</summary>
    public static string BuildAlert(IngestionAlert alert, string? dashboardUrl)
    {
        var body = new JsonArray
        {
            TextBlock($"⚠ Ingestion aborted — {alert.AsOf}", size: "Large", weight: "Bolder", color: "Attention"),
            TextBlock(
                $"Rejected {Percent(alert.RejectRatio)} of {alert.SalesTotal} sales rows " +
                $"(threshold {Percent(alert.Threshold)}). Previous recommendations were kept.",
                wrap: true),
            FactSet(
                Fact("Rejected rows", alert.SalesRejected.ToString(CultureInfo.InvariantCulture)),
                Fact("Total rows", alert.SalesTotal.ToString(CultureInfo.InvariantCulture)),
                Fact("Threshold", Percent(alert.Threshold))),
        };

        return Envelope(body, dashboardUrl);
    }

    private static string Envelope(JsonArray body, string? dashboardUrl)
    {
        var card = new JsonObject
        {
            ["$schema"] = CardSchema,
            ["type"] = "AdaptiveCard",
            ["version"] = "1.5",
            ["body"] = body,
        };

        if (!string.IsNullOrWhiteSpace(dashboardUrl))
        {
            card["actions"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "Action.OpenUrl",
                    ["title"] = "Open dashboard",
                    ["url"] = dashboardUrl,
                },
            };
        }

        var payload = new JsonObject
        {
            ["type"] = "message",
            ["attachments"] = new JsonArray
            {
                new JsonObject
                {
                    ["contentType"] = CardContentType,
                    ["content"] = card,
                },
            },
        };

        return payload.ToJsonString(JsonOptions);
    }

    private static string UrgentLines(IReadOnlyList<Recommendation> topUrgent)
    {
        if (topUrgent.Count == 0)
        {
            return "No urgent SKUs.";
        }

        return string.Join(
            "\n",
            topUrgent.Select(r => $"• {r.Sku} · {r.Name} · order {r.SuggestedQty.ToString(CultureInfo.InvariantCulture)}"));
    }

    private static string Count(RunSummary summary, Flag flag) =>
        (summary.CountsByFlag.TryGetValue(flag.ToCode(), out var c) ? c : 0).ToString(CultureInfo.InvariantCulture);

    private static string Inr(decimal value) => "₹" + value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Percent(double ratio) => (ratio * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static JsonObject TextBlock(
        string text, string? size = null, string? weight = null, string? color = null,
        bool wrap = false, bool isSubtle = false, string? spacing = null)
    {
        var block = new JsonObject { ["type"] = "TextBlock", ["text"] = text };
        if (size is not null) block["size"] = size;
        if (weight is not null) block["weight"] = weight;
        if (color is not null) block["color"] = color;
        if (wrap) block["wrap"] = true;
        if (isSubtle) block["isSubtle"] = true;
        if (spacing is not null) block["spacing"] = spacing;
        return block;
    }

    private static JsonObject FactSet(params JsonObject[] facts)
    {
        var arr = new JsonArray();
        foreach (var f in facts)
        {
            arr.Add(f);
        }

        return new JsonObject { ["type"] = "FactSet", ["facts"] = arr };
    }

    private static JsonObject Fact(string title, string value) =>
        new() { ["title"] = title, ["value"] = value };
}
