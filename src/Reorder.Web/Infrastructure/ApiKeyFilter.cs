using System.Security.Cryptography;
using System.Text;

namespace Reorder.Web.Infrastructure;

/// <summary>
/// Endpoint filter that requires a shared secret in a header, compared in constant time.
/// Fails closed: a missing/mismatched header — or an unconfigured key — returns 401.
/// Parameterized by header + key so it also guards the Day 4 job endpoint (<c>X-Job-Key</c>).
/// </summary>
public sealed class ApiKeyFilter(string headerName, string configuredKey) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (string.IsNullOrEmpty(configuredKey))
        {
            return Results.Unauthorized();
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(headerName, out var provided)
            || provided.Count != 1
            || !ConstantTimeEquals(provided.ToString(), configuredKey))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }

    private static bool ConstantTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
