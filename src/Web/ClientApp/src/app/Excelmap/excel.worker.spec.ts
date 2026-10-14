import { extractHeaderIndex } from './excel.worker';

describe('extractHeaderIndex', () => {
  it('maps each header to its original column index', () => {
    const index = extractHeaderIndex(['Account', 'Name', 'Balance']);
    expect([...index.keys()]).toEqual(['Account', 'Name', 'Balance']);
    expect(index.get('Account')).toBe(0);
    expect(index.get('Name')).toBe(1);
    expect(index.get('Balance')).toBe(2);
  });

  it('skips blank header cells WITHOUT shifting the following columns', () => {
    // Blank cell in the middle (merged cells / trailing cosmetic column are
    // common in real exports): "Balance" still owns original index 2.
    const index = extractHeaderIndex(['Account', null, 'Balance', '   ']);
    expect([...index.keys()]).toEqual(['Account', 'Balance']);
    expect(index.get('Account')).toBe(0);
    expect(index.get('Balance')).toBe(2);
  });

  it('keeps the last occurrence of a duplicate header (server parity)', () => {
    const index = extractHeaderIndex(['Amount', 'Amount', 'Name']);
    expect([...index.keys()]).toEqual(['Amount', 'Name']);
    expect(index.get('Amount')).toBe(1);
  });

  it('returns an empty map for a header row with no usable names', () => {
    const index = extractHeaderIndex([null, '', '   ']);
    expect(index.size).toBe(0);
  });
});
