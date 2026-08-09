import * as XLSX from 'xlsx';

export interface ExcelPreviewResult {
  headers: string[];
  rows: Record<string, unknown>[];
}

type WorkerScope = {
  onmessage: ((ev: MessageEvent<ArrayBuffer>) => void) | null;
  postMessage(message: unknown, transfer?: Transferable[]): void;
};

const ctx = self as unknown as WorkerScope;

ctx.onmessage = (e: MessageEvent<ArrayBuffer>) => {
  try {
    const workbook = XLSX.read(new Uint8Array(e.data), { type: 'array', sheetRows: 50 });
    const firstSheetName = workbook.SheetNames[0];
    const sheet = workbook.Sheets[firstSheetName];

    if (!sheet) {
      ctx.postMessage({ error: 'The workbook has no sheets.' });
      return;
    }

    const raw = XLSX.utils.sheet_to_json(sheet, { header: 1, defval: null, raw: false }) as unknown[][];

    if (raw.length === 0) {
      ctx.postMessage({ error: 'The first sheet appears to be empty.' });
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

    const result: ExcelPreviewResult = { headers, rows };
    ctx.postMessage({ result });
  } catch {
    ctx.postMessage({ error: 'Could not read this file. Make sure it is a valid .xlsx workbook.' });
  }
};
