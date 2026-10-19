// Pure parsers for the import-time config snapshots (journal framework §3):
// the journal page renders them as readable tables; the raw JSON stays
// available behind a toggle + download. Defensive by design — snapshots are
// audit evidence written by an older build at import time, so an unexpected
// shape degrades to null (caller falls back to the raw JSON view), never
// throws.

export interface SnapshotColumnView {
  excelColumn: string;
  dbField: string | null;
  isRequired: boolean;
  /** Human phrases for the format options actually set on the column. */
  formats: string[];
}

export interface MappingSnapshotView {
  id: number | null;
  name: string;
  targetEntity: string;
  columns: SnapshotColumnView[];
}

export interface RuleSnapshotView {
  id: number | null;
  field: string;
  operator: string;
  /** The rule's right-hand side: constant, membership list, or clock function. */
  valueDisplay: string;
  message: string | null;
}

export function parseMappingSnapshot(raw: string | null | undefined): MappingSnapshotView | null {
  if (!raw) return null;
  let obj: unknown;
  try {
    obj = JSON.parse(raw);
  } catch {
    return null;
  }
  if (!obj || typeof obj !== 'object' || !Array.isArray((obj as Record<string, unknown>)['columns'])) return null;

  const o = obj as Record<string, unknown>;
  const columns = (o['columns'] as unknown[]).map((c): SnapshotColumnView => {
    const col = (c ?? {}) as Record<string, unknown>;
    const dbField = typeof col['targetColName'] === 'string' && (col['targetColName'] as string).trim() !== ''
      ? col['targetColName'] as string
      : null;
    const formats: string[] = [];
    if (col['dateFormat']) formats.push(`date: ${col['dateFormat']}`);
    if (col['timeFormat']) formats.push(`time: ${col['timeFormat']}`);
    if (col['decimalSeparator']) formats.push(`decimal: ${col['decimalSeparator']}`);
    if (col['stripSymbols'] === true) formats.push('strip symbols');
    if (col['boolTrueValue'] || col['boolFalseValue']) {
      formats.push(`true='${col['boolTrueValue'] ?? '?'}' / false='${col['boolFalseValue'] ?? '?'}'`);
    }
    return {
      excelColumn: typeof col['columnName'] === 'string' ? col['columnName'] as string : '(unknown)',
      dbField,
      isRequired: col['isRequired'] === true,
      formats,
    };
  });

  return {
    id: typeof o['id'] === 'number' ? o['id'] as number : null,
    name: typeof o['name'] === 'string' ? o['name'] as string : '',
    targetEntity: typeof o['targetEntity'] === 'string' ? o['targetEntity'] as string : '',
    columns,
  };
}

export function parseRulesSnapshot(raw: string | null | undefined): RuleSnapshotView[] | null {
  if (!raw) return null;
  let obj: unknown;
  try {
    obj = JSON.parse(raw);
  } catch {
    return null;
  }
  if (!Array.isArray(obj)) return null;

  return obj.map((r): RuleSnapshotView => {
    const rule = (r ?? {}) as Record<string, unknown>;
    let valueDisplay: string;
    if (rule['valueSource'] === 'Function') {
      const arg = rule['functionArg'] != null ? `+${rule['functionArg']}` : '';
      valueDisplay = `${rule['function'] ?? '?'}(${arg})`;
    } else if (Array.isArray(rule['values']) && rule['values'].length > 0) {
      valueDisplay = (rule['values'] as unknown[]).map(String).join(', ');
    } else if (rule['value'] != null) {
      valueDisplay = String(rule['value']);
    } else {
      valueDisplay = '';
    }
    return {
      id: typeof rule['id'] === 'number' ? rule['id'] as number : null,
      field: typeof rule['field'] === 'string' ? rule['field'] as string : '',
      operator: typeof rule['operator'] === 'string' ? rule['operator'] as string : '',
      valueDisplay,
      message: typeof rule['message'] === 'string' ? rule['message'] as string : null,
    };
  });
}
