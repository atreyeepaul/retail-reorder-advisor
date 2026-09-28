namespace Reorder.Web.Infrastructure;

/// <summary>
/// Webhook configuration (section <c>Notifications</c>). Empty locally unless set via user-secrets;
/// set from Bicep-managed app settings in Azure. When <see cref="WebhookUrl"/> is empty the notifier
/// is a no-op, so dev/test runs never post anywhere.
/// </summary>
public sealed class NotificationsOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Teams (Workflows) incoming-webhook URL the digest/alert cards are POSTed to.</summary>
    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>Public dashboard URL for the card's "Open dashboard" action; the action is omitted when empty.</summary>
    public string DashboardUrl { get; set; } = string.Empty;
}
