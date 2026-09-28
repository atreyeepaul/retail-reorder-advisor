using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reorder.Core.Models;
using Reorder.Web.Infrastructure;

namespace Reorder.Web.Tests;

public sealed class WebhookNotifierTests
{
    [Fact]
    public async Task Digest_posts_an_adaptive_card_to_the_configured_url()
    {
        var handler = new CapturingHandler();
        var notifier = Notifier(handler, webhookUrl: "https://example.test/webhook", dashboardUrl: "https://app.test/");

        await notifier.PostDigestAsync(Summary(), new[] { Rec("1002", "Filter Coffee 500ml", 96, Flag.Urgent) });

        Assert.Equal(1, handler.Calls);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://example.test/webhook", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("application/json", handler.LastContentType);
        Assert.Contains("AdaptiveCard", handler.LastBody!, StringComparison.Ordinal);
        Assert.Contains("Reorder digest", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("Filter Coffee 500ml", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("₹", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("Open dashboard", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Alert_posts_the_reject_details()
    {
        var handler = new CapturingHandler();
        var notifier = Notifier(handler, webhookUrl: "https://example.test/webhook");

        await notifier.PostAlertAsync(new IngestionAlert("2026-09-05", SalesTotal: 100, SalesRejected: 30, RejectRatio: 0.30, Threshold: 0.20));

        Assert.Equal(1, handler.Calls);
        Assert.Contains("Ingestion aborted", handler.LastBody!, StringComparison.Ordinal);
        Assert.Contains("30%", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_url_configured_posts_nothing()
    {
        var handler = new CapturingHandler();
        var notifier = Notifier(handler, webhookUrl: "");

        await notifier.PostDigestAsync(Summary(), Array.Empty<Recommendation>());

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task A_failing_webhook_never_throws()
    {
        // A handler that throws and a handler that returns 500 must both be swallowed.
        var thrown = Notifier(new ThrowingHandler(), webhookUrl: "https://example.test/webhook");
        await thrown.PostDigestAsync(Summary(), Array.Empty<Recommendation>());

        var server500 = Notifier(new CapturingHandler(HttpStatusCode.InternalServerError), webhookUrl: "https://example.test/webhook");
        await server500.PostAlertAsync(new IngestionAlert("2026-09-05", 10, 9, 0.9, 0.2));
    }

    private static WebhookNotifier Notifier(HttpMessageHandler handler, string webhookUrl, string dashboardUrl = "") =>
        new(new HttpClient(handler),
            Options.Create(new NotificationsOptions { WebhookUrl = webhookUrl, DashboardUrl = dashboardUrl }),
            NullLogger<WebhookNotifier>.Instance);

    private static RunSummary Summary() => new(
        "2026-09-05", new DateTimeOffset(2026, 9, 28, 15, 19, 0, TimeSpan.Zero), 200,
        new Dictionary<string, int>
        {
            ["URGENT"] = 28, ["STOCKOUT_RISK"] = 16, ["DEAD_STOCK"] = 5, ["OVERSTOCK"] = 15, ["HEALTHY"] = 136,
        },
        OverstockValue: 12480m, DeadStockValue: 3400m, EstLostSalesAvoided: 9900m,
        QuarantinedRows: 0, RunDurationMs: 42);

    private static Recommendation Rec(string sku, string name, int qty, Flag flag) => new(
        sku, name, null, "SUP-01", "Acme", 0, 1.0, 0.5, 5, 2.0, 3.0, 0.0, qty, flag, Confidence.High, null, "reason");

    private sealed class CapturingHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public string? LastContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            LastContentType = request.Content?.Headers.ContentType?.MediaType;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("boom");
    }
}
