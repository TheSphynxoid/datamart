import { ImportReportError } from '../web-api-client';
import { buildErrorCsv } from './report-errors.component';

function error(rowNumber: number, reason: string): ImportReportError {
  return new ImportReportError({ rowNumber, reason });
}

describe('buildErrorCsv', () => {
  it('returns just the header for an empty list', () => {
    expect(buildErrorCsv([])).toBe('Row,Reason');
  });

  it('emits Row,Reason lines', () => {
    const errors = [error(1, 'Required field missing'), error(2, 'Bad date')];
    expect(buildErrorCsv(errors)).toBe('Row,Reason\n1,Required field missing\n2,Bad date');
  });

  it('escapes commas by quoting', () => {
    expect(buildErrorCsv([error(1, 'Value "foo, bar" invalid')]))
      .toBe('Row,Reason\n1,"Value ""foo, bar"" invalid"');
  });

  it('escapes newlines by quoting', () => {
    expect(buildErrorCsv([error(1, 'Line1\nLine2')]))
      .toBe('Row,Reason\n1,"Line1\nLine2"');
  });

  it('escapes quotes by doubling', () => {
    expect(buildErrorCsv([error(1, 'He said "hi"')]))
      .toBe('Row,Reason\n1,"He said ""hi"""');
  });

  it('handles undefined reason as empty string', () => {
    const e = new ImportReportError({ rowNumber: 5, reason: '' });
    e.reason = undefined as unknown as string;
    expect(buildErrorCsv([e])).toBe('Row,Reason\n5,');
  });

  it('handles a long list (stress)', () => {
    const errors: ImportReportError[] = [];
    for (let i = 1; i <= 10_000; i++) {
      errors.push(error(i, `Error on row ${i}`));
    }
    const csv = buildErrorCsv(errors);
    const lines = csv.split('\n');
    expect(lines.length).toBe(10_001); // header + 10k rows
    expect(lines[0]).toBe('Row,Reason');
    expect(lines[1]).toBe('1,Error on row 1');
    expect(lines[10_000]).toBe('10000,Error on row 10000');
  });
});
