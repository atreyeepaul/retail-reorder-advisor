using System.Net;

namespace Reorder.Web.Tests;

// Uses the in-memory blob fake so the /health blob probe passes without Azurite.
public sealed class SmokeTests(IngestionAppFactory factory) : IClassFixture<IngestionAppFactory>
{
    [Fact]
    public async Task Health_endpoint_returns_healthy()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task Home_page_renders()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Today's action list", body, StringComparison.Ordinal);
    }
}
