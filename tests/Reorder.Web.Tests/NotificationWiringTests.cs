using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reorder.Core.Models;
using Reorder.Web.Infrastructure;

namespace Reorder.Web.Tests;

public sealed class NotificationWiringTests
{
    [Fact]
    public async Task A_successful_run_posts_one_digest()
    {
        var notifier = new RecordingNotifier();
        using var baseFactory = new IngestionAppFactory();
        using var factory = WithNotifier(baseFactory, notifier);
        using var client = factory.CreateClient();

        await UploadClean(client, SampleData.LatestAsOf);
        await RunAdvisor(client, SampleData.LatestAsOf);

        Assert.Equal(1, notifier.DigestCalls);
        Assert.Equal(0, notifier.AlertCalls);
        Assert.Equal(200, notifier.LastDigestSummary!.SkuCount);
        Assert.True(notifier.LastDigestTopUrgent!.Count <= 5);
        Assert.All(notifier.LastDigestTopUrgent, r => Assert.Equal(Flag.Urgent, r.Flag));
    }

    [Fact]
    public async Task An_aborted_ingestion_posts_an_alert_and_writes_no_normalized_data()
    {
        var notifier = new RecordingNotifier();
        using var baseFactory = new IngestionAppFactory();
        using var factory = WithNotifier(baseFactory, notifier);
        using var client = factory.CreateClient();

        const string asOf = "2026-09-06";
        await UploadDirty(client, asOf);

        Assert.Equal(1, notifier.AlertCalls);
        Assert.Equal(0, notifier.DigestCalls);
        Assert.True(notifier.LastAlert!.RejectRatio > notifier.LastAlert.Threshold);
        Assert.False(baseFactory.Blobs.Has(Containers.Normalized, $"{asOf}.json"));   // aborted → nothing normalized
    }

    private static WebApplicationFactory<Program> WithNotifier(IngestionAppFactory baseFactory, INotifier notifier) =>
        baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<INotifier>();
                services.AddSingleton(notifier);
            }));

    private static async Task UploadClean(HttpClient client, string asOf)
    {
        using var form = new MultipartFormDataContent();
        AddFile(form, "sales", SampleData.FilePath(asOf, "sales.csv"));
        AddFile(form, "inventory", SampleData.FilePath(asOf, "inventory.csv"));
        AddFile(form, "suppliers", SampleData.FilePath(asOf, "suppliers.csv"));
        await SendUpload(client, form, asOf);
    }

    private static async Task UploadDirty(HttpClient client, string asOf)
    {
        // 4 of 5 sales rows reference SKUs not in inventory → 80% rejected, well over the 20% threshold.
        const string dirtySales =
            "date,sku,qty_sold,unit_price\n" +
            "2026-06-01,1001,5,10.00\n" +
            "2026-06-01,999999,5,10.00\n" +
            "2026-06-01,999998,5,10.00\n" +
            "2026-06-01,999997,5,10.00\n" +
            "2026-06-01,999996,5,10.00\n";

        using var form = new MultipartFormDataContent();
        var sales = new ByteArrayContent(Encoding.UTF8.GetBytes(dirtySales));
        sales.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(sales, "sales", "sales.csv");
        AddFile(form, "inventory", SampleData.FilePath(SampleData.LatestAsOf, "inventory.csv"));
        AddFile(form, "suppliers", SampleData.FilePath(SampleData.LatestAsOf, "suppliers.csv"));
        await SendUpload(client, form, asOf);
    }

    private static async Task SendUpload(HttpClient client, MultipartFormDataContent form, string asOf)
    {
        using var upload = new HttpRequestMessage(HttpMethod.Post, $"/api/uploads?asOf={asOf}") { Content = form };
        upload.Headers.Add("X-Admin-Key", IngestionAppFactory.AdminKey);
        (await client.SendAsync(upload)).EnsureSuccessStatusCode();
    }

    private static async Task RunAdvisor(HttpClient client, string asOf)
    {
        using var run = new HttpRequestMessage(HttpMethod.Post, $"/api/jobs/run-advisor?asOf={asOf}");
        run.Headers.Add("X-Job-Key", IngestionAppFactory.JobKey);
        (await client.SendAsync(run)).EnsureSuccessStatusCode();
    }

    private static void AddFile(MultipartFormDataContent form, string field, string path)
    {
        var content = new ByteArrayContent(File.ReadAllBytes(path));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(content, field, Path.GetFileName(path));
    }

    private sealed class RecordingNotifier : INotifier
    {
        public int DigestCalls { get; private set; }
        public int AlertCalls { get; private set; }
        public RunSummary? LastDigestSummary { get; private set; }
        public IReadOnlyList<Recommendation>? LastDigestTopUrgent { get; private set; }
        public IngestionAlert? LastAlert { get; private set; }

        public Task PostDigestAsync(RunSummary summary, IReadOnlyList<Recommendation> topUrgent, CancellationToken ct = default)
        {
            DigestCalls++;
            LastDigestSummary = summary;
            LastDigestTopUrgent = topUrgent;
            return Task.CompletedTask;
        }

        public Task PostAlertAsync(IngestionAlert alert, CancellationToken ct = default)
        {
            AlertCalls++;
            LastAlert = alert;
            return Task.CompletedTask;
        }
    }
}
