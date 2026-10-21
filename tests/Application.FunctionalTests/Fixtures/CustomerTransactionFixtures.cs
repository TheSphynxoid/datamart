using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;

namespace DataMart.Application.FunctionalTests.Fixtures;

/// <summary>
/// Test data for the second importable entity (CustomerTransaction): used by the
/// import-concurrency tests to prove imports to DIFFERENT tables run concurrently
/// and each lands complete (rows + journal) despite overlapping execution.
/// </summary>
public static class CustomerTransactionFixtures
{
    private const string SheetName = "Transactions";

    private static readonly string[] Headers =
    {
        "TransactionID", "CustomerID", "CustomerDOB", "CustGender", "CustLocation",
        "CustAccountBalance", "TransactionDate", "TransactionTime", "TransactionAmount"
    };

    /// <summary>Returns a real .xlsx byte array with the given number of valid rows.</summary>
    public static byte[] CreateXlsx(int rowCount)
    {
        using var ms = new MemoryStream();
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);

        for (int c = 0; c < Headers.Length; c++)
            ws.Cell(1, c + 1).Value = Headers[c];

        for (int r = 0; r < rowCount; r++)
        {
            var row = r + 2;
            ws.Cell(row, 1).Value = $"T-{r:D6}";
            ws.Cell(row, 2).Value = $"C-{r % 500:D4}";
            ws.Cell(row, 3).Value = $"19{60 + (r % 30):D2}-0{1 + (r % 9)}-1{r % 9}";
            ws.Cell(row, 4).Value = r % 2 == 0 ? "M" : "F";
            ws.Cell(row, 5).Value = r % 3 == 0 ? "L1" : "L2";
            ws.Cell(row, 6).Value = (100 + r).ToString();
            ws.Cell(row, 7).Value = $"2024-0{1 + (r % 9)}-1{r % 9}";
            ws.Cell(row, 8).Value = $"10:{r % 60:D2}:{r % 60:D2}";
            ws.Cell(row, 9).Value = (50 + r % 900).ToString();
        }

        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
