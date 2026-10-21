using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;

namespace DataMart.Application.FunctionalTests.Fixtures;

/// <summary>
/// Test data sets ("jeux de tests") for the Excel import integration tests.
/// Each scenario is defined as plain rows of strings and rendered to a real .xlsx
/// at test time (and on first run, also materialised to the source Fixtures
/// folder so the deliverable .xlsx files exist on disk for the live demo).
/// </summary>
public static class ClientAccountFixtures
{
    private const string SheetName = "Clients";

    private static readonly string[] Headers = { "AccountNumber", "ClientName", "Balance", "OpenedDate" };

    public enum Scenario
    {
        Ok,
        MissingAccountNumber,
        BadTypes,
        PartialErrors,
    }

    /// <summary>Resolves the source Fixtures folder from the test assembly's output path.</summary>
    public static string GetFixturesFolder()
    {
        // bin/Debug/{TFM}/ -> ../ -> ../ -> ../ = project source folder
        var asmDir = Path.GetDirectoryName(typeof(ClientAccountFixtures).Assembly.Location)!;
        return Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "Fixtures"));
    }

    /// <summary>Returns a real .xlsx byte array for the given scenario (single sheet, in-memory).</summary>
    public static byte[] CreateXlsx(Scenario scenario)
    {
        using var ms = new MemoryStream();
        WriteTo(scenario, ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Generates a programmatically large but fully-valid file (all rows importable).
    /// Used where a test needs the import's row pass to take real time (e.g. proving
    /// same-table FIFO ordering through StartedAt/CompletedAt, or catching the
    /// Validating phase mid-flight).
    /// </summary>
    public static byte[] CreateLargeXlsx(int rowCount)
    {
        using var ms = new MemoryStream();
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);

        for (int c = 0; c < Headers.Length; c++)
            ws.Cell(1, c + 1).Value = Headers[c];

        for (int r = 0; r < rowCount; r++)
        {
            var row = r + 2;
            ws.Cell(row, 1).Value = $"A-{r:D6}";
            ws.Cell(row, 2).Value = $"Client {r}";
            ws.Cell(row, 3).Value = (1000 + r).ToString();
            ws.Cell(row, 4).Value = $"2024-0{1 + (r % 9)}-1{r % 9}";
        }

        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>Writes the four scenario .xlsx files to the Fixtures folder if they are not already present.</summary>
    public static void EnsureOnDisk()
    {
        var folder = GetFixturesFolder();
        Directory.CreateDirectory(folder);
        foreach (var scenario in Enum.GetValues<Scenario>())
        {
            var path = Path.Combine(folder, $"client_{NameOf(scenario)}.xlsx");
            if (!File.Exists(path))
                File.WriteAllBytes(path, CreateXlsx(scenario));
        }
    }

    private static string NameOf(Scenario s) => s switch
    {
        Scenario.Ok => "ok",
        Scenario.MissingAccountNumber => "missing_account_number",
        Scenario.BadTypes => "bad_types",
        Scenario.PartialErrors => "partial_errors",
        _ => s.ToString().ToLowerInvariant(),
    };

    private static void WriteTo(Scenario scenario, Stream output)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);

        for (int c = 0; c < Headers.Length; c++)
            ws.Cell(1, c + 1).Value = Headers[c];

        var rows = GetRows(scenario);
        for (int r = 0; r < rows.Count; r++)
        {
            for (int c = 0; c < Headers.Length; c++)
            {
                var v = rows[r][c];
                if (v is null || (v is string s && s.Length == 0))
                    ws.Cell(r + 2, c + 1).Value = string.Empty;
                else
                    ws.Cell(r + 2, c + 1).Value = v;
            }
        }

        wb.SaveAs(output);
    }

    /// <summary>Each inner list is one row of string? cells (matches the column order in <see cref="Headers"/>).</summary>
    private static List<List<string?>> GetRows(Scenario scenario) => scenario switch
    {
        Scenario.Ok => new()
        {
            new() { "A-001", "Alice Johnson",  "1500.50", "2024-01-15" },
            new() { "A-002", "Bob Smith",     "2750.00", "2024-02-20" },
            new() { "A-003", "Carol Davis",   "   99.99", "2024-03-05" },
            new() { "A-004", "Dan Brown",     "10000.0", "2024-04-12" },
            new() { "A-005", "Eve Wilson",    "  500.00", "2024-05-30" },
        },
        Scenario.MissingAccountNumber => new()
        {
            new() { "A-010", "Frank Wright",  "1000.00", "2024-01-10" },
            new() { "",     "Grace Hopper",  "2000.00", "2024-02-15" }, // required missing
            new() { "A-011", "Heidi Clarke",  "3000.00", "2024-03-20" },
            new() { "",     "Ivan Petrov",   "4000.00", "2024-04-25" }, // required missing
            new() { "A-012", "Judy Reeves",   "5000.00", "2024-05-30" },
            new() { "",     "Kim Young",     "6000.00", "2024-06-10" }, // required missing
            new() { "A-013", "Liam Foster",   "7000.00", "2024-07-15" },
        },
        Scenario.BadTypes => new()
        {
            new() { "A-020", "Mia Tanaka",    "1500.00", "2024-01-20" },
            new() { "A-021", "Noah Becker",   "abc",     "2024-02-25" },          // bad decimal
            new() { "A-022", "Olivia Stein",  "3000.00", "2024-03-30" },
            new() { "A-023", "Paolo Ricci",   "4000.00", "not-a-date" },          // bad date
            new() { "A-024", "Quinn Adler",   "not-a-number", "2024-05-10" },    // bad decimal
            new() { "A-025", "Rita Hahn",     "6000.00", "2024-06-20" },
        },
        Scenario.PartialErrors => new()
        {
            new() { "A-030", "Sam Cole",      "1000.00", "2024-01-05" },
            new() { "A-031", "Tara Webb",     "2000.00", "2024-02-10" },
            new() { "A-032", "Umar Said",     "3000.00", "2024-03-15" },
            new() { "A-033", "Vera Olsen",    "4000.00", "2024-04-20" },
            new() { "",     "Walt Pham",     "5000.00", "2024-05-25" },          // required missing
            new() { "A-034", "Xena Liu",      "6000.00", "2024-06-30" },
            new() { "A-035", "Yara Costa",    "7000.00", "2024-07-05" },
            new() { "A-036", "Zane Miller",   "8000.00", "2024-08-10" },
            new() { "",     "Ada Lovelace",  "9000.00", "2024-09-15" },          // required missing
            new() { "A-037", "Boris Pasternak","oops",  "2024-10-20" },          // bad decimal
        },
        _ => new(),
    };
}
