// Standalone helper that materialises the Excel-import test data sets (jeux de tests).
// Generates real .xlsx files (ClosedXML) plus matching .csv fallbacks. Not part of the
// solution; run from the repo root with:
//
//     dotnet run --project tests/FixturesGenerator
//
// Output:
//   tests/Application.FunctionalTests/Fixtures/   small files mirroring the integration
//                                                  test scenarios (kept byte-identical to
//                                                  ClientAccountFixtures.GetRows so the
//                                                  tests still pass).
//   docs/sample-data/                             larger demo workbooks for live wizard
//                                                  walkthroughs, error-rejection showcase,
//                                                  multi-sheet selection, etc.

using System.Globalization;
using ClosedXML.Excel;

namespace DataMart.Tests.FixturesGenerator;

internal static class Program
{
    // ----- Schema mirrors ------------------------------------------------
    // Mirrors src/Domain/Entities/ClientAccount.cs.
    private static readonly string[] ClientHeaders =
    {
        "AccountNumber", "ClientName", "Balance", "InterestRate", "TransactionCount",
        "OpenedDate", "LastActivityDate", "IsActive", "Notes",
    };

    // Mirrors src/Domain/Entities/CustomerTransaction.cs.
    private static readonly string[] TransactionHeaders =
    {
        "TransactionID", "CustomerID", "CustomerDOB", "CustGender", "CustLocation",
        "CustAccountBalance", "TransactionDate", "TransactionTime", "TransactionAmount",
    };

    // Same column set the integration test mapping uses (AccountNumber/ClientName/Balance/OpenedDate).
    private static readonly string[] ClientMinimalHeaders =
        { "AccountNumber", "ClientName", "Balance", "OpenedDate" };

    private const string ClientSheet = "Clients";
    private const string TransactionSheet = "Transactions";

    // ----- Themed name pools ---------------------------------------------
    private static readonly string[] FirstNames =
    {
        "Alice", "Bob", "Carol", "Dan", "Eve", "Frank", "Grace", "Heidi", "Ivan", "Judy",
        "Kim", "Liam", "Mia", "Noah", "Olivia", "Paolo", "Quinn", "Rita", "Sam", "Tara",
        "Umar", "Vera", "Walt", "Xena", "Yara", "Zane",
    };

    private static readonly string[] LastNames =
    {
        "Johnson", "Smith", "Davis", "Brown", "Wilson", "Wright", "Hopper", "Clarke",
        "Petrov", "Reeves", "Young", "Foster", "Tanaka", "Becker", "Stein", "Ricci",
        "Adler", "Hahn", "Cole", "Webb", "Said", "Olsen", "Pham", "Liu", "Costa", "Miller",
    };

    private static readonly string[] Cities =
    {
        "Paris", "Lyon", "Marseille", "Bordeaux", "Lille", "Nantes", "Toulouse",
        "Nice", "Strasbourg", "Montpellier", "Rennes", "Reims",
    };

    private static readonly string?[] Notes =
    {
        "VIP", "Watchlist", "New", "Seasonal", "Corporate", "Premier", null, null,
    };

    private static readonly string[] DateFormats = { "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy" };
    private static readonly string[] DateSamples =
    {
        "2024-01-15", "2024-02-20", "2024-03-05", "2024-04-12", "2024-05-30",
        "2024-06-14", "2024-07-22", "2024-08-08", "2024-09-19", "2024-10-31",
    };

