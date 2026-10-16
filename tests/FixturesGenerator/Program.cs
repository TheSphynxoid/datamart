// Standalone helper that materialises the Excel-import test data sets (jeux de tests).
// Generates real .xlsx files (ClosedXML). Not part of the solution; run from the
// repo root with:
//
//     dotnet run --project tests/FixturesGenerator
//
// Output:
//   tests/Application.FunctionalTests/Fixtures/   small files mirroring the integration
//                                                  test scenarios (kept byte-identical to
//                                                  ClientAccountFixtures.GetRows so the
//                                                  tests still pass).
//   docs/sample-data/                             jury-facing French workbooks (.xlsx only —
//                                                  the product rejects anything else), each
//                                                  with a stated expected result (see the
//                                                  folder README.md).

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
    // French pools: the docs/sample-data workbooks are jury-facing deliverables
    // for a French company, so values are French (headers stay English — the
    // wizard auto-matches on entity property names). The integration-test
    // scenarios below use hardcoded rows and are unaffected by these pools.
    private static readonly string[] FirstNames =
    {
        "Marie", "Jean", "Pierre", "Sophie", "Lucas", "Emma", "Hugo", "Chloé", "Nathan", "Sarah",
        "Maxime", "Camille", "Antoine", "Manon", "Julien", "Laura", "Thomas", "Julie", "Nicolas", "Claire",
        "François", "Isabelle", "Philippe", "Nathalie", "Karim", "Fatou", "Aïcha", "Mehdi", "Sylvie", "Alain",
    };

    private static readonly string[] LastNames =
    {
        "Dupont", "Martin", "Bernard", "Dubois", "Moreau", "Laurent", "Simon", "Michel", "Lefebvre", "Leroy",
        "Roux", "David", "Bertrand", "Morel", "Fournier", "Girard", "Bonnet", "Durand", "Fontaine", "Rousseau",
        "Vincent", "Muller", "Faure", "André", "Mercier", "Lambert", "Benali", "Traoré", "Diallo", "Petit",
    };

    private static readonly string[] Cities =
    {
        "Paris", "Lyon", "Marseille", "Bordeaux", "Lille", "Nantes", "Toulouse",
        "Nice", "Strasbourg", "Montpellier", "Rennes", "Reims",
    };

    private static readonly string?[] Notes =
    {
        "Client prioritaire", "À surveiller", "Nouveau", "Saisonnier", "Entreprise", "Premium", null, null,
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

        // 2. Jury-facing sample data (jeux de tests livrables): French values,
        //    .xlsx only (the product rejects anything else), each with a stated
        //    expected result documented in docs/sample-data/README.md.
        WriteLivrableClients(docsDir, count: 2000, path: "comptes_clients_2000.xlsx");
        WriteLivrableTransactions(docsDir, count: 1000, path: "transactions_1000.xlsx");
        WriteLivrableMultiformat(docsDir, count: 150);
        WriteLivrableMultiSheet(docsDir, countPerSheet: 75);
        WriteLivrableRejected(docsDir);
        WriteLivrableMessyTransactions(docsDir, count: 200);

        // 3. Rules demo suite (jeux de tests "regles"): deterministic transaction
        //    rows with exact expected results under the documented demo rules
        //    (see docs/sample-data/README.md). Idempotent like the scenarios:
        //    never rewrite committed files (xlsx embeds timestamps -> binary churn).
        WriteRulesFile(docsDir, "regles_age_60", BuildRuleAgeRows());
        WriteRulesFile(docsDir, "regles_genre_60", BuildRuleGenderRows());
        WriteRulesFile(docsDir, "regles_montants_60", BuildRuleAmountRows());
        WriteRulesFile(docsDir, "regles_mixtes_120", BuildRuleMixedRows());

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
        // Idempotent like ClientAccountFixtures.EnsureOnDisk: never rewrite the
        // committed files (xlsx embeds timestamps, so rewriting shows up as
        // binary churn in git even when the content is identical).
        var baseName = $"client_{NameOf(scenario)}";
        var xlsx = Path.Combine(dir, $"{baseName}.xlsx");
        var csv = Path.Combine(dir, $"{baseName}.csv");
        if (!File.Exists(xlsx))
            WriteXlsx(xlsx, ClientSheet, ClientMinimalHeaders, rows);
        if (!File.Exists(csv))
            WriteCsv(csv, ClientMinimalHeaders, rows);
    }

    // -----------------------------------------------------------------------
    // Livrable: clean client rows. Uniform formats (ISO dates, period decimals,
    // Oui/Non booleans) so the import succeeds out of the box with a single
    // format setting (boolTrue=Oui, boolFalse=Non).
    // Expected: all rows imported, 0 rejected.
    // -----------------------------------------------------------------------
    private static void WriteLivrableClients(string dir, int count, string path)
    {
        var rows = GenerateClientRows(count, seed: 42, corruptionRate: 0.0,
            accountFor: (rng, _) => FakeIban(rng));
        foreach (var row in rows)
            row[7] = row[7] == "Yes" ? "Oui" : "Non";
        WriteXlsx(Path.Combine(dir, path), ClientSheet, ClientHeaders, rows);
    }

    /// <summary>Fictive but format-plausible French IBAN (for demo only).</summary>
    private static string FakeIban(Random rng) =>
        $"FR{rng.Next(10, 100)} {rng.Next(10000, 100000)} {rng.Next(10000, 100000)} " +
        $"{rng.Next(100000000, 1000000000):D9} {rng.Next(10, 100)}";

    // -----------------------------------------------------------------------
    // Livrable: every date/decimal/boolean flavour mixed per row — exercises
    // the format auto-detection and configuration UI.
    // -----------------------------------------------------------------------
    private static void WriteLivrableMultiformat(string dir, int count)
    {
        var rows = GenerateClientRows(count, seed: 1337, corruptionRate: 0.0, multiformat: true,
            accountFor: (rng, _) => FakeIban(rng));
        WriteXlsx(Path.Combine(dir, "comptes_multiformats_150.xlsx"), ClientSheet, ClientHeaders, rows);
    }

    // -----------------------------------------------------------------------
    // Livrable: two-sheet workbook — exercises the sheet selector (step 2).
    // -----------------------------------------------------------------------
    private static void WriteLivrableMultiSheet(string dir, int countPerSheet)
    {
        var path = Path.Combine(dir, "comptes_multi_feuilles.xlsx");
        using var wb = new XLWorkbook();
        foreach (var (sheet, seed) in new[] { ("Comptes actifs", 11), ("Comptes clôturés", 22) })
        {
            var ws = wb.Worksheets.Add(sheet);
            for (int c = 0; c < ClientHeaders.Length; c++)
                ws.Cell(1, c + 1).Value = ClientHeaders[c];
            var rows = GenerateClientRows(countPerSheet, seed: seed,
                corruptionRate: 0.0,
                openedYearRange: sheet == "Comptes actifs" ? (2022, 2024) : (2018, 2021),
                accountFor: (rng, _) => FakeIban(rng));
            for (int r = 0; r < rows.Count; r++)
                for (int c = 0; c < ClientHeaders.Length; c++)
                    ws.Cell(r + 2, c + 1).Value = rows[r][c] ?? string.Empty;
            ws.Columns().AdjustToContents();
        }
        wb.SaveAs(path);
    }

    // -----------------------------------------------------------------------
    // Livrable: deliberate errors so the import report has content.
    // Exact composition: 40 clean + 5 missing account + 5 bad decimal +
    // 5 bad date + 5 bad boolean = 60 rows -> 40 imported, 20 rejected.
    // -----------------------------------------------------------------------
    private static void WriteLivrableRejected(string dir)
    {
        var rows = new List<string?[]>();
        var rng = new Random(99);

        for (int i = 0; i < 5; i++)
            rows.Add(BuildClientRow(rng, i + 1, forceMissingAccount: true, forceBadDecimal: false, forceBadDate: false, forceBadBool: false, accountFor: (r, _) => FakeIban(r)));
        for (int i = 0; i < 5; i++)
            rows.Add(BuildClientRow(rng, 100 + i, forceMissingAccount: false, forceBadDecimal: true, forceBadDate: false, forceBadBool: false, accountFor: (r, _) => FakeIban(r)));
        for (int i = 0; i < 5; i++)
            rows.Add(BuildClientRow(rng, 200 + i, forceMissingAccount: false, forceBadDecimal: false, forceBadDate: true, forceBadBool: false, accountFor: (r, _) => FakeIban(r)));
        for (int i = 0; i < 5; i++)
            rows.Add(BuildClientRow(rng, 300 + i, forceMissingAccount: false, forceBadDecimal: false, forceBadDate: false, forceBadBool: true, accountFor: (r, _) => FakeIban(r)));

        var clean = GenerateClientRows(40, seed: 1, corruptionRate: 0.0,
            accountFor: (r, _) => FakeIban(r));
        foreach (var row in clean)
            row[7] = row[7] == "Yes" ? "Oui" : "Non";
        rows.AddRange(clean);

        WriteXlsx(Path.Combine(dir, "lignes_rejetees_demo.xlsx"), ClientSheet, ClientHeaders, rows);
    }

    // -----------------------------------------------------------------------
    // Livrable: 1000 clean transactions. Expected: 1000 imported, 0 rejected.
    // -----------------------------------------------------------------------
    private static void WriteLivrableTransactions(string dir, int count, string path)
    {
        var rows = GenerateLivrableTransactionRows(count, seed: 7);
        WriteXlsx(Path.Combine(dir, path), TransactionSheet, TransactionHeaders, rows);
    }

    // -----------------------------------------------------------------------
    // Livrable: transactions with mixed EU/US dates and bad values — exercises
    // the pre-flight validation report. Exact rejected count varies by seed;
    // the report shows the precise figure at runtime.
    // -----------------------------------------------------------------------
    private static void WriteLivrableMessyTransactions(string dir, int count)
    {
        var rows = new List<string?[]>();
        var rng = new Random(13);

        for (int i = 1; i <= count; i++)
        {
            var dob = new DateTime(1965 + rng.Next(35), rng.Next(1, 13), rng.Next(1, 28));
            var txDate = new DateTime(2024, rng.Next(1, 13), rng.Next(1, 29));
            var gender = rng.Next(2) == 0 ? "H" : "F";
            var balance = rng.NextDouble() * 20_000 - 10_000;
            var amount  = rng.NextDouble() * 4_000 - 2_000;

            var txId = rng.Next(8) == 0 ? null : $"TX-{1000 + i:D4}";

            var balanceStr = rng.Next(12) == 0
                ? balance.ToString("F2", CultureInfo.GetCultureInfo("fr-FR"))
                : balance.ToString("F2", CultureInfo.InvariantCulture);

            var txDateStr = rng.Next(10) == 0
                ? txDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : txDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var amountStr = rng.Next(15) == 0 ? "n/a" : amount.ToString("F2", CultureInfo.InvariantCulture);

            rows.Add(new string?[]
            {
                txId,
                $"CLI-{rng.Next(100, 999)}",
                dob.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                gender,
                Cities[rng.Next(Cities.Length)],
                balanceStr,
                txDateStr,
                $"{rng.Next(0, 24):D2}:{rng.Next(0, 60):D2}:{rng.Next(0, 60):D2}",
                amountStr,
            });
        }

        WriteXlsx(Path.Combine(dir, "transactions_avec_erreurs_200.xlsx"), TransactionSheet, TransactionHeaders, rows);
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
        (int from, int to)? openedYearRange = null,
        Func<Random, int, string>? accountFor = null)
    {
        var rng = new Random(seed);
        var rows = new List<string?[]>(count);

        for (int i = 1; i <= count; i++)
        {
            var row = BuildClientRow(rng, i, accountPrefix,
                forceMissingAccount: false, forceBadDecimal: false, forceBadDate: false, forceBadBool: false,
                multiformat: multiformat,
                openedYearRange: openedYearRange ?? (2020, 2024),
                accountFor: accountFor);

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
        string accountPrefix = "DEMO",
        bool forceMissingAccount = false,
        bool forceBadDecimal = false,
        bool forceBadDate = false,
        bool forceBadBool = false,
        bool multiformat = false,
        (int from, int to)? openedYearRange = null,
        Func<Random, int, string>? accountFor = null)
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
        var accountNumber = forceMissingAccount ? "" : (accountFor?.Invoke(rng, index) ?? $"{accountPrefix}-{index:D5}");
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

    private static List<string?[]> GenerateLivrableTransactionRows(int count, int seed)
    {
        var rng = new Random(seed);
        var rows = new List<string?[]>(count);
        for (int i = 1; i <= count; i++)
        {
            var dob = new DateTime(1965 + rng.Next(35), rng.Next(1, 13), rng.Next(1, 28));
            var txDate = new DateTime(2024, rng.Next(1, 13), rng.Next(1, 29));
            var balance = rng.NextDouble() * 20_000 - 10_000;
            var amount  = rng.NextDouble() * 4_000 - 2_000;

            rows.Add(new string?[]
            {
                $"TX-{1000 + i:D5}",
                $"CLI-{rng.Next(100, 999):D3}",
                dob.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                rng.Next(2) == 0 ? "H" : "F",
                Cities[rng.Next(Cities.Length)],
                balance.ToString("F2", CultureInfo.InvariantCulture),
                txDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                $"{rng.Next(0, 24):D2}:{rng.Next(0, 60):D2}:{rng.Next(0, 60):D2}",
                amount.ToString("F2", CultureInfo.InvariantCulture),
            });
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
    // Rules demo suite: seeded clean rows + exact injected violations, so each
    // file has a stated exact result under the demo rules (CustomerDOB <=
    // today-18y, CustGender in [H,F], TransactionAmount >= 0). Violation rows
    // are built clean then mutated on ONE column, so each trips exactly one
    // rule. Minor DOBs are fixed 2012-2021 dates — underage until 2030+, and
    // reruns reproduce the files identically.
    // -----------------------------------------------------------------------
    private static void WriteRulesFile(string dir, string baseName, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        var path = Path.Combine(dir, $"{baseName}.xlsx");
        if (!File.Exists(path))
            WriteXlsx(path, TransactionSheet, TransactionHeaders, rows);
    }

    private static readonly string[] MinorDobs =
        { "2012-04-25", "2015-06-01", "2017-08-09", "2018-03-12", "2021-11-30" };

    private static readonly string[] InvalidGenders = { "X", "Male", "?", "M", "Femme", "Homme" };

    private static readonly string[] NegativeAmounts = { "-10.50", "-0.01", "-500.00", "-99.99", "-250.00" };

    // One clean adult transaction; the index drives deterministic TX IDs.
    private static string?[] BuildCleanRuleTransaction(Random rng, int index, string txPrefix)
    {
        var dob = new DateTime(rng.Next(1960, 2001), rng.Next(1, 13), rng.Next(1, 28));
        var txDate = new DateTime(2024, rng.Next(1, 13), rng.Next(1, 28));
        return new string?[]
        {
            $"{txPrefix}-{index:D4}",
            $"CLI-{rng.Next(100, 1000):D3}",
            dob.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            rng.Next(2) == 0 ? "H" : "F",
            Cities[rng.Next(Cities.Length)],
            (rng.NextDouble() * 20_000 + 100).ToString("F2", CultureInfo.InvariantCulture),
            txDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            $"{rng.Next(0, 24):D2}:{rng.Next(0, 60):D2}:{rng.Next(0, 60):D2}",
            (rng.NextDouble() * 5_000 + 1).ToString("F2", CultureInfo.InvariantCulture),
        };
    }

    // 50 clean + 10 minors -> 50 imported, 10 rejected (18+ rule).
    private static List<string?[]> BuildRuleAgeRows()
    {
        var rng = new Random(901);
        var rows = new List<string?[]>();
        for (int i = 1; i <= 50; i++)
            rows.Add(BuildCleanRuleTransaction(rng, i, "TX-A"));
        for (int k = 0; k < 10; k++)
        {
            var row = BuildCleanRuleTransaction(rng, 51 + k, "TX-A");
            row[2] = MinorDobs[k % MinorDobs.Length];
            rows.Add(row);
        }
        return rows;
    }

    // 50 clean (one lowercase 'f') + 10 invalid -> 50/10 (In [H,F] rule).
    private static List<string?[]> BuildRuleGenderRows()
    {
        var rng = new Random(902);
        var rows = new List<string?[]>();
        for (int i = 1; i <= 50; i++)
            rows.Add(BuildCleanRuleTransaction(rng, i, "TX-G"));
        rows[2][3] = rows[2][3]!.ToLowerInvariant(); // minuscule -> accepte (insensible a la casse)
        for (int k = 0; k < 10; k++)
        {
            var row = BuildCleanRuleTransaction(rng, 51 + k, "TX-G");
            row[3] = InvalidGenders[k % InvalidGenders.Length];
            rows.Add(row);
        }
        return rows;
    }

    // 50 clean (one 0.00 limite) + 10 negatifs -> 50/10 (>= 0 rule).
    private static List<string?[]> BuildRuleAmountRows()
    {
        var rng = new Random(903);
        var rows = new List<string?[]>();
        for (int i = 1; i <= 50; i++)
            rows.Add(BuildCleanRuleTransaction(rng, i, "TX-M"));
        rows[1][8] = "0.00"; // limite incluse
        for (int k = 0; k < 10; k++)
        {
            var row = BuildCleanRuleTransaction(rng, 51 + k, "TX-M");
            row[8] = NegativeAmounts[k % NegativeAmounts.Length];
            rows.Add(row);
        }
        return rows;
    }

    // 90 clean + 10 mineurs + 10 genres invalides + 10 montants negatifs
    // -> 90 imported, 30 rejected (les trois regles cumulees).
    private static List<string?[]> BuildRuleMixedRows()
    {
        var rng = new Random(904);
        var rows = new List<string?[]>();
        for (int i = 1; i <= 90; i++)
            rows.Add(BuildCleanRuleTransaction(rng, i, "TX-X"));
        for (int k = 0; k < 10; k++)
        {
            var minor = BuildCleanRuleTransaction(rng, 91 + k, "TX-X");
            minor[2] = MinorDobs[k % MinorDobs.Length];
            rows.Add(minor);

            var gender = BuildCleanRuleTransaction(rng, 101 + k, "TX-X");
            gender[3] = InvalidGenders[k % InvalidGenders.Length];
            rows.Add(gender);

            var amount = BuildCleanRuleTransaction(rng, 111 + k, "TX-X");
            amount[8] = NegativeAmounts[k % NegativeAmounts.Length];
            rows.Add(amount);
        }
        return rows;
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