using System.Net;

namespace Reorder.Web.Tests;

public sealed class UploadPageTests
{
    [Fact]
    public async Task Upload_page_renders_the_form()
    {
        using var factory = new IngestionAppFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/upload");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Upload &amp; run", body, StringComparison.Ordinal);
        Assert.Contains("type=\"file\"", body, StringComparison.Ordinal);
        Assert.Contains("id=\"result\"", body, StringComparison.Ordinal);
        Assert.Contains("js/upload.js", body, StringComparison.Ordinal);
    }
}
