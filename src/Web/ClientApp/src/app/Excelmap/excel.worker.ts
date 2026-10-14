import * as XLSX from 'xlsx';

export interface ExcelPreviewResult {
  sheetNames: string[];
  sheetIndex: number;
  headers: string[];
  rows: Record<string, unknown>[];
}

type WorkerInput = { bytes: ArrayBuffer; sheetIndex?: number };

type WorkerScope = {
  onmessage: ((ev: MessageEvent<WorkerInput>) => void) | null;
  postMessage(message: unknown, transfer?: Transferable[]): void;
};

/**
 * Maps each non-blank header name to its ORIGINAL column index in the first
 * row. Blank cells are skipped (they carry no name) but do not shift anything:
 * values are later read by this original index, so a blank or merged header
 * cell cannot push the following columns onto the wrong names. Duplicate
 * header names: the last occurrence wins, mirroring the server-side
 * columnIndex built by the import executor/validator.
 *
 * Pure and exported for tests. Returns an empty map when the row has no
 * usable header at all.
 */
export function extractHeaderIndex(firstRow: unknown[]): Map<string, number> {
  const headerIndex = new Map<string, number>();
  firstRow.forEach((cell, i) => {
    const h = String(cell ?? '').trim();
    if (h.length > 0) headerIndex.set(h, i);
  });
  return headerIndex;
}

const ctx = self as unknown as WorkerScope;

ctx.onmessage = (e: MessageEvent<WorkerInput>) => {
  try {
    const input = e.data;
    const buffer = input.bytes;
    const byteLength = buffer instanceof ArrayBuffer ? buffer.byteLength : (buffer as Uint8Array).byteLength;

    if (!buffer || byteLength === 0) {
      ctx.postMessage({ error: 'The file appears to be empty.' });
      return;
    }

    const workbook = XLSX.read(new Uint8Array(buffer), { type: 'array', sheetRows: 50 });
    const sheetNames = workbook.SheetNames;

    if (!sheetNames || sheetNames.length === 0) {
      ctx.postMessage({ error: 'The workbook has no sheets.' });
      return;
    }

    const requested = input.sheetIndex ?? 0;
    const sheetIndex = requested >= 0 && requested < sheetNames.length ? requested : 0;
    const sheetName = sheetNames[sheetIndex];
    const sheet = workbook.Sheets[sheetName];

    if (!sheet) {
      ctx.postMessage({ error: 'The selected sheet could not be read.' });
      return;
    }

    const raw = XLSX.utils.sheet_to_json(sheet, { header: 1, defval: null, raw: false }) as unknown[][];

    if (raw.length === 0) {
      ctx.postMessage({ error: `The sheet "${sheetName}" appears to be empty.` });
      return;
    }

    // Headers keep their ORIGINAL column index (see extractHeaderIndex): the
    // preview must attribute every value to the same header the server's own
    // parse will use.
    const headerIndex = extractHeaderIndex(raw[0]);

    if (headerIndex.size === 0) {
      ctx.postMessage({ error: 'No column headers found in the first row.' });
      return;
    }

    const headers = [...headerIndex.keys()];

    const rows = raw.slice(1).map(row => {
      const obj: Record<string, unknown> = {};
      headerIndex.forEach((colIdx, h) => { obj[h] = row[colIdx] ?? null; });
      return obj;
    });

    const result: ExcelPreviewResult = { sheetNames, sheetIndex, headers, rows };
    ctx.postMessage({ result });
  } catch (err) {
    console.error('[excel.worker] XLSX.read failed:', err);
    ctx.postMessage({ error: 'Could not read this file. Make sure it is a valid .xlsx workbook.' });
  }
};
