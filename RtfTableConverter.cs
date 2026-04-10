using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using RtfPipe;

namespace RtfTableExporter;

internal static class RtfTableConverter
{
    private const string OutputNewLine = "\r\n";
    private static readonly string[] SupportedInputExtensions = [".rtf", ".docx"];

    public static bool IsSupportedInputExtension(string path)
        => SupportedInputExtensions.Any(extension =>
            string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase));

    public static ConversionResult Convert(string inputPath, string outputPath, string delimiter, TextFileEncodingKind outputEncoding)
    {
        var normalizedInputPath = Path.GetFullPath(inputPath);
        var normalizedOutputPath = Path.GetFullPath(outputPath);

        if (!File.Exists(normalizedInputPath))
        {
            throw new FileNotFoundException("Input file was not found.", normalizedInputPath);
        }

        if (!IsSupportedInputExtension(normalizedInputPath))
        {
            throw new InvalidOperationException("Only .rtf and .docx files are supported.");
        }

        var liveRows = ExtractRowsForExport(normalizedInputPath);
        if (liveRows.Count == 0)
        {
            throw new NoTablesFoundException("The input file does not contain exportable data rows.");
        }

        var directory = Path.GetDirectoryName(normalizedOutputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Output directory could not be resolved.");
        }

        Directory.CreateDirectory(directory);
        WriteRows(normalizedOutputPath, liveRows, delimiter, outputEncoding);

        return new ConversionResult(
            normalizedInputPath,
            normalizedOutputPath,
            liveRows.Count,
            liveRows.Max(row => row.Count));
    }

    private static IReadOnlyList<IReadOnlyList<string>> ExtractRowsForExport(string inputPath)
        => string.Equals(Path.GetExtension(inputPath), ".docx", StringComparison.OrdinalIgnoreCase)
            ? ExtractRowsForExportFromDocx(inputPath)
            : ExtractRowsForExportFromRtf(inputPath);

    private static IReadOnlyList<IReadOnlyList<string>> ExtractRowsForExportFromRtf(string inputPath)
    {
        var rawIntblRows = ParseRawRtfTableRows(inputPath, RawRtfRowCaptureMode.Intbl);
        var rawTrowdRows = ParseRawRtfTableRows(inputPath, RawRtfRowCaptureMode.Trowd);
        var candidates = new List<RtfExtractionCandidate>();

        AddRtfCandidate(candidates, "rtf-raw-intbl", ExtractRowsForExportFromRawRows(rawIntblRows));
        if (!AreRowsEqual(rawIntblRows, rawTrowdRows))
        {
            AddRtfCandidate(candidates, "rtf-raw-trowd", ExtractRowsForExportFromRawRows(rawTrowdRows));
        }

        TryAddHtmlRtfCandidate(candidates, inputPath);

        if (candidates.Count == 0)
        {
            throw new NoTablesFoundException("The input file does not contain exportable data rows.");
        }

        var reportKind = DetectRtfReportKind(rawIntblRows, rawTrowdRows, candidates);
        return SelectBestRtfCandidate(candidates, reportKind).Rows;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ExtractRowsForExportFromDocx(string inputPath)
        => ExtractRowsForExportFromParsedTables(ParseDocxTables(inputPath));

    private static bool IsRtfPipeTableLayoutFailure(KeyNotFoundException exception)
        => exception.Message.Contains("RtfPipe.UnitValue", StringComparison.Ordinal);

    private static bool IsRecoverableRtfHtmlFailure(Exception exception)
        => exception switch
        {
            NoTablesFoundException => true,
            KeyNotFoundException keyNotFoundException => IsRtfPipeTableLayoutFailure(keyNotFoundException),
            OutOfMemoryException => false,
            StackOverflowException => false,
            AccessViolationException => false,
            _ => true,
        };

    private static void TryAddHtmlRtfCandidate(ICollection<RtfExtractionCandidate> candidates, string inputPath)
    {
        try
        {
            AddRtfCandidate(candidates, "rtf-html", ExtractRowsForExportFromHtml(inputPath));
        }
        catch (Exception ex) when (IsRecoverableRtfHtmlFailure(ex))
        {
        }
    }

    private static void AddRtfCandidate(
        ICollection<RtfExtractionCandidate> candidates,
        string strategyName,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        if (candidates.Any(candidate => AreRowsEqual(candidate.Rows, rows)))
        {
            return;
        }

        candidates.Add(new RtfExtractionCandidate(
            strategyName,
            rows,
            AnalyzeExportRows(rows)));
    }

    private static IReadOnlyList<IReadOnlyList<string>> ExtractRowsForExportFromHtml(string inputPath)
    {
        using var inputStream = File.OpenRead(inputPath);
        var html = Rtf.ToHtml(inputStream, new RtfHtmlSettings
        {
            Indent = false,
            NewLineChars = "\n",
            NewLineOnAttributes = false,
        });

        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);
        var topLevelTables = document.QuerySelectorAll("table")
            .Where(IsTopLevelTable)
            .Select(ParseTopLevelTable)
            .ToList();

        return ExtractRowsForExportFromParsedTables(topLevelTables);
    }

    private static IReadOnlyList<IReadOnlyList<string>> ExtractRowsForExportFromParsedTables(IReadOnlyList<ParsedTopLevelTable> topLevelTables)
    {
        if (topLevelTables.Count == 0)
        {
            throw new NoTablesFoundException("No top-level tables were found in the input file.");
        }

        if (topLevelTables.Any(table => table.NormalizedText.Contains("0503152", StringComparison.OrdinalIgnoreCase)))
        {
            var form0503152Rows = ExtractForm0503152Rows(topLevelTables.SelectMany(table => table.Rows));
            if (form0503152Rows.Count > 0)
            {
                return form0503152Rows;
            }
        }

        var sectionRows = new List<IReadOnlyList<string>>();
        AppendSectionRows(sectionRows, topLevelTables, TableSectionTitle.Income);
        AppendSectionRows(sectionRows, topLevelTables, TableSectionTitle.Expense);

        if (sectionRows.Count > 0)
        {
            return sectionRows.ToArray();
        }

        return ExtractGenericTableRows(topLevelTables.Select(table => table.Rows));
    }

    private static IReadOnlyList<ParsedTopLevelTable> ParseDocxTables(string inputPath)
    {
        using var archive = ZipFile.OpenRead(inputPath);
        var documentEntry = GetDocxEntry(archive, "word/document.xml")
            ?? throw new InvalidOperationException("DOCX main document part word/document.xml was not found.");

        using var documentStream = documentEntry.Open();
        var document = XDocument.Load(documentStream);
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        return document
            .Descendants(word + "tbl")
            .Where(table => !table.Ancestors(word + "tbl").Any())
            .Select(table => ParseDocxTable(table, word))
            .ToArray();
    }

    private static ZipArchiveEntry? GetDocxEntry(ZipArchive archive, string normalizedFullName)
        => archive.Entries.FirstOrDefault(entry =>
            string.Equals(
                entry.FullName.Replace('\\', '/'),
                normalizedFullName,
                StringComparison.OrdinalIgnoreCase));

    private static ParsedTopLevelTable ParseDocxTable(XElement tableElement, XNamespace word)
    {
        var grid = tableElement
            .Elements(word + "tr")
            .Select(row => ParseDocxRow(row, word).Select<string, string?>(cell => cell).ToList())
            .ToList();

        var tableData = new TableData(NormalizeGrid(grid));
        var normalizedText = NormalizeCellText(string.Join(' ', tableData.Rows.SelectMany(row => row)));
        var liveRows = ExtractLiveRows(tableData);

        return new ParsedTopLevelTable(
            DetectSectionTitle(normalizedText),
            normalizedText,
            liveRows,
            tableData.Rows);
    }

    private static IReadOnlyList<string> ParseDocxRow(XElement rowElement, XNamespace word)
    {
        var row = new List<string>();
        foreach (var cellElement in rowElement.Elements(word + "tc"))
        {
            row.Add(ParseDocxCell(cellElement, word));

            var gridSpan = ParseDocxGridSpan(cellElement, word);
            for (var index = 1; index < gridSpan; index++)
            {
                row.Add(string.Empty);
            }
        }

        return row;
    }

    private static string ParseDocxCell(XElement cellElement, XNamespace word)
    {
        var paragraphs = cellElement
            .Elements(word + "p")
            .Select(paragraph => ParseDocxParagraph(paragraph, word))
            .Where(text => !string.IsNullOrWhiteSpace(text));

        return NormalizeCellText(string.Join(' ', paragraphs));
    }

    private static string ParseDocxParagraph(XElement paragraphElement, XNamespace word)
    {
        var builder = new StringBuilder();
        foreach (var element in paragraphElement.Descendants())
        {
            if (element.Name == word + "t")
            {
                builder.Append(element.Value);
            }
            else if (element.Name == word + "tab" || element.Name == word + "br" || element.Name == word + "cr")
            {
                builder.Append(' ');
            }
        }

        return builder.ToString();
    }

    private static int ParseDocxGridSpan(XElement cellElement, XNamespace word)
    {
        var rawValue = cellElement
            .Element(word + "tcPr")?
            .Element(word + "gridSpan")?
            .Attribute(word + "val")?
            .Value;

        return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : 1;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ExtractRowsForExportFromRawRtf(string inputPath)
        => ExtractRowsForExportFromRawRows(ParseRawRtfTableRows(inputPath));

    private static IReadOnlyList<IReadOnlyList<string>> ExtractRowsForExportFromRawRows(IReadOnlyList<IReadOnlyList<string>> rawRows)
    {
        if (rawRows.Count == 0)
        {
            return [];
        }

        if (LooksLikeForm0503152(rawRows))
        {
            return ExtractForm0503152Rows(rawRows);
        }

        return NormalizeExportRows(rawRows);
    }

    private static DetectedReportKind DetectRtfReportKind(
        IReadOnlyList<IReadOnlyList<string>> rawIntblRows,
        IReadOnlyList<IReadOnlyList<string>> rawTrowdRows,
        IReadOnlyList<RtfExtractionCandidate> candidates)
    {
        var sourceRows = rawIntblRows.Count >= rawTrowdRows.Count
            ? rawIntblRows
            : rawTrowdRows;

        if (LooksLikeForm0503152(sourceRows))
        {
            return DetectedReportKind.Form0503152;
        }

        if (LooksLikeForm0531857(sourceRows) ||
            candidates.Any(candidate => candidate.Stats.Form0531857DataRowCount > 0 || candidate.Stats.Form0531857SubtotalRowCount > 0))
        {
            return DetectedReportKind.Form0531857;
        }

        return DetectedReportKind.Unknown;
    }

    private static RtfExtractionCandidate SelectBestRtfCandidate(
        IReadOnlyList<RtfExtractionCandidate> candidates,
        DetectedReportKind reportKind)
        => candidates
            .OrderByDescending(candidate => ScoreRtfCandidate(candidate, reportKind))
            .ThenByDescending(candidate => candidate.Stats.RowCount)
            .ThenByDescending(candidate => candidate.Stats.NonEmptyCellCount)
            .First();

    private static long ScoreRtfCandidate(RtfExtractionCandidate candidate, DetectedReportKind reportKind)
        => reportKind switch
        {
            DetectedReportKind.Form0503152 => ScoreForm0503152Candidate(candidate.Stats),
            DetectedReportKind.Form0531857 => ScoreForm0531857Candidate(candidate.Stats),
            _ => ScoreGenericCandidate(candidate.Stats),
        };

    private static long ScoreForm0503152Candidate(ExportRowsStats stats)
    {
        var noiseRowCount = Math.Max(0, stats.RowCount - stats.Form0503152DataRowCount);
        var lowConfidencePenalty = stats.Form0503152DataRowCount < 5 ? 100_000L : 0L;

        return (stats.Form0503152DataRowCount * 10_000L) +
               (stats.BudgetCodeRowCount * 500L) +
               (stats.ValueRowCount * 250L) +
               stats.NonEmptyCellCount -
               (noiseRowCount * 100L) -
               lowConfidencePenalty;
    }

    private static long ScoreForm0531857Candidate(ExportRowsStats stats)
    {
        var noiseRowCount = Math.Max(0, stats.RowCount - stats.Form0531857DataRowCount - stats.Form0531857SubtotalRowCount);
        var lowConfidencePenalty = stats.Form0531857DataRowCount == 0 && stats.Form0531857SubtotalRowCount == 0
            ? 100_000L
            : 0L;

        return (stats.Form0531857DataRowCount * 10_000L) +
               (stats.Form0531857SubtotalRowCount * 15_000L) +
               (stats.BudgetCodeRowCount * 250L) +
               (stats.ValueRowCount * 100L) +
               stats.NonEmptyCellCount -
               (noiseRowCount * 250L) -
               lowConfidencePenalty;
    }

    private static long ScoreGenericCandidate(ExportRowsStats stats)
        => (stats.BudgetCodeRowCount * 2_000L) +
           (stats.ValueRowCount * 1_000L) +
           (stats.RowCount * 100L) +
           stats.NonEmptyCellCount;

    private static ExportRowsStats AnalyzeExportRows(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var nonEmptyCellCount = rows.Sum(row => row.Count(cell => !string.IsNullOrWhiteSpace(cell)));
        var budgetCodeRowCount = rows.Count(row => row.Any(LooksLikeBudgetCode));
        var valueRowCount = rows.Count(row => row.Any(LooksLikeValueCell));
        var form0503152DataRowCount = rows.Count(row => LooksLikeForm0503152DataRow(CompactRow(row)));
        var form0531857DataRowCount = rows.Count(LooksLikeDataRow);
        var form0531857SubtotalRowCount = rows.Count(LooksLikeSubtotalRow);

        return new ExportRowsStats(
            rows.Count,
            nonEmptyCellCount,
            budgetCodeRowCount,
            valueRowCount,
            form0503152DataRowCount,
            form0531857DataRowCount,
            form0531857SubtotalRowCount);
    }

    private static IReadOnlyList<IReadOnlyList<string>> ParseRawRtfTableRows(string inputPath)
        => ParseRawRtfTableRows(inputPath, RawRtfRowCaptureMode.Intbl);

    private static IReadOnlyList<IReadOnlyList<string>> ParseRawRtfTableRows(
        string inputPath,
        RawRtfRowCaptureMode rowCaptureMode)
    {
        var ansiEncoding = GetRtfAnsiEncoding();
        var rtf = File.ReadAllText(inputPath, ansiEncoding);
        var rows = new List<IReadOnlyList<string>>();
        var currentRow = new List<string>();
        var currentCell = new StringBuilder();
        var inRow = false;
        var unicodeFallbackLength = 1;

        for (var index = 0; index < rtf.Length; index++)
        {
            var current = rtf[index];
            if (current is '{' or '}')
            {
                continue;
            }

            if (current != '\\')
            {
                if (current is not '\r' and not '\n')
                {
                    AppendRawRtfText(currentCell, inRow, current);
                }

                continue;
            }

            if (++index >= rtf.Length)
            {
                break;
            }

            var next = rtf[index];
            if (next is '\\' or '{' or '}')
            {
                AppendRawRtfText(currentCell, inRow, next);
                continue;
            }

            if (next == '~')
            {
                AppendRawRtfText(currentCell, inRow, ' ');
                continue;
            }

            if (next == '\'')
            {
                if (index + 2 < rtf.Length &&
                    TryDecodeRtfHexByte(rtf[index + 1], rtf[index + 2], ansiEncoding, out var decoded))
                {
                    AppendRawRtfText(currentCell, inRow, decoded);
                    index += 2;
                }

                continue;
            }

            if (!char.IsLetter(next))
            {
                continue;
            }

            var wordStart = index;
            while (index < rtf.Length && char.IsLetter(rtf[index]))
            {
                index++;
            }

            var word = rtf[wordStart..index];
            var hasArgument = false;
            var negativeArgument = false;
            var argument = 0;

            if (index < rtf.Length && rtf[index] is '-' or '+')
            {
                negativeArgument = rtf[index] == '-';
                index++;
            }

            while (index < rtf.Length && char.IsDigit(rtf[index]))
            {
                hasArgument = true;
                argument = checked((argument * 10) + (rtf[index] - '0'));
                index++;
            }

            if (negativeArgument)
            {
                argument = -argument;
            }

            var hasDelimiterSpace = index < rtf.Length && rtf[index] == ' ';
            switch (word)
            {
                case "intbl":
                    if (rowCaptureMode == RawRtfRowCaptureMode.Intbl && !inRow)
                    {
                        currentRow.Clear();
                        currentCell.Clear();
                        inRow = true;
                    }

                    break;

                case "trowd":
                    // Some RTF variants emit row properties after the cell content, so row capture
                    // must start on the first in-table paragraph rather than on \trowd alone.
                    if (rowCaptureMode == RawRtfRowCaptureMode.Trowd && !inRow)
                    {
                        currentRow.Clear();
                        currentCell.Clear();
                        inRow = true;
                    }

                    break;

                case "cell":
                    if (inRow)
                    {
                        AddRawRtfCell(currentRow, currentCell);
                    }

                    break;

                case "row":
                    if (inRow)
                    {
                        AddRawRtfRow(rows, currentRow, currentCell);
                        inRow = false;
                    }

                    break;

                case "par":
                case "line":
                case "tab":
                    AppendRawRtfText(currentCell, inRow, ' ');
                    break;

                case "uc" when hasArgument && argument >= 0:
                    unicodeFallbackLength = argument;
                    break;

                case "u" when hasArgument:
                    AppendRawRtfUnicode(currentCell, inRow, argument);
                    SkipRtfUnicodeFallback(rtf, ref index, unicodeFallbackLength);
                    continue;
            }

            if (!hasDelimiterSpace && index < rtf.Length)
            {
                index--;
            }
        }

        if (inRow && (currentCell.Length > 0 || currentRow.Count > 0))
        {
            AddRawRtfRow(rows, currentRow, currentCell);
        }

        return rows;
    }

    private static Encoding GetRtfAnsiEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1251);
    }

    private static void AppendRawRtfText(StringBuilder target, bool inRow, char value)
    {
        if (inRow)
        {
            target.Append(value);
        }
    }

    private static void AppendRawRtfUnicode(StringBuilder target, bool inRow, int value)
    {
        if (!inRow)
        {
            return;
        }

        if (value < 0)
        {
            value += 65536;
        }

        target.Append(char.ConvertFromUtf32(value));
    }

    private static void AddRawRtfCell(ICollection<string> row, StringBuilder currentCell)
    {
        row.Add(NormalizeCellText(currentCell.ToString()));
        currentCell.Clear();
    }

    private static void AddRawRtfRow(ICollection<IReadOnlyList<string>> rows, List<string> currentRow, StringBuilder currentCell)
    {
        if (currentCell.Length > 0)
        {
            AddRawRtfCell(currentRow, currentCell);
        }

        if (currentRow.Any(cell => !string.IsNullOrWhiteSpace(cell)))
        {
            rows.Add(currentRow.ToArray());
        }

        currentRow.Clear();
        currentCell.Clear();
    }

    private static bool TryDecodeRtfHexByte(char high, char low, Encoding encoding, out char decoded)
    {
        decoded = default;
        var highNibble = HexToNumber(high);
        var lowNibble = HexToNumber(low);
        if (highNibble < 0 || lowNibble < 0)
        {
            return false;
        }

        var bytes = new[] { (byte)((highNibble << 4) + lowNibble) };
        var text = encoding.GetString(bytes);
        if (text.Length != 1)
        {
            return false;
        }

        decoded = text[0];
        return true;
    }

    private static int HexToNumber(char value)
        => value switch
        {
            >= '0' and <= '9' => value - '0',
            >= 'a' and <= 'f' => value - 'a' + 10,
            >= 'A' and <= 'F' => value - 'A' + 10,
            _ => -1,
        };

    private static void SkipRtfUnicodeFallback(string rtf, ref int index, int fallbackLength)
    {
        for (var skipped = 0; skipped < fallbackLength && index < rtf.Length; skipped++)
        {
            if (rtf[index] == '\\' &&
                index + 3 < rtf.Length &&
                rtf[index + 1] == '\'' &&
                HexToNumber(rtf[index + 2]) >= 0 &&
                HexToNumber(rtf[index + 3]) >= 0)
            {
                index += 3;
                continue;
            }

            if (rtf[index] is '{' or '}')
            {
                break;
            }
        }
    }

    private static bool LooksLikeForm0503152(IReadOnlyList<IReadOnlyList<string>> rows)
        => rows.Any(row => row.Any(cell => cell.Contains("0503152", StringComparison.OrdinalIgnoreCase)));

    private static bool LooksLikeForm0531857(IReadOnlyList<IReadOnlyList<string>> rows)
        => rows.Any(row =>
        {
            var rowText = NormalizeCellText(string.Join(' ', row));
            return ContainsAnyIgnoreCase(
                rowText,
                "1.Доходы",
                "1. Доходы",
                "2.Расходы",
                "2. Расходы",
                "Итого по коду БК",
                "РС‚РѕРіРѕ РїРѕ РєРѕРґСѓ Р‘Рљ");
        });

    private static IReadOnlyList<IReadOnlyList<string>> ExtractForm0503152Rows(IEnumerable<IReadOnlyList<string>> rows)
        => NormalizeExportRows(rows.Where(row => LooksLikeForm0503152DataRow(CompactRow(row))));

    private static IReadOnlyList<IReadOnlyList<string>> ExtractGenericTableRows(IEnumerable<IReadOnlyList<IReadOnlyList<string>>> tables)
        => tables
            .Select(NormalizeGenericTableRows)
            .Where(rows => rows.Count > 0)
            .SelectMany(rows => rows)
            .ToArray();

    private static IReadOnlyList<IReadOnlyList<string>> NormalizeGenericTableRows(IReadOnlyList<IReadOnlyList<string>> rows)
        => NormalizeExportRows(rows);

    private static IReadOnlyList<IReadOnlyList<string>> NormalizeExportRows(IEnumerable<IReadOnlyList<string>> rows)
    {
        var meaningfulRows = rows
            .Where(HasMeaningfulCells)
            .ToList();

        if (meaningfulRows.Count == 0)
        {
            return [];
        }

        var firstUsedColumn = meaningfulRows
            .Select(FindFirstNonEmptyColumn)
            .Where(index => index >= 0)
            .DefaultIfEmpty(0)
            .Min();

        var lastUsedColumn = meaningfulRows
            .Select(FindLastNonEmptyColumn)
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Max();

        if (lastUsedColumn < firstUsedColumn)
        {
            return [];
        }

        return meaningfulRows
            .Select(row => SliceRow(row, firstUsedColumn, lastUsedColumn))
            .ToArray();
    }

    private static bool HasMeaningfulCells(IReadOnlyList<string> row)
        => row.Any(cell => !string.IsNullOrWhiteSpace(cell));

    private static bool LooksLikeForm0503152DataRow(IReadOnlyList<string> compactRow)
    {
        if (compactRow.Count < 3)
        {
            return false;
        }

        return (LooksLikeForm0503152LineCode(compactRow[1]) || LooksLikeBudgetCode(compactRow[1])) &&
               compactRow.Skip(2).Any(LooksLikeNumericValueCell);
    }

    private static bool LooksLikeNumericValueCell(string value)
        => value.Any(char.IsDigit) && LooksLikeValueCell(value);

    private static bool LooksLikeForm0503152LineCode(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 3 && trimmed.All(char.IsDigit);
    }

    private static IReadOnlyList<string> CompactRow(IReadOnlyList<string> row)
        => row.Where(cell => !string.IsNullOrWhiteSpace(cell)).ToArray();

    private static IReadOnlyList<string> SliceRow(IReadOnlyList<string> row, int firstColumn, int lastColumn)
    {
        if (lastColumn < firstColumn)
        {
            return [];
        }

        var length = lastColumn - firstColumn + 1;
        var normalized = new string[length];
        for (var index = 0; index < length; index++)
        {
            var sourceIndex = firstColumn + index;
            normalized[index] = sourceIndex < row.Count ? row[sourceIndex] : string.Empty;
        }

        return normalized;
    }

    private static ParsedTopLevelTable ParseTopLevelTable(IElement tableElement)
    {
        var tableData = BuildTableData(tableElement);
        var liveRows = ExtractLiveRows(tableData);
        var normalizedText = NormalizeCellText(tableElement.TextContent);

        return new ParsedTopLevelTable(
            DetectSectionTitle(normalizedText),
            normalizedText,
            liveRows,
            tableData.Rows);
    }

    private static void AppendSectionRows(
        ICollection<IReadOnlyList<string>> targetRows,
        IReadOnlyList<ParsedTopLevelTable> tables,
        TableSectionTitle sectionTitle)
    {
        var titleIndex = -1;
        for (var index = 0; index < tables.Count; index++)
        {
            if (tables[index].Title == sectionTitle)
            {
                titleIndex = index;
                break;
            }
        }

        if (titleIndex < 0)
        {
            return;
        }

        for (var index = titleIndex + 1; index < tables.Count; index++)
        {
            var table = tables[index];
            if (table.Title != TableSectionTitle.None)
            {
                break;
            }

            if (table.LiveRows.Count == 0)
            {
                continue;
            }

            foreach (var row in table.LiveRows)
            {
                targetRows.Add(row);
            }

            return;
        }
    }

    private static TableSectionTitle DetectSectionTitle(string normalizedText)
    {
        if (normalizedText.Contains("Источники финансирования дефицита бюджета", StringComparison.OrdinalIgnoreCase))
        {
            return TableSectionTitle.Sources;
        }

        if (normalizedText.Contains("1.Доходы", StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Contains("1. Доходы", StringComparison.OrdinalIgnoreCase))
        {
            return TableSectionTitle.Income;
        }

        if (normalizedText.Contains("2.Расходы", StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Contains("2. Расходы", StringComparison.OrdinalIgnoreCase))
        {
            return TableSectionTitle.Expense;
        }

        return TableSectionTitle.None;
    }

    private static bool IsTopLevelTable(IElement table)
        => table.ParentElement?.Closest("table") is null;

    private static TableData BuildTableData(IElement tableElement)
    {
        var rowElements = tableElement.QuerySelectorAll("tr")
            .Where(row => ReferenceEquals(row.Closest("table"), tableElement))
            .ToList();

        var grid = new List<List<string?>>(rowElements.Count);
        for (var rowIndex = 0; rowIndex < rowElements.Count; rowIndex++)
        {
            EnsureRow(grid, rowIndex);
            var row = rowElements[rowIndex];
            var columnIndex = 0;

            foreach (var cell in row.Children.Where(IsTableCell))
            {
                while (IsOccupied(grid, rowIndex, columnIndex))
                {
                    columnIndex++;
                }

                var text = NormalizeCellText(cell.TextContent);
                var rowSpan = ParseSpan(cell.GetAttribute("rowspan"));
                var colSpan = ParseSpan(cell.GetAttribute("colspan"));

                for (var spanRow = rowIndex; spanRow < rowIndex + rowSpan; spanRow++)
                {
                    EnsureRow(grid, spanRow);
                    EnsureColumns(grid[spanRow], columnIndex + colSpan);

                    for (var spanCol = columnIndex; spanCol < columnIndex + colSpan; spanCol++)
                    {
                        grid[spanRow][spanCol] = spanRow == rowIndex && spanCol == columnIndex
                            ? text
                            : string.Empty;
                    }
                }

                columnIndex += colSpan;
            }
        }

        return new TableData(NormalizeGrid(grid));
    }

    private static IReadOnlyList<IReadOnlyList<string>> ExtractLiveRows(TableData table)
    {
        var startIndex = FindFirstDataRowIndex(table.Rows);
        if (startIndex < 0)
        {
            return [];
        }

        var selectedRows = table.Rows
            .Skip(startIndex)
            .Where(LooksLikeExportRow)
            .ToList();

        if (selectedRows.Count == 0)
        {
            return [];
        }

        return NormalizeExportRows(selectedRows);
    }

    private static int FindFirstDataRowIndex(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            if (LooksLikeDataRow(rows[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool LooksLikeDataRow(IReadOnlyList<string> row)
    {
        var nonEmptyCells = row.Where(cell => !string.IsNullOrWhiteSpace(cell)).ToArray();
        if (nonEmptyCells.Length < 2)
        {
            return false;
        }

        return LooksLikeBudgetCode(nonEmptyCells[0]) &&
               nonEmptyCells.Skip(1).Any(LooksLikeValueCell);
    }

    private static bool LooksLikeExportRow(IReadOnlyList<string> row)
        => LooksLikeDataRow(row) || LooksLikeSubtotalRow(row);

    private static bool LooksLikeSubtotalRow(IReadOnlyList<string> row)
    {
        var nonEmptyCells = row.Where(cell => !string.IsNullOrWhiteSpace(cell)).ToArray();
        if (nonEmptyCells.Length < 2)
        {
            return false;
        }

        if (!nonEmptyCells[0].StartsWith("Итого по коду БК", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return nonEmptyCells.Skip(1).Any(LooksLikeValueCell);
    }

    private static bool LooksLikeBudgetCode(string value)
        => value.Count(char.IsDigit) >= 8;

    private static bool LooksLikeValueCell(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (string.Equals(trimmed, "X", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!trimmed.Any(char.IsDigit))
        {
            return false;
        }

        return trimmed.All(ch =>
            char.IsDigit(ch) ||
            char.IsWhiteSpace(ch) ||
            ch is ',' or '.' or '-' or '+' or '(' or ')' or '/');
    }

    private static bool ContainsAnyIgnoreCase(string value, params string[] patterns)
        => patterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));

    private static int FindFirstNonEmptyColumn(IReadOnlyList<string> row)
    {
        for (var index = 0; index < row.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(row[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindLastNonEmptyColumn(IReadOnlyList<string> row)
    {
        for (var index = row.Count - 1; index >= 0; index--)
        {
            if (!string.IsNullOrWhiteSpace(row[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool AreRowsEqual(
        IReadOnlyList<IReadOnlyList<string>> left,
        IReadOnlyList<IReadOnlyList<string>> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var rowIndex = 0; rowIndex < left.Count; rowIndex++)
        {
            var leftRow = left[rowIndex];
            var rightRow = right[rowIndex];
            if (leftRow.Count != rightRow.Count)
            {
                return false;
            }

            for (var columnIndex = 0; columnIndex < leftRow.Count; columnIndex++)
            {
                if (!string.Equals(leftRow[columnIndex], rightRow[columnIndex], StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void WriteRows(
        string outputPath,
        IReadOnlyList<IReadOnlyList<string>> rows,
        string delimiter,
        TextFileEncodingKind outputEncoding)
    {
        var tempPath = Path.Combine(
            Path.GetDirectoryName(outputPath)!,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var writer = new StreamWriter(tempPath, false, outputEncoding.GetEncoding()))
            {
                writer.NewLine = OutputNewLine;

                foreach (var row in rows)
                {
                    if (row.Count == 0)
                    {
                        continue;
                    }

                    for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
                    {
                        if (columnIndex > 0)
                        {
                            writer.Write(delimiter);
                        }

                        writer.Write(row[columnIndex]);
                    }

                    writer.WriteLine();
                }
            }

            File.Move(tempPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Ignore temp cleanup failures.
                }
            }
        }
    }

    private static bool IsTableCell(IElement element)
        => string.Equals(element.LocalName, "td", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.LocalName, "th", StringComparison.OrdinalIgnoreCase);

    private static int ParseSpan(string? rawValue)
        => int.TryParse(rawValue, out var parsed) && parsed > 0 ? parsed : 1;

    private static bool IsOccupied(List<List<string?>> grid, int rowIndex, int columnIndex)
        => rowIndex < grid.Count &&
           columnIndex < grid[rowIndex].Count &&
           grid[rowIndex][columnIndex] is not null;

    private static void EnsureRow(List<List<string?>> grid, int rowIndex)
    {
        while (grid.Count <= rowIndex)
        {
            grid.Add([]);
        }
    }

    private static void EnsureColumns(List<string?> row, int requiredColumnCount)
    {
        while (row.Count < requiredColumnCount)
        {
            row.Add(null);
        }
    }

    private static IReadOnlyList<IReadOnlyList<string>> NormalizeGrid(List<List<string?>> grid)
    {
        var width = grid.Count == 0 ? 0 : grid.Max(row => row.Count);
        var normalized = new List<IReadOnlyList<string>>(grid.Count);

        foreach (var row in grid)
        {
            EnsureColumns(row, width);
            normalized.Add(row.Select(cell => cell ?? string.Empty).ToArray());
        }

        return normalized;
    }

    private static string NormalizeCellText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var current in value)
        {
            if (char.GetUnicodeCategory(current) == UnicodeCategory.Format ||
                char.IsControl(current))
            {
                continue;
            }

            var normalized = current == '\u00A0' ? ' ' : current;
            if (char.IsWhiteSpace(normalized))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(normalized);
        }

        return builder.ToString().Trim();
    }
}

internal sealed record ConversionResult(string InputPath, string OutputPath, int RowCount, int ColumnCount);

internal sealed record TableData(IReadOnlyList<IReadOnlyList<string>> Rows)
{
    public int RowCount => Rows.Count;

    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(row => row.Count);

    public int NonEmptyCellCount => Rows.Sum(row => row.Count(cell => !string.IsNullOrWhiteSpace(cell)));

    public int MeaningfulRowCount => Rows.Count(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell)));
}

internal sealed record ParsedTopLevelTable(
    TableSectionTitle Title,
    string NormalizedText,
    IReadOnlyList<IReadOnlyList<string>> LiveRows,
    IReadOnlyList<IReadOnlyList<string>> Rows);

internal sealed record RtfExtractionCandidate(
    string StrategyName,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    ExportRowsStats Stats);

internal sealed record ExportRowsStats(
    int RowCount,
    int NonEmptyCellCount,
    int BudgetCodeRowCount,
    int ValueRowCount,
    int Form0503152DataRowCount,
    int Form0531857DataRowCount,
    int Form0531857SubtotalRowCount);

internal enum TableSectionTitle
{
    None,
    Income,
    Expense,
    Sources,
}

internal enum DetectedReportKind
{
    Unknown,
    Form0503152,
    Form0531857,
}

internal enum RawRtfRowCaptureMode
{
    Intbl,
    Trowd,
}

internal sealed class NoTablesFoundException(string message) : Exception(message);
