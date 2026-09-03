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

const ctx = self as unknown as WorkerScope;

ctx.onmessage = (e: MessageEvent<WorkerInput>) => {
  try {
    const input = e.data;
    const buffer = input.bytes;
    const byteLength = buffer instanceof ArrayBuffer ? buffer.byteLength : (buffer as Uint8Array).byteLength;

    console.debug('[excel.worker] received bytes:', byteLength, 'isArrayBuffer:', buffer instanceof ArrayBuffer);

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

    const headers = raw[0]
      .map(h => String(h ?? '').trim())
      .filter(h => h.length > 0);

    if (headers.length === 0) {
      ctx.postMessage({ error: 'No column headers found in the first row.' });
      return;
    }

    const rows = raw.slice(1).map(row => {
      const obj: Record<string, unknown> = {};
      headers.forEach((h, i) => { obj[h] = row[i] ?? null; });
      return obj;
    });

    const result: ExcelPreviewResult = { sheetNames, sheetIndex, headers, rows };
    ctx.postMessage({ result });
  } catch (err) {
    console.error('[excel.worker] XLSX.read failed:', err);
    ctx.postMessage({ error: 'Could not read this file. Make sure it is a valid .xlsx workbook.' });
  }
};
