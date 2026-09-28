using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Reorder.Core.Ingestion;
using Reorder.Web.Infrastructure;
using Reorder.Web.Services;

namespace Reorder.Web.Tests;

public sealed class UploadEndpointTests
{
    private static readonly JsonSerializerOptions WebJson =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Valid_upload_writes_blobs_and_reports_expected_rejects()
    {
        const string asOf = SampleData.LatestAsOf;
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();

        using var form = RealForm(asOf);
        using var response = await Post(client, asOf, form, IngestionAppFactory.AdminKey);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<IngestionResult>(WebJson))!;

        Assert.Equal(IngestionResult.StatusOk, result.Status);
        Assert.Equal(result.Sales.Accepted + result.Sales.Rejected, result.Sales.Total);
        Assert.True(result.Sales.RejectRatio < 0.20, $"reject ratio {result.Sales.RejectRatio} should be under threshold");
        Assert.True(result.RejectsSample.Count <= 50);

        // Raw + outputs written.
        Assert.True(factory.Blobs.Has(Containers.Raw, $"{asOf}/sales.csv"));
        Assert.True(factory.Blobs.Has(Containers.Raw, $"{asOf}/inventory.csv"));
        Assert.True(factory.Blobs.Has(Containers.Raw, $"{asOf}/suppliers.csv"));
        Assert.True(factory.Blobs.Has(Containers.Normalized, $"{asOf}.json"));
        Assert.True(factory.Blobs.Has(Containers.Quarantine, $"{asOf}.json"));

        // Deterministic reject categories (see plan: the cumulative file makes FUTURE_DATE /
        // DUPLICATE_KEY / BAD_TYPE drift from the latest-folder labels, so those are only > 0).
        var report = JsonSerializer.Deserialize(
            factory.Blobs.Get(Containers.Quarantine, $"{asOf}.json")!,
            IngestionJsonContext.Default.QuarantineReport)!;
        int SalesRejects(RejectReason reason) =>
            report.Rejects.Count(r => r.File == "sales.csv" && r.Reason == reason);

        Assert.Equal(37, SalesRejects(RejectReason.NegativeQty));
        Assert.Equal(38, SalesRejects(RejectReason.UnknownSku));
        Assert.Equal(26 + 28, SalesRejects(RejectReason.MissingField));   // missingDate + missingPrice
        Assert.True(SalesRejects(RejectReason.BadType) > 0);
        Assert.True(SalesRejects(RejectReason.FutureDate) > 0);
        Assert.True(SalesRejects(RejectReason.DuplicateKey) > 0);

        // Normalized sanity: clean inventory/suppliers, and SUP-07's blank lead time is null (not rejected).
        var dataset = JsonSerializer.Deserialize(
            factory.Blobs.Get(Containers.Normalized, $"{asOf}.json")!,
            IngestionJsonContext.Default.NormalizedDataset)!;
        Assert.Equal(200, dataset.Inventory.Count);
        Assert.Equal(8, dataset.Suppliers.Count);
        Assert.Contains(dataset.Suppliers, s => s.Id == "SUP-07" && s.LeadTimeDays is null);
    }

    [Fact]
    public async Task Upload_over_reject_threshold_aborts_without_writing_normalized()
    {
        const string asOf = "2026-01-01";
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();

        using var form = new MultipartFormDataContent();
        AddText(form, "suppliers", "suppliers.csv", "supplier_id,name,lead_time_days,moq,pack_size\nSUP-01,Acme,5,1,1\n");
        AddText(form, "inventory", "inventory.csv", "sku,name,category,on_hand,unit_cost,supplier_id\n1001,Widget,Cat,10,5.00,SUP-01\n");
        AddText(form, "sales", "sales.csv",
            "date,sku,qty_sold,unit_price\n"
            + "2026-01-01,1001,5,10.00\n"    // 1 valid
            + "2026-01-01,1001,-1,10.00\n"   // 4 negative → 80% rejected
            + "2026-01-01,1001,-2,10.00\n"
            + "2026-01-01,1001,-3,10.00\n"
            + "2026-01-01,1001,-4,10.00\n");

        using var response = await Post(client, asOf, form, IngestionAppFactory.AdminKey);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<IngestionResult>(WebJson))!;

        Assert.Equal(IngestionResult.StatusAborted, result.Status);
        Assert.True(result.Sales.RejectRatio > 0.20);
        Assert.False(factory.Blobs.Has(Containers.Normalized, $"{asOf}.json"));   // last good output preserved
        Assert.True(factory.Blobs.Has(Containers.Quarantine, $"{asOf}.json"));    // still diagnosable
    }

    [Fact]
    public async Task Missing_admin_key_is_unauthorized()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();

        using var form = RealForm(SampleData.LatestAsOf);
        using var response = await Post(client, SampleData.LatestAsOf, form, adminKey: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_admin_key_is_unauthorized()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();

        using var form = RealForm(SampleData.LatestAsOf);
        using var response = await Post(client, SampleData.LatestAsOf, form, adminKey: "not-the-key");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Missing_file_is_bad_request()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();

        using var form = new MultipartFormDataContent();
        AddText(form, "sales", "sales.csv", "date,sku,qty_sold,unit_price\n");
        // inventory + suppliers omitted

        using var response = await Post(client, SampleData.LatestAsOf, form, IngestionAppFactory.AdminKey);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bad_as_of_is_bad_request()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();

        using var form = RealForm(SampleData.LatestAsOf);
        using var response = await Post(client, "not-a-date", form, IngestionAppFactory.AdminKey);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> Post(
        HttpClient client, string asOf, MultipartFormDataContent form, string? adminKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/uploads?asOf={asOf}") { Content = form };
        if (adminKey is not null)
        {
            request.Headers.Add("X-Admin-Key", adminKey);
        }

        return await client.SendAsync(request);
    }

    private static MultipartFormDataContent RealForm(string asOf)
    {
        var form = new MultipartFormDataContent();
        AddFile(form, "sales", SampleData.FilePath(asOf, "sales.csv"));
        AddFile(form, "inventory", SampleData.FilePath(asOf, "inventory.csv"));
        AddFile(form, "suppliers", SampleData.FilePath(asOf, "suppliers.csv"));
        return form;
    }

    private static void AddFile(MultipartFormDataContent form, string field, string path)
    {
        var content = new ByteArrayContent(File.ReadAllBytes(path));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(content, field, Path.GetFileName(path));
    }

    private static void AddText(MultipartFormDataContent form, string field, string fileName, string text)
    {
        var content = new StringContent(text);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(content, field, fileName);
    }
}
