namespace Reorder.Web.Infrastructure;

/// <summary>
/// API keys for the write endpoints (section <c>Security</c>). Empty locally unless set via
/// user-secrets; set from Bicep-managed app settings in Azure.
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Key required by <c>POST /api/jobs/run-advisor</c> (Day 4).</summary>
    public string JobKey { get; set; } = string.Empty;

    /// <summary>Key required by <c>POST /api/uploads</c>.</summary>
    public string AdminKey { get; set; } = string.Empty;
}
