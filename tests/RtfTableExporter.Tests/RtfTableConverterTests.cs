using System.Text;

namespace RtfTableExporter.Tests;

public sealed class RtfTableConverterTests
{
    private static readonly Encoding Cp1251 = TestEncodingHelper.CreateCp1251();
    private static readonly string[] ExpectedForm0503152Lines =
    [
        "1. Поступления по доходам - всего|010|x||100,00|200,00",
        "Налог на доходы физических лиц||000 1010201001 0000 110||50,00|75,00",
        "3.Источники финансирования дефицитов бюджетов - всего|500|x||10,00|20,00",
    ];

    [Fact]
    public void Convert_Form0503152CompatibilityRtf_ProducesSameOutputAsStandardVariant()
    {
        using var sandbox = new TestSandbox();
        var standardInputPath = sandbox.CreateRtf("standard-0503152.rtf", SyntheticRtfFactory.CreateForm0503152Standard());
        var compatibilityInputPath = sandbox.CreateRtf("compatibility-0503152.rtf", SyntheticRtfFactory.CreateForm0503152Compatibility());
        var standardOutputPath = sandbox.GetPath("standard-0503152.txt");
        var compatibilityOutputPath = sandbox.GetPath("compatibility-0503152.txt");

        var standardResult = RtfTableConverter.Convert(standardInputPath, standardOutputPath, "|", TextFileEncodingKind.Cp1251);
        var compatibilityResult = RtfTableConverter.Convert(compatibilityInputPath, compatibilityOutputPath, "|", TextFileEncodingKind.Cp1251);

        var standardLines = File.ReadAllLines(standardOutputPath, Cp1251);
        var compatibilityLines = File.ReadAllLines(compatibilityOutputPath, Cp1251);

        Assert.Equal(ExpectedForm0503152Lines, standardLines);
        Assert.Equal(ExpectedForm0503152Lines, compatibilityLines);
        Assert.Equal(standardLines, compatibilityLines);
        Assert.Equal(ExpectedForm0503152Lines.Length, standardResult.RowCount);
        Assert.Equal(ExpectedForm0503152Lines.Length, compatibilityResult.RowCount);
    }

    [Fact]
    public void Convert_Form0503152CompatibilityRtf_PreservesDetailRowsAndCp1251Output()
    {
        using var sandbox = new TestSandbox();
        var inputPath = sandbox.CreateRtf("compatibility-0503152.rtf", SyntheticRtfFactory.CreateForm0503152Compatibility());
        var outputPath = sandbox.GetPath("compatibility-0503152.txt");

        RtfTableConverter.Convert(inputPath, outputPath, "|", TextFileEncodingKind.Cp1251);

        var bytes = File.ReadAllBytes(outputPath);
        var lines = File.ReadAllLines(outputPath, Cp1251);

        Assert.False(HasUtf8Bom(bytes));
        Assert.Equal(ExpectedForm0503152Lines, lines);
        Assert.Contains(lines, line => line.Contains("000 1010201001 0000 110", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("На лог", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("в том числе", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Convert_Form0503152CompatibilityRtf_WritesUtf8BomWhenRequested()
    {
        using var sandbox = new TestSandbox();
        var inputPath = sandbox.CreateRtf("compatibility-0503152.rtf", SyntheticRtfFactory.CreateForm0503152Compatibility());
        var outputPath = sandbox.GetPath("compatibility-0503152-utf8bom.txt");

        RtfTableConverter.Convert(inputPath, outputPath, "|", TextFileEncodingKind.Utf8Bom);

        var bytes = File.ReadAllBytes(outputPath);
        var lines = File.ReadAllLines(outputPath, Encoding.UTF8);

        Assert.True(HasUtf8Bom(bytes));
        Assert.Equal(ExpectedForm0503152Lines, lines);
    }

    private static bool HasUtf8Bom(byte[] bytes)
        => bytes.Length >= 3 &&
           bytes[0] == 0xEF &&
           bytes[1] == 0xBB &&
           bytes[2] == 0xBF;
}

internal static class SyntheticRtfFactory
{
    public static string CreateForm0503152Standard()
        => BuildDocument([
            StandardRow("Форма по ОКУД", "0503152", "", "", "", ""),
            StandardRow("1. Поступления по доходам - всего", "010", "x", "", "100,00", "200,00"),
            StandardRow("в том числе:", "", "", "", "", ""),
            StandardRow("Налог на доходы физических лиц", "", "000 1010201001 0000 110", "", "50,00", "75,00"),
            StandardRow("3.Источники финансирования дефицитов бюджетов - всего", "500", "x", "", "10,00", "20,00"),
        ]);

    public static string CreateForm0503152Compatibility()
        => BuildDocument([
            StandardRow("Форма по ОКУД", "0503152", "", "", "", ""),
            StandardRow("1. Поступления по доходам - всего", "010", "x", "", "100,00", "200,00"),
            StandardRow("в том числе:", "", "", "", "", ""),
            CompatibilityRow("На\r\nлог на доходы физических лиц", "", "000 1010201001 0000 110", "", "50,00", "75,00"),
            StandardRow("3.Источники финансирования дефицитов бюджетов - всего", "500", "x", "", "10,00", "20,00"),
        ]);

    private static string BuildDocument(IEnumerable<string> rows)
        => "{\\rtf1\\ansi\\ansicpg1251\\deff0{\\fonttbl{\\f0 Times New Roman;}}\r\n" +
           string.Join("\r\n", rows) +
           "\r\n}";

    private static string StandardRow(params string[] cells)
    {
        var builder = new StringBuilder();
        builder.Append("{\\trowd");
        AppendCellDefinitions(builder, cells.Length);

        foreach (var cell in cells)
        {
            builder.Append("\\pard\\intbl ");
            builder.Append(EscapeCellText(cell));
            builder.Append("\\cell");
        }

        builder.Append("\\row}");
        return builder.ToString();
    }

    private static string CompatibilityRow(params string[] cells)
    {
        var builder = new StringBuilder();
        builder.Append('{');

        foreach (var cell in cells)
        {
            builder.Append("\\pard\\intbl ");
            builder.Append(EscapeCellText(cell));
            builder.Append("\\cell");
        }

        builder.Append("\\trowd");
        AppendCellDefinitions(builder, cells.Length);
        builder.Append("\\row}");
        return builder.ToString();
    }

    private static void AppendCellDefinitions(StringBuilder builder, int cellCount)
    {
        const int cellWidth = 1800;
        for (var index = 1; index <= cellCount; index++)
        {
            builder.Append("\\cellx");
            builder.Append(index * cellWidth);
        }
    }

    private static string EscapeCellText(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("{", "\\{", StringComparison.Ordinal)
            .Replace("}", "\\}", StringComparison.Ordinal);
}

internal static class TestEncodingHelper
{
    public static Encoding CreateCp1251()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1251);
    }
}

internal sealed class TestSandbox : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(
        Path.GetTempPath(),
        "RtfTableExporterTests",
        Guid.NewGuid().ToString("N"));

    public TestSandbox()
    {
        Directory.CreateDirectory(DirectoryPath);
    }

    public string GetPath(string fileName)
        => Path.Combine(DirectoryPath, fileName);

    public string CreateRtf(string fileName, string content)
    {
        var path = GetPath(fileName);
        File.WriteAllText(path, content, TestEncodingHelper.CreateCp1251());
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
        catch
        {
            // Ignore temp cleanup failures in tests.
        }
    }
}