    // -----------------------------------------------------------------------
    // Entry point
    // -----------------------------------------------------------------------
    private static int Main()
    {
        var fixturesDir = Path.GetFullPath(Path.Combine("tests", "Application.FunctionalTests", "Fixtures"));
        var docsDir = Path.GetFullPath(Path.Combine("docs", "sample-data"));
        Directory.CreateDirectory(fixturesDir);
        Directory.CreateDirectory(docsDir);

        // 1. The four integration-test scenario files. Row data is byte-identical to
        //    ClientAccountFixtures.GetRows so the existing tests keep passing.
        WriteClientScenario(fixturesDir, Scenario.Ok, ClientRows.Ok);
        WriteClientScenario(fixturesDir, Scenario.MissingAccountNumber, ClientRows.MissingAccountNumber);
        WriteClientScenario(fixturesDir, Scenario.BadTypes, ClientRows.BadTypes);
        WriteClientScenario(fixturesDir, Scenario.PartialErrors, ClientRows.PartialErrors);

        // 2. Demo workbooks for the live wizard walkthrough.
        WriteClientDemo(docsDir, count: 250,  path: "client_demo_250.xlsx");
        WriteClientDemo(docsDir, count: 2000, path: "client_demo_2000.xlsx");
        WriteClientMultiformat(docsDir, count: 150);
        WriteClientMultiSheet(docsDir, countPerSheet: 75);
        WriteClientRejectedShowcase(docsDir, count: 50);
        WriteTransactionsDemo(docsDir, count: 500);
        WriteTransactionsMessy(docsDir, count: 200);

        // CSV fallbacks for the two primary demo files (one ClientAccount, one Transaction).
        WriteClientDemoCsv(docsDir, count: 250,  path: "client_demo_250.csv");
        WriteTransactionsCsv(docsDir, count: 500,  path: "customer_transactions_500.csv");

        Console.WriteLine($"Wrote fixtures to:");
        foreach (var dir in new[] { fixturesDir, docsDir })
        {
            Console.WriteLine($"  {dir}");
            foreach (var f in Directory.EnumerateFiles(dir).OrderBy(p => p))
                Console.WriteLine($"    - {Path.GetFileName(f),-40} {new FileInfo(f).Length,10:N0} bytes");
        }

        return 0;
    }

    // -----------------------------------------------------------------------
    // Integration-test scenarios (unchanged)
    // -----------------------------------------------------------------------
    private enum Scenario
    {
        Ok,
        MissingAccountNumber,
        BadTypes,
        PartialErrors,
    }

    private static string NameOf(Scenario s) => s switch
    {
        Scenario.Ok => "ok",
        Scenario.MissingAccountNumber => "missing_account_number",
        Scenario.BadTypes => "bad_types",
        Scenario.PartialErrors => "partial_errors",
        _ => s.ToString().ToLowerInvariant(),
    };

    private static void WriteClientScenario(string dir, Scenario scenario, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        var baseName = $"client_{NameOf(scenario)}";
        WriteXlsx(Path.Combine(dir, $"{baseName}.xlsx"), ClientSheet, ClientMinimalHeaders, rows);
        WriteCsv(Path.Combine(dir, $"{baseName}.csv"), ClientMinimalHeaders, rows);
    }

    // -----------------------------------------------------------------------
    // Demo: 250 / 2000-row ClientAccount workbook (full schema, format variety)
    // -----------------------------------------------------------------------
    private static void WriteClientDemo(string dir, int count, string path)
    {
        var rows = GenerateClientRows(count, seed: 42, corruptionRate: 0.0);
        WriteXlsx(Path.Combine(dir, path), ClientSheet, ClientHeaders, rows);
    }

    private static void WriteClientDemoCsv(string dir, int count, string path)
    {
        var rows = GenerateClientRows(count, seed: 42, corruptionRate: 0.0);
        // CSV in the simplest shape: ISO dates, period decimals, no currency symbols, bool as true/false.
        var simpleHeaders = new[] { "AccountNumber", "ClientName", "Balance", "InterestRate", "TransactionCount", "OpenedDate", "LastActivityDate", "IsActive", "Notes" };
        var simplified = rows.Select(r => new[]
        {
            r[0], r[1],
            r[2]!.Replace("$", "").Replace("€", "").Replace("£", "").Trim(),
            r[3]!.Replace(",", "."),
            r[4],
            r[5],
            r[6],
            r[7]!.ToLowerInvariant() switch { "yes" => "true", "no" => "false", _ => r[7]!.ToLowerInvariant() },
            r[8] ?? string.Empty,
        }).ToList();
        WriteCsv(Path.Combine(dir, path), simpleHeaders, simplified);
    }

