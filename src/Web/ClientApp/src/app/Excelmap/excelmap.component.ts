import { Component, OnInit, OnDestroy, signal, effect, computed } from '@angular/core';
import { EntitySchemaClient, EntitySchemaDto, EntityPropertyDto } from '../web-api-client';
import type { ExcelPreviewResult } from './excel.worker';

type StepStatus = 'locked' | 'active' | 'completed';

interface ColumnMapping {
  excelColumn: string;
  dbField: string | null;
  dateFormat?: string;
  dateFormatDetected?: boolean;
  decimalSeparator?: '.' | ',';
  stripSymbols?: boolean;
  boolTrueValue?: string;
  boolFalseValue?: string;
}

type FormatCategory = 'date' | 'decimal' | 'boolean' | 'none';

interface FormatEntry {
  index: number;
  excelColumn: string;
  dbField: string;
  dataType: string;
  category: FormatCategory;
  destination: string;
  dateFormat?: string;
  dateFormatDetected?: boolean;
  decimalSeparator?: '.' | ',';
  stripSymbols?: boolean;
  boolTrueValue?: string;
  boolFalseValue?: string;
}

@Component({
  standalone: false,
  selector: 'app-excelmap',
  templateUrl: './excelmap.component.html',
  styleUrls: ['./excelmap.component.scss']
})
export class ExcelMapComponent implements OnInit, OnDestroy {
  protected readonly steps = [
    { id: 'target', label: 'Target entity' },
    { id: 'upload', label: 'Upload file' },
    { id: 'map', label: 'Map columns' },
    { id: 'preview', label: 'Preview' },
    { id: 'import', label: 'Verify & import' },
  ] as const;

  protected readonly dateFormats = [
    { label: 'dd/MM/yyyy — 31/12/2024', value: 'dd/MM/yyyy' },
    { label: 'MM/dd/yyyy — 12/31/2024', value: 'MM/dd/yyyy' },
    { label: 'yyyy-MM-dd — 2024-12-31', value: 'yyyy-MM-dd' },
    { label: 'dd-MM-yyyy — 31-12-2024', value: 'dd-MM-yyyy' },
  ];

  protected readonly decimalSeparators = [
    { label: '. (period — 1,234.56)', value: '.' },
    { label: ', (comma — 1.234,56)', value: ',' },
  ];

  protected currentStepId = signal<string>('target');
  protected entitySchemas = signal<EntitySchemaDto[] | null>(null);
  protected selectedEntity = signal<EntitySchemaDto | null>(null);
  protected draftEntity = signal<EntitySchemaDto | null>(null);

  protected selectedFile = signal<File | null>(null);
  protected excelColumns = signal<string[]>([]);
  protected previewRows = signal<Record<string, unknown>[]>([]);
  protected parsing = signal(false);
  protected parseError = signal<string | null>(null);

  protected mapping = signal<ColumnMapping[] | null>(null);
  protected mappableProperties = computed(() => {
    const entity = this.selectedEntity();
    return entity ? entity.properties!.filter(p => !p.isExcluded && !p.isPrimary) : [];
  });
  protected unmappedRequired = computed(() => {
    const props = this.mappableProperties();
    const mapped = new Set(
      (this.mapping() ?? [])
        .filter(m => m.dbField !== null)
        .map(m => m.dbField)
    );
    return props
      .filter(p => !p.isNullable && !mapped.has(p.name!))
      .map(p => p.name!);
  });
  protected formatEntries = computed<FormatEntry[]>(() => {
    const m = this.mapping();
    if (!m) return [];
    return m.flatMap((row, index) => {
      if (!row.dbField) return [];
      const category = this.formatCategoryFor(row.dbField);
      if (category === 'none') return [];
      const prop = this.mappableProperties().find(p => p.name === row.dbField);
      const dataType = prop?.dataType ?? '';
      const destination = category === 'date' ? `${dataType} \u2014 parsed to ISO 8601`
                        : category === 'decimal' ? `${dataType} \u2014 numeric`
                        : 'Boolean \u2014 true/false';
      return [{
        index,
        excelColumn: row.excelColumn,
        dbField: row.dbField,
        dataType,
        category,
        destination,
        dateFormat: row.dateFormat,
        dateFormatDetected: row.dateFormatDetected,
        decimalSeparator: row.decimalSeparator,
        stripSymbols: row.stripSymbols,
        boolTrueValue: row.boolTrueValue,
        boolFalseValue: row.boolFalseValue,
      }];
    });
  });
  protected missingFormats = computed<string[]>(() => {
    const m = this.mapping();
    if (!m) return [];
    return m
      .filter(row => row.dbField !== null
        && this.formatCategoryFor(row.dbField) === 'date'
        && !row.dateFormat)
      .map(row => row.excelColumn);
  });
  protected previewData = signal<unknown | null>(null);
  protected verified = signal<boolean | null>(null);

