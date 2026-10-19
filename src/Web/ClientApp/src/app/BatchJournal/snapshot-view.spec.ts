import { parseMappingSnapshot, parseRulesSnapshot } from './snapshot-view';

// Matches the executor's snapshot serialization (JournalJsonOptions: camelCase,
// string enums) — see ImportFileExecutor.
const mappingJson = JSON.stringify({
  id: 7,
  name: 'ClientAccount_Default',
  targetEntity: 'ClientAccounts',
  columns: [
    { columnName: 'AccountNumber', columnType: null, targetColName: 'AccountNumber', isRequired: true, dateFormat: null, timeFormat: null, decimalSeparator: null, stripSymbols: false, boolTrueValue: null, boolFalseValue: null },
    { columnName: 'Balance', columnType: null, targetColName: 'Balance', isRequired: false, dateFormat: null, timeFormat: null, decimalSeparator: ',', stripSymbols: true, boolTrueValue: null, boolFalseValue: null },
    { columnName: 'OpenedDate', columnType: null, targetColName: 'OpenedDate', isRequired: false, dateFormat: 'yyyy-MM-dd', timeFormat: null, decimalSeparator: null, stripSymbols: false, boolTrueValue: null, boolFalseValue: null },
    { columnName: 'IgnoredCol', columnType: null, targetColName: '', isRequired: false, dateFormat: null, timeFormat: null, decimalSeparator: null, stripSymbols: false, boolTrueValue: null, boolFalseValue: null },
  ],
});

const rulesJson = JSON.stringify([
  { id: 3, targetEntity: 'ClientAccounts', field: 'Balance', operator: 'GreaterThanOrEqual', valueSource: 'Constant', value: '0', values: [], function: null, functionArg: null, message: 'Balance must be non-negative', isActive: true },
  { id: 4, targetEntity: 'ClientAccounts', field: 'Status', operator: 'In', valueSource: 'Constant', value: null, values: ['active', 'frozen'], function: null, functionArg: null, message: null, isActive: true },
  { id: 5, targetEntity: 'ClientAccounts', field: 'OpenedDate', operator: 'LessThan', valueSource: 'Function', value: null, values: [], function: 'ImportDate', functionArg: 3, message: 'Date must be before import', isActive: true },
]);

describe('snapshot-view parsers', () => {
  it('parses the mapping snapshot into a column table view, formats only where set', () => {
    const view = parseMappingSnapshot(mappingJson);
    expect(view).not.toBeNull();
    expect(view!.name).toBe('ClientAccount_Default');
    expect(view!.targetEntity).toBe('ClientAccounts');
    expect(view!.columns.length).toBe(4);

    const account = view!.columns[0];
    expect(account.dbField).toBe('AccountNumber');
    expect(account.isRequired).toBeTrue();
    expect(account.formats).toEqual([]);

    const balance = view!.columns[1];
    expect(balance.formats).toEqual(['decimal: ,', 'strip symbols']);

    const opened = view!.columns[2];
    expect(opened.formats).toEqual(['date: yyyy-MM-dd']);

    // Blank target = unmapped source column, shown as a dash.
    const ignored = view!.columns[3];
    expect(ignored.dbField).toBeNull();
  });

  it('parses the rules snapshot: constants, membership lists, and clock functions', () => {
    const rules = parseRulesSnapshot(rulesJson);
    expect(rules).not.toBeNull();
    expect(rules!.length).toBe(3);

    expect(rules![0].operator).toBe('GreaterThanOrEqual');
    expect(rules![0].valueDisplay).toBe('0');
    expect(rules![0].message).toBe('Balance must be non-negative');

    expect(rules![1].valueDisplay).toBe('active, frozen');
    expect(rules![1].message).toBeNull();

    expect(rules![2].valueDisplay).toBe('ImportDate(+3)');
  });

  it('degrades defensively: null/empty/invalid/malformed input yields null (raw fallback)', () => {
    expect(parseMappingSnapshot(null)).toBeNull();
    expect(parseMappingSnapshot('')).toBeNull();
    expect(parseMappingSnapshot('not json')).toBeNull();
    expect(parseMappingSnapshot('{"noColumnsHere": true}')).toBeNull();
    expect(parseRulesSnapshot(null)).toBeNull();
    expect(parseRulesSnapshot('{"not":"an array"}')).toBeNull();
    // Empty rules array parses to an empty list (distinct from null).
    expect(parseRulesSnapshot('[]')).toEqual([]);
  });
});