    // -----------------------------------------------------------------------
    // Demo: multiformat (every date/decimal/boolean flavour mixed per row)
    // -----------------------------------------------------------------------
    private static void WriteClientMultiformat(string dir, int count)
    {
        var rows = GenerateClientRows(count, seed: 1337, corruptionRate: 0.0, multiformat: true);
        WriteXlsx(Path.Combine(dir, "client_multiformat_150.xlsx"), ClientSheet, ClientHeaders, rows);
    }

    // -----------------------------------------------------------------------
    // Demo: multi-sheet workbook (tests sheet selection)
    // -----------------------------------------------------------------------
    private static void WriteClientMultiSheet(string dir, int countPerSheet)
    {
        var path = Path.Combine(dir, "client_multi_sheet.xlsx");
        using var wb = new XLWorkbook();
        foreach (var (sheet, prefix) in new[] { ("Active", "ACT"), ("Closed", "CLS") })
        {
            var ws = wb.Worksheets.Add(sheet);
            for (int c = 0; c < ClientHeaders.Length; c++)
                ws.Cell(1, c + 1).Value = ClientHeaders[c];
            var rows = GenerateClientRows(countPerSheet, seed: sheet == "Active" ? 11 : 22,
                corruptionRate: 0.0, accountPrefix: prefix, openedYearRange: sheet == "Active" ? (2022, 2024) : (2018, 2021));
            for (int r = 0; r < rows.Count; r++)
                for (int c = 0; c < ClientHeaders.Length; c++)
                    ws.Cell(r + 2, c + 1).Value = rows[r][c] ?? string.Empty;
            ws.Columns().AdjustToContents();
        }
        wb.SaveAs(path);
    }

    // -----------------------------------------------------------------------
    // Demo: rejected-rows showcase (deliberate errors so the report has content)
    // -----------------------------------------------------------------------
    private static void WriteClientRejectedShowcase(string dir, int count)
    {
        // Higher corruption rate + dedicated error rows: ~30% rejected.
        var rows = new List<string?[]>();
        var rng = new Random(99);

        // 5 deliberately-empty AccountNumber rows.
        for (int i = 0; i < 5; i++)
            rows.Add(BuildClientRow(rng, i + 1, "ERR", forceMissingAccount: true, forceBadDecimal: false, forceBadDate: false, forceBadBool: false));

        // 5 bad-decimal rows.
        for (int i = 0; i < 5; i++)
            rows.Add(BuildClientRow(rng, 100 + i, "ERR", forceMissingAccount: false, forceBadDecimal: true, forceBadDate: false, forceBadBool: false));

        // 5 bad-date rows.
        for (int i = 0; i < 5; i++)
            rows.Add(BuildClientRow(rng, 200 + i, "ERR", forceMissingAccount: false, forceBadDecimal: false, forceBadDate: true, forceBadBool: false));

        // 5 bad-boolean rows.
        for (int i = 0; i < 5; i++)
            rows.Add(BuildClientRow(rng, 300 + i, "ERR", forceMissingAccount: false, forceBadDecimal: false, forceBadDate: false, forceBadBool: true));

        // Fill the rest with clean rows.
        var clean = GenerateClientRows(count - rows.Count, seed: 1, corruptionRate: 0.0);
        rows.AddRange(clean);

        WriteXlsx(Path.Combine(dir, "client_rejected_demo.xlsx"), ClientSheet, ClientHeaders, rows);
    }

    // -----------------------------------------------------------------------
    // Demo: 500-row CustomerTransaction workbook
    // -----------------------------------------------------------------------
    private static void WriteTransactionsDemo(string dir, int count)
    {
        var rows = GenerateTransactionRows(count, seed: 7, corruptionRate: 0.0);
        WriteXlsx(Path.Combine(dir, "customer_transactions_500.xlsx"), TransactionSheet, TransactionHeaders, rows);
    }

    private static void WriteTransactionsCsv(string dir, int count, string path)
    {
        var rows = GenerateTransactionRows(count, seed: 7, corruptionRate: 0.0);
        WriteCsv(Path.Combine(dir, path), TransactionHeaders, rows);
    }

