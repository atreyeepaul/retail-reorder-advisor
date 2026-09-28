using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Reorder.Core.Models;

namespace Reorder.Web.Tests;

public sealed class ReadEndpointTests
{
    private static readonly JsonSerializerOptions WebJson =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Summary_after_a_run_returns_the_summary()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();
        await Seed(client);

        using var response = await client.GetAsync("/api/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = (await response.Content.ReadFromJsonAsync<RunSummary>(WebJson))!;
        Assert.Equal(200, summary.SkuCount);
        Assert.Equal(232, summary.QuarantinedRows);
        Assert.Equal(200, summary.CountsByFlag.Values.Sum());
    }

    [Fact]
    public async Task Recommendations_returns_all_ranked()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();
        await Seed(client);

        var recs = (await client.GetFromJsonAsync<List<Recommendation>>("/api/recommendations", WebJson))!;

        Assert.Equal(200, recs.Count);
        Assert.True((int)recs[0].Flag <= (int)recs[^1].Flag);   // ranked by severity
    }

    [Fact]
    public async Task Recommendations_filtered_by_flag()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();
        await Seed(client);

        var recs = (await client.GetFromJsonAsync<List<Recommendation>>("/api/recommendations?flag=URGENT", WebJson))!;

        Assert.NotEmpty(recs);
        Assert.All(recs, r => Assert.Equal(Flag.Urgent, r.Flag));
    }

    [Fact]
    public async Task Recommendations_unknown_flag_is_bad_request()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();
        await Seed(client);

        using var response = await client.GetAsync("/api/recommendations?flag=BOGUS");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Export_csv_returns_csv_with_header_and_a_row_per_recommendation()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();
        await Seed(client);

        using var response = await client.GetAsync("/api/recommendations/export.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        string csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("sku,name,category,", lines[0]);
        Assert.Equal(201, lines.Length);   // header + 200 rows
    }

    [Fact]
    public async Task Export_csv_respects_the_flag_filter()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();
        await Seed(client);

        var urgent = (await client.GetFromJsonAsync<List<Recommendation>>("/api/recommendations?flag=URGENT", WebJson))!;

        using var response = await client.GetAsync("/api/recommendations/export.csv?flag=URGENT");
        string csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(urgent.Count + 1, lines.Length);   // header + URGENT rows
    }

    [Fact]
    public async Task Read_endpoints_return_404_before_any_run()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/recommendations")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/recommendations/export.csv")).StatusCode);
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
