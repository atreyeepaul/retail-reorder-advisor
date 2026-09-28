namespace Reorder.Web.Tests;

/// <summary>Locates the committed synthetic dataset under the repo's <c>data/</c> folder.</summary>
public static class SampleData
{
    public const string LatestAsOf = "2026-09-05";

    public static string FilePath(string asOf, string fileName) =>
        Path.Combine(RepoRoot(), "data", asOf, fileName);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RetailReorderAdvisor.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repo root (RetailReorderAdvisor.sln).");
    }
}
