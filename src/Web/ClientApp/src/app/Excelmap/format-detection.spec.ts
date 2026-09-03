import { detectDateFormat, detectTimeFormat } from './format-detection';

describe('detectDateFormat', () => {
  it('detects ISO-style yyyy-MM-dd with hyphens', () => {
    expect(detectDateFormat('2024-12-31')).toBe('yyyy-MM-dd');
  });

  it('detects ISO-style yyyy/MM/dd with slashes', () => {
    expect(detectDateFormat('2024/12/31')).toBe('yyyy/MM/dd');
  });

  it('detects ISO-style yyyy.MM.dd with dots', () => {
    expect(detectDateFormat('2024.12.31')).toBe('yyyy.MM.dd');
  });

  it('detects dd/MM/yyyy when day > 12', () => {
    expect(detectDateFormat('25/01/2024')).toBe('dd/MM/yyyy');
  });

  it('detects MM/dd/yyyy when the second group > 12', () => {
    expect(detectDateFormat('01/25/2024')).toBe('MM/dd/yyyy');
  });

  it('detects dd-MM-yyyy when day > 12', () => {
    expect(detectDateFormat('25-01-2024')).toBe('dd-MM-yyyy');
  });

  it('detects dd.MM.yyyy when day > 12', () => {
    expect(detectDateFormat('25.01.2024')).toBe('dd.MM.yyyy');
  });

  it('returns null for ambiguous values where both groups could be a day', () => {
    // 05/03/2024: either 5 March or 3 May. Don't guess.
    expect(detectDateFormat('05/03/2024')).toBeNull();
  });

  it('returns null for empty / whitespace', () => {
    expect(detectDateFormat('')).toBeNull();
    expect(detectDateFormat('   ')).toBeNull();
  });

  it('returns null for non-date text', () => {
    expect(detectDateFormat('hello')).toBeNull();
  });

  it('trims surrounding whitespace before matching', () => {
    expect(detectDateFormat('  2024-12-31  ')).toBe('yyyy-MM-dd');
  });
});

describe('detectTimeFormat', () => {
  it('detects HHmmss for a 6-digit compact value within bounds', () => {
    expect(detectTimeFormat('143207')).toBe('HHmmss');
  });

  it('does not detect HHmmss for an out-of-range value', () => {
    expect(detectTimeFormat('256000')).toBeNull();
  });

  it('detects HH:mm:ss', () => {
    expect(detectTimeFormat('14:32:07')).toBe('HH:mm:ss');
  });

  it('detects HH:mm when there are no seconds', () => {
    expect(detectTimeFormat('14:32')).toBe('HH:mm');
  });

  it('detects h:mm tt for am/pm', () => {
    expect(detectTimeFormat('2:32 PM')).toBe('h:mm tt');
    expect(detectTimeFormat('2:32 am')).toBe('h:mm tt');
  });

  it('returns null for empty / whitespace', () => {
    expect(detectTimeFormat('')).toBeNull();
    expect(detectTimeFormat('   ')).toBeNull();
  });

  it('returns null for non-time text', () => {
    expect(detectTimeFormat('hello')).toBeNull();
  });
});
