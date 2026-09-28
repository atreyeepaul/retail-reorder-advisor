using System.Net;
using System.Net.Http.Headers;

namespace Reorder.Web.Tests;

public sealed class HealthSummaryTests
{
    [Fact]
    public async Task Health_summary_renders_tiles_and_the_flag_bar_after_a_run()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();
        await Seed(client);

        using var response = await client.GetAsync("/health-summary");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Inventory health", body, StringComparison.Ordinal);
        Assert.Contains("At-risk SKUs", body, StringComparison.Ordinal);
        Assert.Contains("Overstock value", body, StringComparison.Ordinal);
        Assert.Contains("Dead stock", body, StringComparison.Ordinal);
        Assert.Contains("Est. lost sales avoided", body, StringComparison.Ordinal);
        Assert.Contains("Last run", body, StringComparison.Ordinal);
        Assert.Contains("₹", body, StringComparison.Ordinal);           // INR money figure
        Assert.Contains("flag-bar", body, StringComparison.Ordinal);    // the distribution bar
        Assert.Contains("seg--urgent", body, StringComparison.Ordinal); // a colored segment
    }

    private static async Task Seed(HttpClient client)
    {
        const string asOf = SampleData.LatestAsOf;

        using var form = new MultipartFormDataContent();
        AddFile(form, "sales", SampleData.FilePath(asOf, "sales.csv"));
        AddFile(form, "inventory", SampleData.FilePath(asOf, "inventory.csv"));
        AddFile(form, "suppliers", SampleData.FilePath(asOf, "suppliers.csv"));
        using var upload = new HttpRequestMessage(HttpMethod.Post, $"/api/uploads?asOf={asOf}") { Content = form };
        upload.Headers.Add("X-Admin-Key", IngestionAppFactory.AdminKey);
        (await client.SendAsync(upload)).EnsureSuccessStatusCode();

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
}
