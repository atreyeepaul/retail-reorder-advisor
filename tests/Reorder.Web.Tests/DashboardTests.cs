using System.Net;
using System.Net.Http.Headers;

namespace Reorder.Web.Tests;

public sealed class DashboardTests
{
    [Fact]
    public async Task Action_list_renders_the_table_after_a_run()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();
        await Seed(client);

        using var response = await client.GetAsync("/");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Today's action list", body, StringComparison.Ordinal);
        Assert.Contains("<table", body, StringComparison.Ordinal);
        Assert.Contains("Export CSV", body, StringComparison.Ordinal);
        Assert.Contains("badge--urgent", body, StringComparison.Ordinal);
        Assert.Contains("/?flag=URGENT", body, StringComparison.Ordinal);   // a filter chip link
    }

    [Fact]
    public async Task Filter_flows_into_the_export_link()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();
        await Seed(client);

        using var response = await client.GetAsync("/?flag=URGENT");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("export.csv?flag=URGENT", body, StringComparison.Ordinal);
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
