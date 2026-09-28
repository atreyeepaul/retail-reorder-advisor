using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reorder.Web.Infrastructure;

namespace Reorder.Web.Tests;

/// <summary>
/// Hosts the app with a known admin key and an in-memory blob store, so upload tests run
/// with no Azurite and can inspect what was written.
/// </summary>
public sealed class IngestionAppFactory : WebApplicationFactory<Program>
{
    public const string AdminKey = "test-admin-key";
    public const string JobKey = "test-job-key";

    public InMemoryBlobStore Blobs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Security:AdminKey", AdminKey);
        builder.UseSetting("Security:JobKey", JobKey);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBlobStore>();
            services.AddSingleton<IBlobStore>(Blobs);
        });
    }
}
