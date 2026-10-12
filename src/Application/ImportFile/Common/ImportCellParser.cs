using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using DataMart.Application.Common.Exceptions;
using DataMart.Application.Common.Interfaces;
using DataMart.Domain.Entities;

namespace DataMart.Application.ImportFile.Common;

/// <summary>
/// Pure cell-parsing primitives shared by the import executor and the pre-flight
/// validator. Centralised so validation and import produce the same errors from
/// the same rules (a row that validates clean here will import clean there, and
/// vice versa).
/// </summary>
internal static class ImportCellParser
{
    public static ImportColumnType MapType(string clr) => clr switch
    {
        "DateTime" or "DateTimeOffset" => ImportColumnType.Timestamp,
        "DateOnly" => ImportColumnType.Date,
        "TimeOnly" => ImportColumnType.Time,
        "Decimal" or "Double" or "Single" => ImportColumnType.Decimal,
        "Boolean" => ImportColumnType.Boolean,
        "Int32" or "Int64" or "Int16" => ImportColumnType.Integer,
        "Guid" => ImportColumnType.Uuid,
        _ => ImportColumnType.Text,
    };

    public static bool IsNullish(string s) =>
        string.IsNullOrWhiteSpace(s)
        || s.Equals("nan", StringComparison.OrdinalIgnoreCase)
        || s.Equals("n/a", StringComparison.OrdinalIgnoreCase)
        || s.Equals("na", StringComparison.OrdinalIgnoreCase)
        || s.Equals("-", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for null cells and for whitespace / placeholder strings.
    /// Used by the import + validator to decide if a required field is "empty"
    /// — ExcelDataReader can return an empty string for a blank cell (rather
    /// than null), so a plain null check would miss those.</summary>
    public static bool IsEmpty(object? v) => v is null || (v is string s && IsNullish(s));

    public static object? Parse(object? cell, ColumnMapping m, ImportColumnType type) => cell is null ? null : type switch
    {
        ImportColumnType.Text => cell?.ToString(),

        ImportColumnType.Integer => cell switch
        {
            int i => i,
            double d => (int)d,
            string s when IsNullish(s) => null,
            string s => int.TryParse(s, CultureInfo.InvariantCulture, out var v) ? v
                       : throw new AppValidationException($"Cannot parse integer from '{s}'"),
            _ => throw new AppValidationException($"Cannot parse integer from '{cell}'")
        },

        ImportColumnType.Uuid => cell switch
        {
            Guid g => g,
            string s when IsNullish(s) => null,
            string s => Guid.TryParse(s, out var g) ? g
                        : throw new AppValidationException($"Cannot parse Guid from '{s}'"),
            _ => throw new AppValidationException($"Cannot parse Guid from '{cell}'")
        },

        ImportColumnType.Decimal => cell switch
        {
            decimal dec => dec,
            double d => (decimal)d,
            string s when IsNullish(s) => null,
            string s => ParseDecimal(s, m.DecimalSeparator, m.StripSymbols),
            _ => throw new AppValidationException($"Cannot parse decimal from '{cell}'")
        },

        ImportColumnType.Date or ImportColumnType.Timestamp => cell switch
        {
            DateTime dt => dt,
            double d when double.IsNaN(d) => null,
            double d => FromExcelSerial(d),
            int n => FromExcelSerial(n),
            string s when IsNullish(s) => null,
            string s => ParseDate(s, m.DateFormat),
            _ => throw new AppValidationException($"Cannot parse date from '{cell}'")
        },

        ImportColumnType.Time => cell switch
        {
            TimeOnly t => t,
            DateTime dt => TimeOnly.FromDateTime(dt),
            double d when double.IsNaN(d) => null,
            double d => FromExcelSerialTime(d),
            int n => FromExcelSerialTime(n),
            string s when IsNullish(s) => null,
            string s => ParseTime(s, m.TimeFormat),
            _ => throw new AppValidationException($"Cannot parse time from '{cell}'")
        },

        ImportColumnType.Boolean => cell switch
        {
            bool b => b,
            string s when IsNullish(s) => null,
            string s => s.Equals(m.BoolTrueValue, StringComparison.OrdinalIgnoreCase) ? true
                      : s.Equals(m.BoolFalseValue, StringComparison.OrdinalIgnoreCase) ? false
                      : throw new AppValidationException($"Cannot parse boolean from '{s}'"),
            _ => throw new AppValidationException($"Cannot parse boolean from '{cell}'")
        },

        _ => cell?.ToString()
    };

    public static decimal ParseDecimal(string s, string? separator, bool stripSymbols)
    {
        if (stripSymbols)
            s = Regex.Replace(s, @"[^\d.,\-]", "");
        if (separator == ",")
            s = s.Replace(".", "").Replace(",", ".");   // EU: remove thousands, swap decimal
        else
            s = s.Replace(",", "");                      // US: remove thousands
        if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            throw new AppValidationException($"Cannot parse decimal from '{s}'");
        return value;
    }

    public static DateTime ParseDate(string s, string? dateFormat)
    {
        // Try configured format + 2-digit year variant (dd/MM/yyyy → dd/MM/yy)
        if (!string.IsNullOrEmpty(dateFormat))
        {
            var formats = new[] { dateFormat!, dateFormat!.Replace("yyyy", "yy") };
            if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
                return exact;
        }

        // Fallback 1: InvariantCulture (US-style, month-first — handles MM/dd/yy)
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var usParsed))
            return usParsed;