  private excelWorker?: Worker;

  constructor(private entitySchemaClient: EntitySchemaClient) {
    effect(() => {
      if (this.currentStepId() === 'target') {
        this.draftEntity.set(this.selectedEntity());
      }
    });

    effect(() => {
      if (this.currentStepId() === 'map' && this.mapping() === null) {
        this.autoMatchMapping();
      }
    });
  }

  ngOnInit(): void {
    this.entitySchemaClient.getEntitySchemas().subscribe({
      next: result => this.entitySchemas.set(result),
      error: error => console.error(error)
    });
  }

  ngOnDestroy(): void {
    this.excelWorker?.terminate();
  }

  protected statusOf(index: number): StepStatus {
    if (!this.priorStepsDone(index)) return 'locked';
    if (this.currentStepId() === this.steps[index].id) return 'active';
    if (this.isStepDone(index)) return 'completed';
    return 'locked';
  }

  protected summaryOf(index: number): string {
    switch (index) {
      case 0: return this.selectedEntity()?.name ?? '';
      case 1: {
        const file = this.selectedFile();
        return file ? `${file.name} (${this.excelColumns().length} columns)` : '';
      }
      case 2: {
        const m = this.mapping();
        if (!m) return 'Not yet mapped';
        const count = m.filter(x => x.dbField !== null).length;
        return `${count} of ${m.length} columns mapped`;
      }
      case 3: return 'No preview yet';
      case 4: return 'Awaiting review';
      default: return '';
    }
  }

  protected editStep(index: number): void {
    if (this.statusOf(index) === 'completed') {
      this.currentStepId.set(this.steps[index].id);
    }
  }

  protected confirmStep(index: number, commit: () => void): void {
    const wasDone = this.isStepDone(index);
    commit();
    if (wasDone) {
      this.resetFrom(index + 1);
    }
    const next = this.steps[index + 1];
    if (next) {
      this.currentStepId.set(next.id);
    }
  }

  protected confirmEntity(): void {
    this.confirmStep(0, () => this.selectedEntity.set(this.draftEntity()));
  }

  protected confirmFile(): void {
    this.confirmStep(1, () => { });
  }

  protected confirmMapping(): void {
    this.confirmStep(2, () => { });
  }

  protected confirmStub(index: number): void {
    this.confirmStep(index, () => {
      switch (index) {
        case 3: this.previewData.set(true); break;
        case 4: this.verified.set(true); break;
      }
    });
  }

  protected onMappingChanged(index: number, value: string | null): void {
    const current = this.mapping();
    if (!current) return;
    this.mapping.set(
      current.map((m, i) => {
        if (i !== index) return m;
        return {
          excelColumn: m.excelColumn,
          dbField: value ?? null,
          ...this.detectFormats(m.excelColumn, value),
        };
      })
    );
  }

  protected sampleFor(column: string): string {
    for (const row of this.previewRows()) {
      const val = row[column];
      if (val !== null && val !== undefined && val !== '') {
        const s = String(val);
        return s.length > 30 ? s.substring(0, 30) + '\u2026' : s;
      }
    }
    return '';
  }

  protected availablePropertiesFor(currentIndex: number): EntityPropertyDto[] {
    const all = this.mappableProperties();
    const mapping = this.mapping();
    if (!mapping) return all;
    const usedByOthers = new Set(
      mapping
        .map((m, i) => i !== currentIndex ? m.dbField : null)
        .filter((f): f is string => f !== null)
    );
    return all.filter(p => !usedByOthers.has(p.name!));
  }

  protected fieldBadges(prop: EntityPropertyDto): { label: string; cls: string }[] {
    const badges: { label: string; cls: string }[] = [];
    if (prop.isPrimary) badges.push({ label: 'PK', cls: 'pk' });
    if (prop.isForeignKey) badges.push({ label: 'FK', cls: 'fk' });
    if (prop.isUnique) badges.push({ label: 'UQ', cls: 'uq' });
    return badges;
  }

  protected formatCategoryFor(dbField: string): FormatCategory {
    const prop = this.mappableProperties().find(p => p.name === dbField);
    if (!prop) return 'none';
    const type = prop.dataType ?? '';
    if (['DateTime', 'DateTimeOffset', 'DateOnly'].includes(type)) return 'date';
    if (['Decimal', 'Double', 'Single'].includes(type)) return 'decimal';
    if (type === 'Boolean') return 'boolean';
    return 'none';
  }

