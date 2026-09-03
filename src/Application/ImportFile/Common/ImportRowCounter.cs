using System.IO;
using System.Threading;
using DataMart.Application.Common.Exceptions;
using DataMart.Application.ImportFile.Commands.ImportFile;
using ExcelDataReader;

namespace DataMart.Application.ImportFile.Common;

/// <summary>
/// Cheap pre-count of data rows in a .xlsx file (no cell parsing). Mirrors
/// the count pass in <c>ImportFileCommand</c> so async handlers can hand
/// the validator a meaningful total for the progress bar. Also performs
/// the header-presence check so a missing-column mapping is rejected
/// before any work is enqueued.
/// </summary>
public static class ImportRowCounter
{
    /// <summary>
    /// Opens the file, advances to the sheet, checks that every required
    /// header from the active mapping is present, then counts the data
    /// rows. Returns the count.
    /// </summary>
    public static int Count(
        string tempPath,
        int? sheetIndex,
        System.Collections.Generic.IReadOnlyCollection<string> requiredColumnNames,
        CancellationToken cancellationToken)
    {
        using var readStream = new FileStream(
            tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        using var reader = ExcelReaderFactory.CreateOpenXmlReader(readStream);

        ImportFileExecutor.AdvanceToSheet(reader, sheetIndex);

        if (!reader.Read())
            throw new AppValidationException("Empty worksheet.");

        var headers = new string[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
            headers[i] = reader.GetValue(i)?.ToString()?.Trim() ?? string.Empty;

        var headerSet = new System.Collections.Generic.HashSet<string>(headers, System.StringComparer.OrdinalIgnoreCase);
        foreach (var col in requiredColumnNames)
        {
            if (!headerSet.Contains(col))
                throw new AppValidationException(
                    $"Column '{col}' from the mapping was not found in the Excel file.");
        }

        int count = 0;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++count > ImportFileCommand.MaxRows)
                throw new AppValidationException(
                    $"File contains more than {ImportFileCommand.MaxRows:N0} data rows; validation rejected.");
        }
        return count;
    }
}
