export interface ColumnMappingLike {
  dateFormat?: string;
  timeFormat?: string;
  decimalSeparator?: '.' | ',';
  stripSymbols?: boolean;
  boolTrueValue?: string;
  boolFalseValue?: string;
}

export type FormatCategory = 'date' | 'time' | 'decimal' | 'boolean' | 'none';

const dateFormatters: Record<string, (s: string) => Date | null> = {
  'dd/MM/yyyy': (s) => { const m = s.match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})$/); return m ? new Date(+m[3], +m[2] - 1, +m[1]) : null; },
  'dd-MM-yyyy': (s) => { const m = s.match(/^(\d{1,2})-(\d{1,2})-(\d{4})$/); return m ? new Date(+m[3], +m[2] - 1, +m[1]) : null; },
  'dd.MM.yyyy': (s) => { const m = s.match(/^(\d{1,2})\.(\d{1,2})\.(\d{4})$/); return m ? new Date(+m[3], +m[2] - 1, +m[1]) : null; },
  'MM/dd/yyyy': (s) => { const m = s.match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})$/); return m ? new Date(+m[3], +m[1] - 1, +m[2]) : null; },
  'MM-dd-yyyy': (s) => { const m = s.match(/^(\d{1,2})-(\d{1,2})-(\d{4})$/); return m ? new Date(+m[3], +m[1] - 1, +m[2]) : null; },
  'MM.dd.yyyy': (s) => { const m = s.match(/^(\d{1,2})\.(\d{1,2})\.(\d{4})$/); return m ? new Date(+m[3], +m[1] - 1, +m[2]) : null; },
  'yyyy-MM-dd': (s) => { const m = s.match(/^(\d{4})-(\d{1,2})-(\d{1,2})$/); return m ? new Date(+m[1], +m[2] - 1, +m[3]) : null; },
  'yyyy/MM/dd': (s) => { const m = s.match(/^(\d{4})\/(\d{1,2})\/(\d{1,2})$/); return m ? new Date(+m[1], +m[2] - 1, +m[3]) : null; },
  'yyyy.MM.dd': (s) => { const m = s.match(/^(\d{4})\.(\d{1,2})\.(\d{1,2})$/); return m ? new Date(+m[1], +m[2] - 1, +m[3]) : null; },
};

const timeRegexes: Record<string, RegExp> = {
  'HHmmss': /^(\d{1,2})(\d{2})(\d{2})$/,
  'HH:mm:ss': /^(\d{1,2}):(\d{2}):(\d{2})$/,
  'HH:mm': /^(\d{1,2}):(\d{2})$/,
};

export function toTimeString(h: number, m: number, s: number): string {
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}

export function tryParseDate(value: string, format: string): Date | string {
  const parser = dateFormatters[format];
  if (!parser) return value;
  return parser(value) ?? value;
}

export function tryParseTime(value: string, format: string): string | null {
  const m = value.trim();
  const re = timeRegexes[format];
  if (!re) return null;
  const parts = m.match(re);
  if (!parts) return null;
  let h = parseInt(parts[1], 10);
  if (h > 23) return null;
  const min = parseInt(parts[2], 10);
  if (min > 59) return null;
  if (parts.length > 3) {
    const s = parseInt(parts[3], 10);
    if (s > 59) return null;
    return toTimeString(h, min, s);
  }
  return toTimeString(h, min, 0);
}

export function transformCell(
  raw: unknown,
  category: FormatCategory,
  col: ColumnMappingLike,
): unknown {
  if (raw === null || raw === undefined || raw === '') return null;

  switch (category) {
    case 'date': {
      if (raw instanceof Date) return raw;
      if (typeof raw === 'string') {
        if (col.dateFormat) return tryParseDate(raw, col.dateFormat);
        return raw;
      }
      return raw;
    }
    case 'time': {
      if (typeof raw === 'string') {
        if (raw.trim() === '') return null;
        if (col.timeFormat) {
          const parsed = tryParseTime(raw, col.timeFormat);
          if (parsed) return parsed;
        }
        return raw;
      }
      if (raw instanceof Date) return toTimeString(raw.getHours(), raw.getMinutes(), raw.getSeconds());
      if (typeof raw === 'number') {
        const totalSeconds = Math.round(raw * 86400);
        return toTimeString(
          Math.floor(totalSeconds / 3600) % 24,
          Math.floor(totalSeconds / 60) % 60,
          totalSeconds % 60
        );
      }
      return raw;
    }
    case 'decimal': {
      if (typeof raw === 'number') return raw;
      if (typeof raw === 'string') {
        let s = raw;
        if (col.stripSymbols) s = s.replace(/[^\d.,-]/g, '');
        if (col.decimalSeparator === ',') s = s.replace(/\./g, '').replace(',', '.');
        else s = s.replace(/,/g, '');
        const val = parseFloat(s);
        return isNaN(val) ? raw : val;
      }
      return raw;
    }
    case 'boolean': {
      if (typeof raw === 'boolean') return raw;
      if (typeof raw === 'string') {
        if (col.boolTrueValue && raw.toLowerCase() === col.boolTrueValue.toLowerCase()) return true;
        if (col.boolFalseValue && raw.toLowerCase() === col.boolFalseValue.toLowerCase()) return false;
      }
      return raw;
    }
    default:
      return raw;
  }
}