    // -----------------------------------------------------------------------
    // Demo: 200-row messy transactions (mixed EU/US dates, blanks, bad values)
    // -----------------------------------------------------------------------
    private static void WriteTransactionsMessy(string dir, int count)
    {
        var rows = new List<string?[]>();
        var rng = new Random(13);

        for (int i = 1; i <= count; i++)
        {
            var dob = new DateTime(1965 + rng.Next(35), rng.Next(1, 13), rng.Next(1, 28));
            var txDate = new DateTime(2024, rng.Next(1, 13), rng.Next(1, 29));
            var gender = rng.Next(2) == 0 ? "M" : "F";
            var balance = (rng.NextDouble() * 20_000 - 10_000);
            var amount  = (rng.NextDouble() * 4_000 - 2_000);

            // 1 in 8 rows: blank TransactionID (would only matter if it were required; it isn't,
            // but the row still exercises the optional-field handling).
            var txId = rng.Next(8) == 0 ? null : $"TX-{1000 + i:D4}";

            // 1 in 12 rows: balance written with European decimal separator.
            var balanceStr = rng.Next(12) == 0
                ? balance.ToString("F2", CultureInfo.GetCultureInfo("fr-FR"))
                : balance.ToString("F2", CultureInfo.InvariantCulture);

            // 1 in 10 rows: EU date for TransactionDate.
            var txDateStr = rng.Next(10) == 0
                ? txDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : txDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            // 1 in 15 rows: bad amount.
            var amountStr = rng.Next(15) == 0 ? "n/a" : amount.ToString("F2", CultureInfo.InvariantCulture);

            rows.Add(new string?[]
            {
                txId,
                $"CUST-{rng.Next(100, 999)}",
                dob.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                gender,
                Cities[rng.Next(Cities.Length)],
                balanceStr,
                txDateStr,
                $"{rng.Next(0, 24):D2}:{rng.Next(0, 60):D2}:{rng.Next(0, 60):D2}",
                amountStr,
            });
        }

        WriteXlsx(Path.Combine(dir, "customer_transactions_messy_200.xlsx"), TransactionSheet, TransactionHeaders, rows);
    }

    // -----------------------------------------------------------------------
    // Row generators
    // -----------------------------------------------------------------------
    private static List<string?[]> GenerateClientRows(
        int count,
        int seed,
        double corruptionRate,
        bool multiformat = false,
        string accountPrefix = "DEMO",
        (int from, int to)? openedYearRange = null)
    {
        var rng = new Random(seed);
        var rows = new List<string?[]>(count);

        for (int i = 1; i <= count; i++)
        {
            var row = BuildClientRow(rng, i, accountPrefix,
                forceMissingAccount: false, forceBadDecimal: false, forceBadDate: false, forceBadBool: false,
                multiformat: multiformat,
                openedYearRange: openedYearRange ?? (2020, 2024));

            if (rng.NextDouble() < corruptionRate)
            {
                // Sprinkle one kind of corruption per corrupted row.
                switch (rng.Next(3))
                {
                    case 0: row[0] = ""; break;                       // missing required
                    case 1: row[2] = "not-a-number"; break;           // bad decimal
                    case 2: row[5] = "31/31/2024"; break;             // bad date
                }
            }
            rows.Add(row);
        }
        return rows;
    }

