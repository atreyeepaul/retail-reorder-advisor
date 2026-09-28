using System.Net;

namespace Reorder.Web.Tests;

public sealed class HealthCheckTests
{
    [Fact]
    public async Task Health_is_healthy_when_blob_is_reachable()
    {
        using var factory = new IngestionAppFactory();   // BlobReachable defaults to true
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_is_unhealthy_when_blob_is_unreachable()
    {
        using var factory = new IngestionAppFactory();
        factory.Blobs.BlobReachable = false;
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
