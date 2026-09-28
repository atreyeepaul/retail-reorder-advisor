namespace Reorder.Web.Infrastructure;

/// <summary>
/// Blob storage configuration (section <c>Storage</c>). Locally, <see cref="ConnectionString"/>
/// points at Azurite; in Azure it is empty and <see cref="BlobServiceUri"/> is used with the
/// web app's managed identity.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string BlobServiceUri { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
}
