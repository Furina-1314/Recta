using System.Text;

namespace Recta.App;

// CSV 导出:UTF-8 带 BOM(Excel 直接打开中文不乱码)、CRLF 行尾、标准引号转义。
public static class CsvExport
{
    public static string Escape(string? field)
    {
        var text = field ?? "";
        var needsQuote = text.Contains(',') || text.Contains('"') ||
                         text.Contains('\n') || text.Contains('\r');
        return needsQuote ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    public static string Build(IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.Append(string.Join(',', row.Select(Escape)));
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    public static void Write(string path, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        File.WriteAllText(path, Build(rows), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}
