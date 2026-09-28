using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Reorder.Web.Infrastructure;

/// <summary>Health check that reports whether blob storage is reachable.</summary>
public sealed class BlobHealthCheck(IBlobStore blobs) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            return await blobs.PingAsync(ct)
                ? HealthCheckResult.Healthy("Blob storage is reachable.")
                : HealthCheckResult.Unhealthy("Blob storage is unreachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Blob storage is unreachable.", ex);
        }
    }
}