        // Fallback 2: European culture (day-first — handles dd/MM/yy where day > 12)
        if (DateTime.TryParse(s, new CultureInfo("fr-FR"), DateTimeStyles.None, out var euParsed))
            return euParsed;

        throw new AppValidationException($"Cannot parse date from '{s}'");
    }

    public static DateTime FromExcelSerial(double serial)
    {
        // Valid Excel serial range: ~1900-03-01 (59) to ~2119-12-31 (80000)
        // Inverted comparison so NaN/Infinity fail the check (NaN comparisons are always false)
        if (!(serial >= 59 && serial <= 80000))
            throw new AppValidationException($"Value '{serial}' is not a valid date (expected a date string or Excel serial number).");
        return DateTime.FromOADate(serial);
    }

    public static TimeOnly FromExcelSerialTime(double serial)
    {
        // Excel stores times as the fractional part of a day: 0.5 = 12:00, 1.0 = 24:00.
        if (!(serial > 0 && serial < 1) && serial != 0)
            throw new AppValidationException($"Value '{serial}' is not a valid time (expected a time string or Excel time fraction).");
        return TimeOnly.FromTimeSpan(TimeSpan.FromDays(serial));
    }

    public static TimeOnly ParseTime(string s, string? timeFormat)
    {
        // Try the configured format first (e.g. HHmmss for "143207").
        if (!string.IsNullOrEmpty(timeFormat) && TimeOnly.TryParseExact(s, timeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return exact;

        // Fallback 1: common formats.
        var common = new[] { "HHmmss", "HH:mm:ss", "H:mm:ss", "HH:mm", "H:mm", "hh:mm tt", "h:mm tt" };
        if (TimeOnly.TryParseExact(s, common, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        // Fallback 2: culture-aware parse.
        if (TimeOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var usParsed))
            return usParsed;

        throw new AppValidationException($"Cannot parse time from '{s}'");
    }

    public static object GetDefault(ImportColumnType type) => type switch
    {
        ImportColumnType.Integer => 0,
        ImportColumnType.Uuid => Guid.Empty,
        ImportColumnType.Decimal => decimal.Zero,
        ImportColumnType.Text => string.Empty,
        ImportColumnType.Boolean => false,
        ImportColumnType.Date or ImportColumnType.Timestamp => DateTime.MinValue,
        ImportColumnType.Time => TimeOnly.MinValue,
        _ => string.Empty
    };
}
