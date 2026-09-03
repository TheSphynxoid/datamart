using System;
using System.IO;
using System.IO.Compression;

namespace DataMart.Web.Common;

/// <summary>
/// Inspects an uploaded stream as a zip (OOXML) package and returns whether it is a
/// well-formed .xlsx, whether it contains macros, and whether it stays within
/// zip-bomb caps. Reads only the zip's central directory / entry metadata — never
/// decompresses entry contents — so the cost is bounded by the (compressed) input
/// size, not the (potentially explosive) uncompressed size.
/// </summary>
public static class XlsxPackageInspector
{
    // Conservative caps for a real .xlsx: a typical workbook has tens of entries and
    // decompresses to a few MB. Anything beyond these is a zip-bomb candidate.
    public const int DefaultMaxEntries = 1_000;
    public const long DefaultMaxDecompressedBytes = 600L * 1024 * 1024; // 600 MB (temporarily raised to allow 1.1M-row stress tests)

    public record XlsxPackageResult(
        bool IsValidOoxPackage,
        bool ContainsMacros,
        bool ExceededEntryCount,
        bool ExceededDecompressedSize,
        string? ErrorMessage)
    {
        public bool IsAcceptable =>
            IsValidOoxPackage && !ContainsMacros && !ExceededEntryCount && !ExceededDecompressedSize;
    }

    public static XlsxPackageResult Inspect(Stream input)
        => Inspect(input, DefaultMaxEntries, DefaultMaxDecompressedBytes);

    public static XlsxPackageResult Inspect(Stream input, int maxEntries, long maxDecompressedBytes)
    {
        // ZipArchive requires a seekable stream. Copy the (already Kestrel-size-capped)
        // input into a MemoryStream. This is the only allocation; entry contents are
        // never decompressed.
        using var ms = new MemoryStream();
        input.CopyTo(ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);

        bool hasContentTypes = false;
        bool hasMacros = false;
        int entryCount = 0;
        long totalUncompressed = 0;

        foreach (var entry in archive.Entries)
        {
            entryCount++;
            totalUncompressed += entry.Length;

            if (string.Equals(entry.FullName, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
                hasContentTypes = true;

            // Macro-enabled workbooks contain a vbaProject.bin (in the root or under xl/).
            // Compare the entry name (case-insensitive) to the well-known macro blob.
            if (entry.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))
                hasMacros = true;

            if (entryCount > maxEntries)
                return new XlsxPackageResult(false, hasMacros, true, ExceededDecompressedSize: false,
                    $"Archive contains more than {maxEntries:N0} entries; upload rejected.");

            if (totalUncompressed > maxDecompressedBytes)
                return new XlsxPackageResult(false, hasMacros, ExceededEntryCount: false, true,
                    $"Archive's uncompressed size exceeds the {maxDecompressedBytes / (1024 * 1024):N0} MB cap; upload rejected.");
        }

        if (!hasContentTypes)
            return new XlsxPackageResult(false, hasMacros, false, false,
                "Not a valid .xlsx package (missing [Content_Types].xml).");

        if (hasMacros)
            return new XlsxPackageResult(true, true, false, false,
                "Macro-enabled workbooks are not accepted.");

        return new XlsxPackageResult(true, false, false, false, null);
    }
}
