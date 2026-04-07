using System.Globalization;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using RtfPipe;

namespace RtfTableExporter;

internal static class RtfTableConverter
{
    private const string OutputNewLine = "\r\n";

    public static ConversionResult Convert(string inputPath, string outputPath, string delimiter, TextFileEncodingKind outputEncoding)
    {
        var normalizedInputPath = Path.GetFullPath(inputPath);
        var normalizedOutputPath = Path.GetFullPath(outputPath);

        if (!File.Exists(normalizedInputPath))
        {
            throw new FileNotFoundException("Input file was not found.", normalizedInputPath);
        }

        if (!string.Equals(Path.GetExtension(normalizedInputPath), ".rtf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only .rtf files are supported.");
        }

        var liveRows = ExtractRowsForExport(normalizedInputPath);
        if (liveRows.Count == 0)
        {
            throw new NoTablesFoundException("The input RTF file does not contain exportable data rows.");
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

        if (topLevelTables.Count == 0)
        {
            throw new NoTablesFoundException("No top-level tables were found in the input RTF file.");
        }

        var sectionRows = new List<IReadOnlyList<string>>();
        AppendSectionRows(sectionRows, topLevelTables, TableSectionTitle.Income);
        AppendSectionRows(sectionRows, topLevelTables, TableSectionTitle.Expense);

        if (sectionRows.Count > 0)
        {
            return sectionRows.ToArray();
        }

        return topLevelTables
            .Where(table => table.LiveRows.Count > 0)
            .Take(2)
            .SelectMany(table => table.LiveRows)
            .ToArray();
    }

    private static ParsedTopLevelTable ParseTopLevelTable(IElement tableElement)
    {
        var tableData = BuildTableData(tableElement);
        var liveRows = ExtractLiveRows(tableData);
        var normalizedText = NormalizeCellText(tableElement.TextContent);

        return new ParsedTopLevelTable(
            DetectSectionTitle(normalizedText),
            normalizedText,
            liveRows);
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

        var lastUsedColumn = selectedRows.Max(FindLastNonEmptyColumn);
        return selectedRows
            .Select(row => row.Take(lastUsedColumn + 1).ToArray())
            .ToArray();
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
                    var lastColumn = FindLastNonEmptyColumn(row);
                    if (lastColumn < 0)
                    {
                        continue;
                    }

                    for (var columnIndex = 0; columnIndex <= lastColumn; columnIndex++)
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
    IReadOnlyList<IReadOnlyList<string>> LiveRows);

internal enum TableSectionTitle
{
    None,
    Income,
    Expense,
    Sources,
}

internal sealed class NoTablesFoundException(string message) : Exception(message);
