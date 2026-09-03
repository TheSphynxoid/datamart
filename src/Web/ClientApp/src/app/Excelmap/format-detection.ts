export type FormatCategory = 'date' | 'time' | 'decimal' | 'boolean' | 'none';

const isoDateRegex = /^(\d{4})([/\-.])(\d{1,2})\2(\d{1,2})$/;
const numericDateRegex = /^(\d{1,2})([/\-.])(\d{1,2})\2(\d{4})$/;
const compactTimeRegex = /^(\d{2})(\d{2})(\d{2})$/;
const colonTimeRegex = /^\d{1,2}:\d{2}(:\d{2})?$/;
const ampmTimeRegex = /^\d{1,2}:\d{2}\s?(am|pm)$/i;

export function detectDateFormat(sample: string): string | null {
  const s = sample.trim();
  if (s === '') return null;

  const iso = s.match(isoDateRegex);
  if (iso) {
    return `yyyy${iso[2]}MM${iso[2]}dd`;
  }

  const num = s.match(numericDateRegex);
  if (num) {
    const a = parseInt(num[1], 10);
    const b = parseInt(num[3], 10);
    if (a > 12 && b <= 12) {
      return `dd${num[2]}MM${num[2]}yyyy`;
    }
    if (b > 12 && a <= 12) {
      return `MM${num[2]}dd${num[2]}yyyy`;
    }
    return null;
  }

  return null;
}

export function detectTimeFormat(sample: string): string | null {
  const s = sample.trim();
  if (s === '') return null;

  const compact = s.match(compactTimeRegex);
  if (compact) {
    const h = parseInt(compact[1], 10);
    const m = parseInt(compact[2], 10);
    const sec = parseInt(compact[3], 10);
    if (h <= 23 && m <= 59 && sec <= 59) {
      return 'HHmmss';
    }
  }

  if (/^\d{1,2}:\d{2}:\d{2}$/.test(s)) return 'HH:mm:ss';
  if (/^\d{1,2}:\d{2}$/.test(s)) return 'HH:mm';
  if (ampmTimeRegex.test(s)) return 'h:mm tt';

  if (colonTimeRegex.test(s)) return 'HH:mm';

  return null;
}

export function defaultDecimalSeparator(): '.' {
  return '.';
}