  protected onFormatChanged(index: number, field: string, value: string | null): void {
    const current = this.mapping();
    if (!current) return;
    const val = value && value.trim() !== '' ? value : undefined;
    this.mapping.set(
      current.map((m, i) => {
        if (i !== index) return m;
        const updated = { ...m, [field]: val };
        if (field === 'dateFormat') updated.dateFormatDetected = false;
        return updated;
      })
    );
  }

  protected onToggleChanged(index: number, field: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const current = this.mapping();
    if (!current) return;
    this.mapping.set(
      current.map((m, i) =>
        i === index ? { ...m, [field]: checked } : m
      )
    );
  }

  protected onEntityChanged(value: EntitySchemaDto): void {
    this.draftEntity.set(value);
  }

  protected onFileSelected(file: File): void {
    this.selectedFile.set(file);
    this.excelColumns.set([]);
    this.previewRows.set([]);
    this.parseError.set(null);
    this.parseExcelPreview(file);
  }

  private parseExcelPreview(file: File): void {
    this.parsing.set(true);

    this.excelWorker?.terminate();
    this.excelWorker = new Worker(new URL('./excel.worker', import.meta.url), { type: 'module' });
    this.excelWorker.onmessage = (e: MessageEvent<{ result?: ExcelPreviewResult; error?: string }>) => {
      this.parsing.set(false);
      if (e.data.error) {
        this.parseError.set(e.data.error);
        this.excelColumns.set([]);
        this.previewRows.set([]);
      } else if (e.data.result) {
        this.excelColumns.set(e.data.result.headers);
        this.previewRows.set(e.data.result.rows);
      }
    };

    file.arrayBuffer().then(buffer => {
      this.excelWorker?.postMessage(buffer, [buffer]);
    }).catch(() => {
      this.parsing.set(false);
      this.parseError.set('Could not read the file.');
    });
  }

  private autoMatchMapping(): void {
    const props = this.mappableProperties();
    const propNameMap = new Map<string, string>();
    for (const p of props) {
      const key = p.name!.toLowerCase().replace(/[\s_-]+/g, '');
      propNameMap.set(key, p.name!);
    }
    this.mapping.set(
      this.excelColumns().map(col => {
        const dbField = propNameMap.get(col.toLowerCase().replace(/[\s_-]+/g, '')) ?? null;
        return {
          excelColumn: col,
          dbField,
          ...this.detectFormats(col, dbField),
        };
      })
    );
  }

  private detectFormats(excelColumn: string, dbField: string | null): Partial<ColumnMapping> {
    if (!dbField) return {};
    const category = this.formatCategoryFor(dbField);
    if (category === 'date') {
      const dateFormat = this.detectDateFormat(this.sampleFor(excelColumn));
      return dateFormat ? { dateFormat, dateFormatDetected: true } : {};
    }
    if (category === 'decimal') {
      return { decimalSeparator: '.' };
    }
    return {};
  }

  private detectDateFormat(sample: string): string | null {
    const s = sample.trim();
    const iso = s.match(/^(\d{4})([/\-.])(\d{1,2})\2(\d{1,2})$/);
    if (iso) return `yyyy${iso[2]}MM${iso[2]}dd`;
    const num = s.match(/^(\d{1,2})([/\-.])(\d{1,2})\2(\d{4})$/);
    if (num) {
      const a = parseInt(num[1], 10);
      const b = parseInt(num[3], 10);
      if (a > 12 && b <= 12) return `dd${num[2]}MM${num[2]}yyyy`;
      if (b > 12 && a <= 12) return `MM${num[2]}dd${num[2]}yyyy`;
    }
    return null;
  }

  private priorStepsDone(index: number): boolean {
    for (let j = 0; j < index; j++) {
      if (!this.isStepDone(j)) return false;
    }
    return true;
  }

  private isStepDone(index: number): boolean {
    switch (index) {
      case 0: return this.selectedEntity() !== null;
      case 1: return this.selectedFile() !== null && this.excelColumns().length > 0;
      case 2: {
        const m = this.mapping();
        return m !== null && m.some(x => x.dbField !== null);
      }
      case 3: return this.previewData() !== null;
      case 4: return this.verified() !== null;
      default: return false;
    }
  }

  private resetFrom(start: number): void {
    for (let i = start; i < this.steps.length; i++) {
      this.clearStep(i);
    }
  }

  private clearStep(index: number): void {
    switch (index) {
      case 0: this.selectedEntity.set(null); break;
      case 1:
        this.selectedFile.set(null);
        this.excelColumns.set([]);
        this.previewRows.set([]);
        this.parseError.set(null);
        break;
      case 2: this.mapping.set(null); break;
      case 3: this.previewData.set(null); break;
      case 4: this.verified.set(null); break;
    }
  }
}
