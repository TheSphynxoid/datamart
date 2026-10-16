import { transformCell, tryParseDate, tryParseTime, toTimeString } from './cell-transform';

describe('tryParseDate', () => {
  it('parses dd/MM/yyyy', () => {
    const d = tryParseDate('25/12/2024', 'dd/MM/yyyy') as Date;
    expect(d.getFullYear()).toBe(2024);
    expect(d.getMonth()).toBe(11);
    expect(d.getDate()).toBe(25);
  });

  it('parses yyyy-MM-dd', () => {
    const d = tryParseDate('2024-12-31', 'yyyy-MM-dd') as Date;
    expect(d.getFullYear()).toBe(2024);
    expect(d.getMonth()).toBe(11);
    expect(d.getDate()).toBe(31);
  });

  it('parses MM/dd/yyyy (US style)', () => {
    const d = tryParseDate('12/25/2024', 'MM/dd/yyyy') as Date;
    expect(d.getFullYear()).toBe(2024);
    expect(d.getMonth()).toBe(11);
    expect(d.getDate()).toBe(25);
  });

  it('returns the raw string when the format does not match', () => {
    expect(tryParseDate('not-a-date', 'dd/MM/yyyy')).toBe('not-a-date');
  });

  it('returns the raw string for an unknown format token', () => {
    expect(tryParseDate('2024-12-31', 'bogus')).toBe('2024-12-31');
  });
});

describe('tryParseTime', () => {
  it('parses HHmmss', () => {
    expect(tryParseTime('143207', 'HHmmss')).toBe('14:32:07');
  });

  it('parses HH:mm:ss', () => {
    expect(tryParseTime('14:32:07', 'HH:mm:ss')).toBe('14:32:07');
  });

  it('parses HH:mm (no seconds)', () => {
    expect(tryParseTime('14:32', 'HH:mm')).toBe('14:32:00');
  });

  it('rejects an hour > 23', () => {
    expect(tryParseTime('24:00:00', 'HH:mm:ss')).toBeNull();
  });

  it('rejects minutes > 59', () => {
    expect(tryParseTime('14:60:00', 'HH:mm:ss')).toBeNull();
  });

  it('rejects seconds > 59', () => {
    expect(tryParseTime('14:32:60', 'HH:mm:ss')).toBeNull();
  });

  it('returns null for an unknown format', () => {
    expect(tryParseTime('14:32', 'bogus')).toBeNull();
  });
});

describe('toTimeString', () => {
  it('zero-pads each component to two digits', () => {
    expect(toTimeString(1, 2, 3)).toBe('01:02:03');
  });

  it('passes through two-digit values unchanged', () => {
    expect(toTimeString(14, 32, 7)).toBe('14:32:07');
  });
});

describe('transformCell', () => {
  it('returns null for null, undefined, or empty string', () => {
    expect(transformCell(null, 'date', {})).toBeNull();
    expect(transformCell(undefined, 'date', {})).toBeNull();
    expect(transformCell('', 'date', {})).toBeNull();
  });

  describe('date category', () => {
    it('passes a Date through unchanged', () => {
      const d = new Date(2024, 11, 31);
      expect(transformCell(d, 'date', {})).toBe(d);
    });

    it('parses a string with the configured dateFormat', () => {
      const d = transformCell('2024-12-31', 'date', { dateFormat: 'yyyy-MM-dd' }) as Date;
      expect(d.getFullYear()).toBe(2024);
      expect(d.getMonth()).toBe(11);
      expect(d.getDate()).toBe(31);
    });

    it('returns the raw string when no dateFormat is configured', () => {
      expect(transformCell('2024-12-31', 'date', {})).toBe('2024-12-31');
    });
  });

  describe('time category', () => {
    it('parses a string with the configured timeFormat', () => {
      expect(transformCell('14:32:07', 'time', { timeFormat: 'HH:mm:ss' })).toBe('14:32:07');
    });

    it('converts a Date to an HH:mm:ss string', () => {
      const d = new Date(2024, 0, 1, 14, 32, 7);
      expect(transformCell(d, 'time', {})).toBe('14:32:07');
    });

    it('converts an Excel fraction-of-day number to HH:mm:ss', () => {
      // 0.5 = 12:00:00
      expect(transformCell(0.5, 'time', {})).toBe('12:00:00');
      // 1 = 24:00:00 -> wraps to 00:00:00
      expect(transformCell(1, 'time', {})).toBe('00:00:00');
    });

    it('returns the raw string when the format does not match', () => {
      expect(transformCell('bogus', 'time', { timeFormat: 'HH:mm:ss' })).toBe('bogus');
    });
  });

  describe('decimal category', () => {
    it('passes a number through unchanged', () => {
      expect(transformCell(123.45, 'decimal', { decimalSeparator: '.' })).toBe(123.45);
    });

    it('parses a US-style string ("1,234.56") with stripSymbols + period separator', () => {
      // stripSymbols=true strips the $, comma kept when separator is '.', so the
      // string becomes "1,234.56" -> replace ',' with '' -> "1234.56" -> 1234.56
      expect(transformCell('1,234.56', 'decimal', { decimalSeparator: '.', stripSymbols: true })).toBe(1234.56);
    });

    it('parses a European-style string ("1.234,56") with comma separator', () => {
      // "1.234,56" -> strip '.' -> "1234,56" -> replace ',' with '.' -> "1234.56" -> 1234.56
      expect(transformCell('1.234,56', 'decimal', { decimalSeparator: ',' })).toBe(1234.56);
    });

    it('strips currency symbols when stripSymbols is on', () => {
      expect(transformCell('$1,234.56', 'decimal', { decimalSeparator: '.', stripSymbols: true })).toBe(1234.56);
      expect(transformCell('€1.234,56', 'decimal', { decimalSeparator: ',', stripSymbols: true })).toBe(1234.56);
    });

    it('returns the raw string when it cannot be parsed', () => {
      expect(transformCell('not a number', 'decimal', { decimalSeparator: '.' })).toBe('not a number');
    });
  });

  describe('boolean category', () => {
    it('passes a boolean through unchanged', () => {
      expect(transformCell(true, 'boolean', { boolTrueValue: 'Yes', boolFalseValue: 'No' })).toBeTrue();
      expect(transformCell(false, 'boolean', { boolTrueValue: 'Yes', boolFalseValue: 'No' })).toBeFalse();
    });

    it('maps a custom true value to true', () => {
      expect(transformCell('Yes', 'boolean', { boolTrueValue: 'Yes', boolFalseValue: 'No' })).toBeTrue();
    });

    it('maps a custom false value to false', () => {
      expect(transformCell('No', 'boolean', { boolTrueValue: 'Yes', boolFalseValue: 'No' })).toBeFalse();
    });

    it('is case-insensitive on the custom text', () => {
      expect(transformCell('YES', 'boolean', { boolTrueValue: 'yes', boolFalseValue: 'no' })).toBeTrue();
    });

    it('returns the raw string when no custom values match', () => {
      expect(transformCell('Maybe', 'boolean', { boolTrueValue: 'Yes', boolFalseValue: 'No' })).toBe('Maybe');
    });
  });
});
