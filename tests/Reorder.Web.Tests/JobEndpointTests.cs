using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Reorder.Core.Advisor;
using Reorder.Core.Models;
using Reorder.Web.Infrastructure;

namespace Reorder.Web.Tests;

public sealed class JobEndpointTests
{
    private static readonly JsonSerializerOptions WebJson =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Upload_then_run_writes_snapshot_and_latest()
    {
        const string asOf = SampleData.LatestAsOf;
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();

        await Upload(client, asOf);
        using var response = await RunAdvisor(client, asOf, IngestionAppFactory.JobKey);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = (await response.Content.ReadFromJsonAsync<RunSummary>(WebJson))!;
        Assert.Equal(200, summary.SkuCount);
        Assert.Equal(200, summary.CountsByFlag.Values.Sum());
        Assert.Equal(232, summary.QuarantinedRows);   // from quarantine/2026-09-05.json (Day 2 total)
        Assert.NotEqual(default, summary.GeneratedAtUtc);

        Assert.True(factory.Blobs.Has(Containers.Recommendations, $"{asOf}.json"));
        Assert.True(factory.Blobs.Has(Containers.Recommendations, "latest.json"));

        string latest = factory.Blobs.Get(Containers.Recommendations, "latest.json")!;
        Assert.Contains("\"summary\"", latest);
        Assert.Contains("\"recommendations\"", latest);
        Assert.Contains("\"countsByFlag\"", latest);
        Assert.Contains("\"supplierId\"", latest);   // camelCase
        Assert.Contains("\"URGENT\"", latest);

        var parsed = JsonSerializer.Deserialize(latest, AdvisorJsonContext.Default.RunResult)!;
        Assert.Equal(200, parsed.Recommendations.Count);
        // ranked by flag severity: the first is no less severe than the last
        Assert.True((int)parsed.Recommendations[0].Flag <= (int)parsed.Recommendations[^1].Flag);
    }

    [Fact]
    public async Task Run_without_as_of_uses_the_latest_normalized()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();

        await Upload(client, SampleData.LatestAsOf);
        using var response = await RunAdvisor(client, asOf: null, IngestionAppFactory.JobKey);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(factory.Blobs.Has(Containers.Recommendations, "latest.json"));
    }

    [Fact]
    public async Task Missing_job_key_is_unauthorized()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();

        using var response = await RunAdvisor(client, SampleData.LatestAsOf, jobKey: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_job_key_is_unauthorized()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();

        using var response = await RunAdvisor(client, SampleData.LatestAsOf, jobKey: "not-the-key");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Run_while_locked_returns_conflict()
    {
        const string asOf = SampleData.LatestAsOf;
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();

        await Upload(client, asOf);
        await using var held = await factory.Blobs.TryAcquireLeaseAsync(
            Containers.Locks, "advisor.lock", TimeSpan.FromMinutes(1));
        Assert.NotNull(held);

        using var response = await RunAdvisor(client, asOf, IngestionAppFactory.JobKey);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Run_with_no_normalized_data_returns_not_found()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();

        using var response = await RunAdvisor(client, "2020-01-01", IngestionAppFactory.JobKey);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_as_of_is_bad_request()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();

        using var response = await RunAdvisor(client, "not-a-date", IngestionAppFactory.JobKey);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> RunAdvisor(HttpClient client, string? asOf, string? jobKey)
    {
        string url = asOf is null ? "/api/jobs/run-advisor" : $"/api/jobs/run-advisor?asOf={asOf}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (jobKey is not null)
        {
            request.Headers.Add("X-Job-Key", jobKey);
        }

        return await client.SendAsync(request);
    }

    private static async Task Upload(HttpClient client, string asOf)
    {
        using var form = new MultipartFormDataContent();
        AddFile(form, "sales", SampleData.FilePath(asOf, "sales.csv"));
        AddFile(form, "inventory", SampleData.FilePath(asOf, "inventory.csv"));
        AddFile(form, "suppliers", SampleData.FilePath(asOf, "suppliers.csv"));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/uploads?asOf={asOf}") { Content = form };
        request.Headers.Add("X-Admin-Key", IngestionAppFactory.AdminKey);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static void AddFile(MultipartFormDataContent form, string field, string path)
    {
        var content = new ByteArrayContent(File.ReadAllBytes(path));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(content, field, Path.GetFileName(path));
    }
}