    private static string?[] BuildClientRow(
        Random rng,
        int index,
        string accountPrefix,
        bool forceMissingAccount,
        bool forceBadDecimal,
        bool forceBadDate,
        bool forceBadBool,
        bool multiformat = false,
        (int from, int to)? openedYearRange = null)
    {
        var yearRange = openedYearRange ?? (2020, 2024);
        var balance = (rng.NextDouble() * 50_000 + 100);
        var interest = (rng.NextDouble() * 5);
        var txCount = rng.Next(1, 250);

        // Date selection
        var openedDt = new DateTime(
            rng.Next(yearRange.from, yearRange.to + 1),
            rng.Next(1, 13),
            rng.Next(1, 29));
        var lastActivityDt = openedDt.AddDays(rng.Next(0, 365));

        var opened = multiformat
            ? FormatDate(openedDt, (FormatStyle)(rng.Next(3)))
            : openedDt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var lastActivity = lastActivityDt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Decimal style
        var balanceStr = multiformat
            ? FormatDecimal(balance, (DecimalStyle)(rng.Next(5)))
            : balance.ToString("F2", CultureInfo.InvariantCulture);

        var interestStr = interest.ToString("F3", CultureInfo.InvariantCulture);
        var txCountStr = txCount.ToString(CultureInfo.InvariantCulture);

        // Boolean style
        var isActive = multiformat
            ? FormatBool(rng.Next(2) == 0, (BoolStyle)(rng.Next(4)))
            : (rng.Next(2) == 0 ? "Yes" : "No");

        // Forced errors override
        var accountNumber = forceMissingAccount ? "" : $"{accountPrefix}-{index:D5}";
        if (forceBadDecimal) balanceStr = "abc";
        if (forceBadDate) opened = "not-a-date";
        if (forceBadBool) isActive = "maybe";

        return new string?[]
        {
            accountNumber,
            $"{FirstNames[rng.Next(FirstNames.Length)]} {LastNames[rng.Next(LastNames.Length)]}",
            balanceStr,
            interestStr,
            txCountStr,
            opened,
            lastActivity,
            isActive,
            Notes[rng.Next(Notes.Length)],
        };
    }

    private static List<string?[]> GenerateTransactionRows(int count, int seed, double corruptionRate)
    {
        var rng = new Random(seed);
        var rows = new List<string?[]>(count);
        for (int i = 1; i <= count; i++)
        {
            var dob = new DateTime(1965 + rng.Next(35), rng.Next(1, 13), rng.Next(1, 28));
            var txDate = new DateTime(2024, rng.Next(1, 13), rng.Next(1, 29));
            var balance = (rng.NextDouble() * 20_000 - 10_000);
            var amount  = (rng.NextDouble() * 4_000 - 2_000);

            var row = new string?[]
            {
                $"TX-{1000 + i:D5}",
                $"CUST-{rng.Next(100, 999):D3}",
                dob.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                rng.Next(2) == 0 ? "M" : "F",
                Cities[rng.Next(Cities.Length)],
                balance.ToString("F2", CultureInfo.InvariantCulture),
                txDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                $"{rng.Next(0, 24):D2}:{rng.Next(0, 60):D2}:{rng.Next(0, 60):D2}",
                amount.ToString("F2", CultureInfo.InvariantCulture),
            };

            if (rng.NextDouble() < corruptionRate)
            {
                switch (rng.Next(2))
                {
                    case 0: row[5] = "n/a"; break;
                    case 1: row[8] = "???";  break;
                }
            }
            rows.Add(row);
        }
        return rows;
    }

    // -----------------------------------------------------------------------
    // Format variants
    // -----------------------------------------------------------------------
    private enum FormatStyle { Iso, Eu, Us }
    private enum DecimalStyle { Plain, Comma, Dollar, Euro, Padded }
    private enum BoolStyle { YesNo, TrueFalse, OneZero, OuiNon }

    private static string FormatDate(DateTime d, FormatStyle style) => style switch
    {
        FormatStyle.Iso => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        FormatStyle.Eu  => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        FormatStyle.Us  => d.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
        _ => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    };

    private static string FormatDecimal(double v, DecimalStyle style) => style switch
    {
        DecimalStyle.Plain  => v.ToString("F2", CultureInfo.InvariantCulture),
        DecimalStyle.Comma  => v.ToString("F2", CultureInfo.GetCultureInfo("fr-FR")),
        DecimalStyle.Dollar => "$" + v.ToString("F2", CultureInfo.InvariantCulture),
        DecimalStyle.Euro   => "€" + v.ToString("F2", CultureInfo.GetCultureInfo("fr-FR")),
        DecimalStyle.Padded => "  " + v.ToString("F2", CultureInfo.InvariantCulture),
        _ => v.ToString("F2", CultureInfo.InvariantCulture),
    };

    private static string FormatBool(bool v, BoolStyle style) => style switch
    {
        BoolStyle.YesNo    => v ? "Yes" : "No",
        BoolStyle.TrueFalse=> v ? "true" : "false",
        BoolStyle.OneZero  => v ? "1" : "0",
        BoolStyle.OuiNon   => v ? "Oui" : "Non",
        _ => v ? "true" : "false",
    };

