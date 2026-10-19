// Shared view helpers for batch/journal rendering (pure, no Angular deps).

// ImportStatus enum (server-side integers): Processing=0, Success=1, Failed=2, Reverted=3.
export const ImportStatusValues = {
  Processing: 0,
  Success: 1,
  Failed: 2,
  Reverted: 3,
} as const;

// ImportRowStatus enum (server-side integers): Imported=0, Rejected=1.
export const ImportRowStatusValues = {
  Imported: 0,
  Rejected: 1,
} as const;

export function batchStatusBadge(status: number): { label: string; cls: string } {
  switch (status) {
    case ImportStatusValues.Success: return { label: 'Success', cls: 'badge-success' };
    case ImportStatusValues.Failed: return { label: 'Failed', cls: 'badge-failed' };
    case ImportStatusValues.Processing: return { label: 'Processing', cls: 'badge-processing' };
    default: return { label: 'Reverted', cls: 'badge-reverted' };
  }
}

// Journal drill-down row label: the typed source-file row number; legacy
// (pre-framework) rows have none, so fall back to their provenance string.
export function journalRowDisplay(entry: { rowNumber?: number | null; sourceKey: string }): string {
  return entry.rowNumber ? entry.rowNumber.toLocaleString() : entry.sourceKey;
}
