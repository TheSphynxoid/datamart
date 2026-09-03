import { Component, Input, signal, computed } from '@angular/core';
import { ImportReportError } from '../web-api-client';

export function buildErrorCsv(errors: ImportReportError[]): string {
    const escape = (s: string | number): string => {
        const str = String(s);
        if (/[",\n\r]/.test(str)) {
            return `"${str.replace(/"/g, '""')}"`;
        }
        return str;
    };

    const lines: string[] = ['Row,Reason'];
    for (const err of errors) {
        lines.push(`${escape(err.rowNumber)},${escape(err.reason ?? '')}`);
    }
    return lines.join('\n');
}

@Component({
    standalone: false,
    selector: 'app-report-errors',
    templateUrl: './report-errors.component.html',
    styleUrls: ['./report-errors.component.scss']
})
export class ReportErrorsComponent {
    private readonly defaultMax = 200;

    private _errors: ImportReportError[] | undefined = [];
    @Input()
    set errors(value: ImportReportError[] | undefined) {
        this._errors = value;
        this.expanded.set(false);
    }
    get errors(): ImportReportError[] | undefined {
        return this._errors;
    }

    protected expanded = signal(false);

    protected readonly max = computed(() => this.expanded() ? (this._errors?.length ?? 0) : this.defaultMax);
    protected readonly visible = computed(() => (this._errors ?? []).slice(0, this.max()));
    protected readonly hiddenCount = computed(() => Math.max(0, (this._errors?.length ?? 0) - this.max()));
    protected readonly hasMore = computed(() => this.hiddenCount() > 0);

    protected showAll(): void {
        this.expanded.set(true);
    }

    protected downloadCsv(): void {
        const rows = this._errors ?? [];
        if (rows.length === 0) return;
        const csv = buildErrorCsv(rows);
        const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `import-errors-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')}.csv`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }
}