    // -----------------------------------------------------------------------
    // File writers
    // -----------------------------------------------------------------------
    private static void WriteXlsx(string path, string sheetName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);
        for (int c = 0; c < headers.Count; c++)
            ws.Cell(1, c + 1).Value = headers[c];
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < headers.Count; c++)
                ws.Cell(r + 2, c + 1).Value = rows[r][c] ?? string.Empty;
        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
    }

    private static void WriteCsv(string path, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        using var sw = new StreamWriter(path);
        sw.WriteLine(string.Join(",", headers.Select(CsvEscape)));
        foreach (var row in rows)
            sw.WriteLine(string.Join(",", row.Select(CsvEscape)));
    }

    private static string CsvEscape(string? s)
    {
        s ??= string.Empty;
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    // -----------------------------------------------------------------------
    // Scenario row data (byte-identical to ClientAccountFixtures.GetRows)
    // -----------------------------------------------------------------------
    private static class ClientRows
    {
        public static readonly IReadOnlyList<IReadOnlyList<string?>> Ok = new List<IReadOnlyList<string?>>
        {
            new[] { "A-001", "Alice Johnson",  "1500.50", "2024-01-15" },
            new[] { "A-002", "Bob Smith",      "2750.00", "2024-02-20" },
            new[] { "A-003", "Carol Davis",    "   99.99", "2024-03-05" },
            new[] { "A-004", "Dan Brown",      "10000.0", "2024-04-12" },
            new[] { "A-005", "Eve Wilson",     "  500.00", "2024-05-30" },
        };

        public static readonly IReadOnlyList<IReadOnlyList<string?>> MissingAccountNumber = new List<IReadOnlyList<string?>>
        {
            new[] { "A-010", "Frank Wright",   "1000.00", "2024-01-10" },
            new[] { "",      "Grace Hopper",   "2000.00", "2024-02-15" }, // required missing
            new[] { "A-011", "Heidi Clarke",   "3000.00", "2024-03-20" },
            new[] { "",      "Ivan Petrov",    "4000.00", "2024-04-25" }, // required missing
            new[] { "A-012", "Judy Reeves",    "5000.00", "2024-05-30" },
            new[] { "",      "Kim Young",      "6000.00", "2024-06-10" }, // required missing
            new[] { "A-013", "Liam Foster",    "7000.00", "2024-07-15" },
        };

        public static readonly IReadOnlyList<IReadOnlyList<string?>> BadTypes = new List<IReadOnlyList<string?>>
        {
            new[] { "A-020", "Mia Tanaka",     "1500.00", "2024-01-20" },
            new[] { "A-021", "Noah Becker",    "abc",     "2024-02-25" },          // bad decimal
            new[] { "A-022", "Olivia Stein",   "3000.00", "2024-03-30" },
            new[] { "A-023", "Paolo Ricci",    "4000.00", "not-a-date" },          // bad date
            new[] { "A-024", "Quinn Adler",    "not-a-number", "2024-05-10" },      // bad decimal
            new[] { "A-025", "Rita Hahn",      "6000.00", "2024-06-20" },
        };

        public static readonly IReadOnlyList<IReadOnlyList<string?>> PartialErrors = new List<IReadOnlyList<string?>>
        {
            new[] { "A-030", "Sam Cole",       "1000.00", "2024-01-05" },
            new[] { "A-031", "Tara Webb",      "2000.00", "2024-02-10" },
            new[] { "A-032", "Umar Said",      "3000.00", "2024-03-15" },
            new[] { "A-033", "Vera Olsen",     "4000.00", "2024-04-20" },
            new[] { "",      "Walt Pham",      "5000.00", "2024-05-25" },          // required missing
            new[] { "A-034", "Xena Liu",       "6000.00", "2024-06-30" },
            new[] { "A-035", "Yara Costa",     "7000.00", "2024-07-05" },
            new[] { "A-036", "Zane Miller",    "8000.00", "2024-08-10" },
            new[] { "",      "Ada Lovelace",   "9000.00", "2024-09-15" },          // required missing
            new[] { "A-037", "Boris Pasternak","oops",   "2024-10-20" },           // bad decimal
        };
    }
}