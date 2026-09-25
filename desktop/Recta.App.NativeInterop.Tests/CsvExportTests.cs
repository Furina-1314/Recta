using System.Text;
using Recta.App;
using Xunit;

namespace Recta.App.NativeInterop.Tests;

// CSV 导出器:转义、BOM(Excel 中文直开)、CRLF 行尾与内容往返。
public sealed class CsvExportTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("abc", "abc")]
    [InlineData("班费平摊", "班费平摊")]
    public void Escape_PlainFieldUnchanged(string? input, string expected)
    {
        Assert.Equal(expected, CsvExport.Escape(input));
    }

    [Fact]
    public void Escape_CommaIsQuoted()
    {
        Assert.Equal("\"a,b\"", CsvExport.Escape("a,b"));
    }

    [Fact]
    public void Escape_QuoteIsDoubledAndWrapped()
    {
        Assert.Equal("\"he said \"\"hi\"\"\"", CsvExport.Escape("he said \"hi\""));
    }

    [Fact]
    public void Escape_NewlineIsQuoted()
    {
        var escaped = CsvExport.Escape("line1\nline2\r\nline3");
        Assert.StartsWith("\"", escaped);
        Assert.EndsWith("\"", escaped);
        Assert.Contains("\n", escaped);
    }

    [Fact]
    public void Build_JoinsRowsWithCrLf()
    {
        var csv = CsvExport.Build(
        [
            new[] { "单号", "金额(元)" },
            new[] { "REQ-000001", "24.00" },
            new[] { "INF-000002", "50.00" },
        ]);
        Assert.Equal("单号,金额(元)\r\nREQ-000001,24.00\r\nINF-000002,50.00\r\n", csv);
    }

    [Fact]
    public void Build_QuotedFieldSurvivesCommaInside()
    {
        var csv = CsvExport.Build([new[] { "备注", "x" }, new[] { "垫资,共2笔", "y" }]);
        Assert.Equal("备注,x\r\n\"垫资,共2笔\",y\r\n", csv);
    }

    [Fact]
    public void Write_EmitsUtf8BomAndChineseContent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"recta_csv_{Guid.NewGuid():N}.csv");
        try
        {
            CsvExport.Write(path,
            [
                new[] { "姓名", "余额(元)" },
                new[] { "陈一", "-18.33" },
                new[] { "李二,钱三", "16.67" },
            ]);

            var bytes = File.ReadAllBytes(path);
            Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]); // UTF-8 BOM

            var text = new UTF8Encoding(false).GetString(bytes[3..]);
            Assert.Contains("陈一,-18.33", text);
            Assert.Contains("\"李二,钱三\",16.67", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Write_RoundTripThroughMicrosoftParser()
    {
        // 用 .NET 内置 TextFieldParser 验证产物符合 RFC 4180(与 Excel 同源逻辑)。
        var path = Path.Combine(Path.GetTempPath(), $"recta_csv_{Guid.NewGuid():N}.csv");
        try
        {
            CsvExport.Write(path,
            [
                new[] { "事项", "金额(元)" },
                new[] { "含,逗号", "1.00" },
                new[] { "含\"引号", "2.00" },
            ]);

            using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(path)
            {
                HasFieldsEnclosedInQuotes = true,
                Delimiters = [","],
            };
            Assert.Equal(["事项", "金额(元)"], parser.ReadFields());
            Assert.Equal(["含,逗号", "1.00"], parser.ReadFields());
            Assert.Equal(["含\"引号", "2.00"], parser.ReadFields());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
