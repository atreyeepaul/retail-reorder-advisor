using System.Text;
using Microsoft.Extensions.Options;
using Reorder.Core.Models;

namespace Reorder.Web.Infrastructure;

/// <summary>
/// Posts the digest/alert Adaptive Cards to a Teams (Workflows) incoming webhook. A no-op when no URL
/// is configured, and never fails the caller: any error (including the client timeout) is logged and
/// swallowed so a webhook problem can't fail the advisor or ingestion (spec §7.2). Genuine caller
/// cancellation is still propagated.
/// </summary>
public sealed class WebhookNotifier(
    HttpClient http, IOptions<NotificationsOptions> options, ILogger<WebhookNotifier> logger) : INotifier
{
    private readonly NotificationsOptions _options = options.Value;

    public Task PostDigestAsync(RunSummary summary, IReadOnlyList<Recommendation> topUrgent, CancellationToken ct = default) =>
        PostAsync("digest", () => TeamsCardBuilder.BuildDigest(summary, topUrgent, DashboardUrl), ct);

    public Task PostAlertAsync(IngestionAlert alert, CancellationToken ct = default) =>
        PostAsync("alert", () => TeamsCardBuilder.BuildAlert(alert, DashboardUrl), ct);

    private string? DashboardUrl =>
        string.IsNullOrWhiteSpace(_options.DashboardUrl) ? null : _options.DashboardUrl;

    private async Task PostAsync(string kind, Func<string> buildJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookUrl))
        {
            logger.LogDebug("Webhook not configured; skipping {Kind} notification.", kind);
            return;
        }

        try
        {
            using var content = new StringContent(buildJson(), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(_options.WebhookUrl, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Webhook {Kind} POST returned {StatusCode}.", kind, (int)response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // the caller cancelled the run — let it propagate
        }
        catch (Exception ex)
        {
            // A client timeout surfaces here as a TaskCanceledException with ct not set; swallow it too.
            logger.LogWarning(ex, "Webhook {Kind} POST failed.", kind);
        }
    }
}
