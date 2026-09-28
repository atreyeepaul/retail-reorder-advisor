using System.Text;

namespace Reorder.DataGen;

/// <summary>Minimal RFC 4180 CSV writer: UTF-8 without BOM, LF line endings.</summary>
internal static class Csv
{
    private static readonly char[] CharsNeedingQuotes = [',', '"', '\n', '\r'];

    public static void Write(string path, string header, IEnumerable<string[]> rows)
    {
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.NewLine = "\n";
        writer.WriteLine(header);
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(',', row.Select(Escape)));
        }
    }

    private static string Escape(string value) =>
        value.IndexOfAny(CharsNeedingQuotes) >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
}
