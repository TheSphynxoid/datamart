import { Component, OnInit, OnDestroy, signal, effect, computed } from '@angular/core';
import { EntitySchemaClient, EntitySchemaDto, EntityPropertyDto, ImportClient, MappingEndpointsClient, MappingDto, ColumnMappingDto, CreateMappingCommand, ImportBatchDto, ImportReportDto } from '../web-api-client';
import type { ExcelPreviewResult } from './excel.worker';

type StepStatus = 'locked' | 'active' | 'completed';

interface ColumnMapping {
  excelColumn: string;
  dbField: string | null;
  isRequired?: boolean;
  dateFormat?: string;
  dateFormatDetected?: boolean;
  timeFormat?: string;
  timeFormatDetected?: boolean;
  decimalSeparator?: '.' | ',';
  stripSymbols?: boolean;
  boolTrueValue?: string;
  boolFalseValue?: string;
}

type FormatCategory = 'date' | 'time' | 'decimal' | 'boolean' | 'none';

interface FormatEntry {
  index: number;
  excelColumn: string;
  dbField: string;
  dataType: string;
  category: FormatCategory;
  destination: string;
  dateFormat?: string;
  dateFormatDetected?: boolean;
  timeFormat?: string;
  timeFormatDetected?: boolean;
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
    { label: 'yyyy-MM-dd — 2024-12-31', value: 'yyyy-MM-dd' },
    { label: 'yyyy/MM/dd — 2024/12/31', value: 'yyyy/MM/dd' },
    { label: 'yyyy.MM.dd — 2024.12.31', value: 'yyyy.MM.dd' },
    { label: 'dd/MM/yyyy — 31/12/2024', value: 'dd/MM/yyyy' },
    { label: 'dd-MM-yyyy — 31-12-2024', value: 'dd-MM-yyyy' },
    { label: 'dd.MM.yyyy — 31.12.2024', value: 'dd.MM.yyyy' },
    { label: 'MM/dd/yyyy — 12/31/2024', value: 'MM/dd/yyyy' },
    { label: 'MM-dd-yyyy — 12-31-2024', value: 'MM-dd-yyyy' },
    { label: 'MM.dd.yyyy — 12.31.2024', value: 'MM.dd.yyyy' },
  ];

  protected readonly timeFormats = [
    { label: 'HHmmss — 143207', value: 'HHmmss' },
    { label: 'HH:mm:ss — 14:32:07', value: 'HH:mm:ss' },
    { label: 'HH:mm — 14:32', value: 'HH:mm' },
    { label: 'h:mm tt — 2:32 PM', value: 'h:mm tt' },
  ];

  protected readonly decimalSeparators = [
    { label: '. (period — 1,234.56)', value: '.' },
    { label: ', (comma — 1.234,56)', value: ',' },
  ];

  protected readonly emptyFieldModes = [
    { label: 'Reject entire import', value: 0 },
    { label: 'Skip rows with empty fields', value: 1 },
    { label: 'Use default values', value: 2 },
  ];

  protected currentStepId = signal<string>('target');
  protected entitySchemas = signal<EntitySchemaDto[] | null>(null);
  protected selectedEntity = signal<EntitySchemaDto | null>(null);
  protected draftEntity = signal<EntitySchemaDto | null>(null);
  protected showExcludedSchema = signal(false);
  protected visibleSchemaProps = computed<EntityPropertyDto[]>(() => {
    const props = this.draftEntity()?.properties;
    if (!props) return [];
    return this.showExcludedSchema() ? props : props.filter(p => !p.isExcluded);
  });

  protected selectedFile = signal<File | null>(null);
  protected excelColumns = signal<string[]>([]);
  protected previewRows = signal<Record<string, unknown>[]>([]);
  protected parsing = signal(false);
  protected parseError = signal<string | null>(null);
  protected sheetNames = signal<string[]>([]);
  protected selectedSheetIndex = signal<number>(0);
  protected sheetOptions = computed(() => this.sheetNames().map((name, index) => ({ name, index })));

  protected savedMappings = signal<MappingDto[]>([]);
  protected loadingMappings = signal(false);
  protected showSaveForm = signal(false);
  protected mappingName = signal('');
  protected mappingDescription = signal('');
  protected savingMapping = signal(false);
  protected saveError = signal<string | null>(null);

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
                        : category === 'time' ? `${dataType} \u2014 parsed to HH:mm:ss`
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
        timeFormat: row.timeFormat,
        timeFormatDetected: row.timeFormatDetected,
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
        && this.formatCategoryFor(row.dbField) !== 'none'
        && ((this.formatCategoryFor(row.dbField) === 'date' && !row.dateFormat)
            || (this.formatCategoryFor(row.dbField) === 'time' && !row.timeFormat)))
      .map(row => row.excelColumn);
  });
  protected previewData = signal<unknown | null>(null);
  protected verified = signal<boolean | null>(null);

  protected savedMappingId = signal<number | null>(null);
  protected importing = signal(false);
  protected importProgress = signal<ImportBatchDto | null>(null);
  protected importResult = signal<{ success: boolean; batchId?: number; importedRows?: number; skippedRows?: number; skippedRowsPreview?: string | null; error?: string } | null>(null);
  protected importReport = signal<ImportReportDto | null>(null);
  protected validateReport = signal<ImportReportDto | null>(null);
  protected validating = signal(false);
  private importPoll?: number;

  protected emptyMode = signal<number>(0);

  protected transformedRows = computed<Record<string, unknown>[]>(() => {
    const rows = this.previewRows();
    const m = this.mapping();
    if (!m || rows.length === 0) return [];

    return rows.map(row => {
      const transformed: Record<string, unknown> = {};
      for (const col of m) {
        if (!col.dbField) continue;
        transformed[col.dbField] = this.transformCell(row[col.excelColumn], col);
      }
      return transformed;
    });
  });

  protected currentStepIndex = computed(() =>
    this.steps.findIndex(s => s.id === this.currentStepId())
  );

  protected stepperItems = computed(() =>
    this.steps.map((s, i) => ({
      text: s.label,
      disabled: !this.priorStepsDone(i),
      isValid: this.isStepDone(i),
    }))
  );

  private excelWorker?: Worker;

  constructor(
    private entitySchemaClient: EntitySchemaClient,
    private mappingClient: MappingEndpointsClient,
    private importClient: ImportClient
  ) {
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

    effect(() => {
      if (this.currentStepId() === 'map' && this.selectedEntity()) {
        this.loadSavedMappings();
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
    window.clearInterval(this.importPoll);
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
      case 3: {
        if (this.previewData()) return `${this.transformedRows().length} rows previewed`;
        return 'No preview yet';
      }
      case 4: {
        if (this.importing()) {
          const p = this.importProgress();
          return p ? `${p.processedRows} of ${p.totalRows} rows` : 'Starting import&hellip;';
        }
        const result = this.importResult();
        if (!result) return 'Awaiting import';
        return result.success ? `Batch #${result.batchId}` : 'Import failed';
      }
      default: return '';
    }
  }

  protected editStep(index: number): void {
    if (this.statusOf(index) === 'completed') {
      this.currentStepId.set(this.steps[index].id);
    }
  }

  protected onStepSelected(e: { component: { option: (name: string) => number } }): void {
    const idx = e.component.option('selectedIndex');
    const step = this.steps[idx];
    if (step) {
      this.currentStepId.set(step.id);
    }
  }

  protected confirmStep(index: number, commit: () => void): void {
    const wasDone = this.isStepDone(index);
    commit();
    if (wasDone) {
      // Changing the target entity must not discard the uploaded file
      // (its columns/rows are entity-independent); only the mapping and
      // later steps depend on the chosen entity.
      const resetFrom = index === 0 ? 2 : index + 1;
      this.resetFrom(resetFrom);
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

  protected confirmPreview(): void {
    this.confirmStep(3, () => { this.previewData.set(true); });
  }

  protected confirmImport(): void {
    const file = this.selectedFile();
    const mappingId = this.savedMappingId();
    if (!file || mappingId === null) return;

    this.importing.set(true);
    this.importProgress.set(null);
    this.importResult.set(null);
    this.importReport.set(null);

    const fileParam = { data: file, fileName: file.name };
    this.importClient.uploadFile(mappingId, this.emptyMode(), this.selectedSheetIndex(), fileParam).subscribe({
      next: (started) => this.pollImport(started.batchId),
      error: (error) => {
        this.importing.set(false);
        this.importResult.set({
          success: false,
          error: error?.message ?? 'Import failed. Check server logs.',
        });
        console.error('Import failed', error);
      },
    });
  }

  private pollImport(batchId: number): void {
    window.clearInterval(this.importPoll);
    this.importPoll = window.setInterval(() => {
      this.importClient.batches2(batchId).subscribe({
        next: (batch) => {
          this.importProgress.set(batch);
          if (batch.status !== 0) { // 0 = Processing
            window.clearInterval(this.importPoll);
            this.importPoll = undefined;
            this.importing.set(false);
            if (batch.status === 1) { // 1 = Success
              this.importResult.set({
                success: true,
                batchId: batch.id,
                importedRows: batch.totalRows - batch.skippedRows,
                skippedRows: batch.skippedRows,
                skippedRowsPreview: batch.skippedRowsPreview ?? null,
              });
                this.verified.set(true);
                this.fetchImportReport(batch.id);
              } else {
              this.importResult.set({
                success: false,
                error: 'Import failed. Check server logs.',
              });
            }
          }
        },
        error: (error) => {
          window.clearInterval(this.importPoll);
          this.importPoll = undefined;
          this.importing.set(false);
          this.importResult.set({
            success: false,
            error: error?.message ?? 'Import failed. Check server logs.',
          });
          console.error('Polling import failed', error);
        },
      });
    }, 500);
  }

  protected progressPercent(batch: ImportBatchDto): number {
    if (batch.totalRows <= 0) return 0;
    return Math.round((batch.processedRows / batch.totalRows) * 100);
  }

  private fetchImportReport(id: number): void {
    this.importClient.report(id).subscribe({
      next: report => this.importReport.set(report),
      error: () => this.importReport.set(null),
    });
  }

  protected runValidation(): void {
    const file = this.selectedFile();
    const mappingId = this.savedMappingId();
    if (!file || mappingId === null) return;

    this.validating.set(true);
    const fileParam = { data: file, fileName: file.name };
    this.importClient.validate(mappingId, this.emptyMode(), this.selectedSheetIndex(), fileParam).subscribe({
      next: report => {
        this.validateReport.set(report);
        this.validating.set(false);
      },
      error: error => {
        this.validateReport.set(null);
        this.validating.set(false);
        this.parseError.set(error?.message ?? 'Validation failed. Check server logs.');
      },
    });
  }

  protected onMappingChanged(index: number, e: { value?: string | null; event?: Event }): void {
    // DevExtreme raises onValueChanged for programmatic [value] changes too — e.g.
    // when applySavedMapping sets a row's dbField. Re-running detectFormats there
    // would wipe just-loaded formats (detectFormats returns {} for booleans, and
    // {} for dates it can't auto-detect). Only treat it as a real change when a
    // DOM event is present (user interaction).
    if (!e.event) return;
    const value = e.value;
    const current = this.mapping();
    if (!current) return;
    this.savedMappingId.set(null);
    this.mapping.set(
      current.map((m, i) => {
        if (i !== index) return m;
        return {
          excelColumn: m.excelColumn,
          dbField: value ?? null,
          isRequired: m.isRequired ?? false,
          ...this.detectFormats(m.excelColumn, value),
        };
      })
    );
  }

  private transformCell(raw: unknown, col: ColumnMapping): unknown {
    if (raw === null || raw === undefined || raw === '') return null;

    const category = this.formatCategoryFor(col.dbField!);
    switch (category) {
      case 'date': {
        if (raw instanceof Date) return raw;
        if (typeof raw === 'string') {
          if (col.dateFormat) return this.tryParseDate(raw, col.dateFormat);
          return raw;
        }
        return raw;
      }
      case 'time': {
        if (typeof raw === 'string') {
          if (raw.trim() === '') return null;
          if (col.timeFormat) {
            const parsed = this.tryParseTime(raw, col.timeFormat);
            if (parsed) return parsed;
          }
          return raw;
        }
        if (raw instanceof Date) return this.toTimeString(raw.getHours(), raw.getMinutes(), raw.getSeconds());
        if (typeof raw === 'number') {
          const totalSeconds = Math.round(raw * 86400);
          return this.toTimeString(
            Math.floor(totalSeconds / 3600) % 24,
            Math.floor(totalSeconds / 60) % 60,
            totalSeconds % 60
          );
        }
        return raw;
      }
      case 'decimal': {
        if (typeof raw === 'number') return raw;
        if (typeof raw === 'string') {
          let s = raw;
          if (col.stripSymbols) s = s.replace(/[^\d.,-]/g, '');
          if (col.decimalSeparator === ',') s = s.replace(/\./g, '').replace(',', '.');
          else s = s.replace(/,/g, '');
          const val = parseFloat(s);
          return isNaN(val) ? raw : val;
        }
        return raw;
      }
      case 'boolean': {
        if (typeof raw === 'boolean') return raw;
        if (typeof raw === 'string') {
          if (col.boolTrueValue && raw.toLowerCase() === col.boolTrueValue.toLowerCase()) return true;
          if (col.boolFalseValue && raw.toLowerCase() === col.boolFalseValue.toLowerCase()) return false;
        }
        return raw;
      }
      default:
        return raw;
    }
  }

  private tryParseDate(value: string, format: string): Date | string {
    const formatMap: Record<string, (s: string) => Date | null> = {
      'dd/MM/yyyy': (s) => { const m = s.match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})$/); return m ? new Date(+m[3], +m[2] - 1, +m[1]) : null; },
      'dd-MM-yyyy': (s) => { const m = s.match(/^(\d{1,2})-(\d{1,2})-(\d{4})$/); return m ? new Date(+m[3], +m[2] - 1, +m[1]) : null; },
      'dd.MM.yyyy': (s) => { const m = s.match(/^(\d{1,2})\.(\d{1,2})\.(\d{4})$/); return m ? new Date(+m[3], +m[2] - 1, +m[1]) : null; },
      'MM/dd/yyyy': (s) => { const m = s.match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})$/); return m ? new Date(+m[3], +m[1] - 1, +m[2]) : null; },
      'MM-dd-yyyy': (s) => { const m = s.match(/^(\d{1,2})-(\d{1,2})-(\d{4})$/); return m ? new Date(+m[3], +m[1] - 1, +m[2]) : null; },
      'MM.dd.yyyy': (s) => { const m = s.match(/^(\d{1,2})\.(\d{1,2})\.(\d{4})$/); return m ? new Date(+m[3], +m[1] - 1, +m[2]) : null; },
      'yyyy-MM-dd': (s) => { const m = s.match(/^(\d{4})-(\d{1,2})-(\d{1,2})$/); return m ? new Date(+m[1], +m[2] - 1, +m[3]) : null; },
      'yyyy/MM/dd': (s) => { const m = s.match(/^(\d{4})\/(\d{1,2})\/(\d{1,2})$/); return m ? new Date(+m[1], +m[2] - 1, +m[3]) : null; },
      'yyyy.MM.dd': (s) => { const m = s.match(/^(\d{4})\.(\d{1,2})\.(\d{1,2})$/); return m ? new Date(+m[1], +m[2] - 1, +m[3]) : null; },
    };
    const parser = formatMap[format];
    if (!parser) return value;
    return parser(value) ?? value;
  }

  private tryParseTime(value: string, format: string): string | null {
    const m = value.trim();
    const timeMap: Record<string, RegExp> = {
      'HHmmss': /^(\d{1,2})(\d{2})(\d{2})$/,
      'HH:mm:ss': /^(\d{1,2}):(\d{2}):(\d{2})$/,
      'HH:mm': /^(\d{1,2}):(\d{2})$/,
    };
    const re = timeMap[format];
    if (!re) return null;
    const parts = m.match(re);
    if (!parts) return null;
    let h = parseInt(parts[1], 10);
    if (h > 23) return null;
    const min = parseInt(parts[2], 10);
    if (min > 59) return null;
    if (parts.length > 3) {
      const s = parseInt(parts[3], 10);
      if (s > 59) return null;
      return this.toTimeString(h, min, s);
    }
    return this.toTimeString(h, min, 0);
  }

  private toTimeString(h: number, m: number, s: number): string {
    return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
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
    if (type === 'TimeOnly') return 'time';
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
        if (field === 'timeFormat') updated.timeFormatDetected = false;
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

  protected onSchemaRowPrepared(e: { data?: EntityPropertyDto; rowElement?: Element }): void {
    if (e.data?.isExcluded && e.rowElement) {
      e.rowElement.classList.add('excluded-row');
    }
  }

  protected onLoadMappingSelected(value: MappingDto | null): void {
    if (value) {
      this.applySavedMapping(value);
    }
  }

  protected toggleSaveForm(): void {
    this.showSaveForm.set(!this.showSaveForm());
    this.saveError.set(null);
    if (!this.showSaveForm()) {
      this.mappingName.set('');
      this.mappingDescription.set('');
    }
  }

  protected saveCurrentMapping(): void {
    const entity = this.selectedEntity();
    const currentMapping = this.mapping();
    if (!entity?.name || !currentMapping) return;

    this.savingMapping.set(true);
    this.saveError.set(null);

    const command = new CreateMappingCommand({
      name: this.mappingName(),
      description: this.mappingDescription() || undefined,
      targetEntity: entity.name,
      columns: currentMapping
        .filter(m => m.dbField !== null)
        .map(m => new ColumnMappingDto({
          columnName: m.excelColumn,
          columnType: undefined,
          targetColName: m.dbField!,
          isRequired: m.isRequired ?? false,
          dateFormat: m.dateFormat,
          timeFormat: m.timeFormat,
          decimalSeparator: m.decimalSeparator,
          stripSymbols: m.stripSymbols ?? false,
          boolTrueValue: m.boolTrueValue,
          boolFalseValue: m.boolFalseValue,
        }))
    });

    this.mappingClient.createMapping(command).subscribe({
      next: (id: number) => {
        this.savedMappingId.set(id);
        this.savingMapping.set(false);
        this.showSaveForm.set(false);
        this.mappingName.set('');
        this.mappingDescription.set('');
        this.validateReport.set(null);
        this.loadSavedMappings();
      },
      error: error => {
        this.savingMapping.set(false);
        this.saveError.set('Failed to save mapping.');
        console.error(error);
      }
    });
  }

  protected onFileSelected(file: File): void {
    this.selectedFile.set(file);
    this.excelColumns.set([]);
    this.previewRows.set([]);
    this.sheetNames.set([]);
    this.selectedSheetIndex.set(0);
    this.parseError.set(null);
    this.validateReport.set(null);
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
        this.sheetNames.set([]);
      } else if (e.data.result) {
        this.sheetNames.set(e.data.result.sheetNames);
        this.selectedSheetIndex.set(e.data.result.sheetIndex);
        this.excelColumns.set(e.data.result.headers);
        this.previewRows.set(e.data.result.rows);
      }
    };

    file.arrayBuffer().then(buffer => {
      this.excelWorker?.postMessage({ bytes: buffer, sheetIndex: this.selectedSheetIndex() }, [buffer]);
    }).catch(() => {
      this.parsing.set(false);
      this.parseError.set('Could not read the file.');
    });
  }

  protected onSheetChanged(index: number): void {
    if (this.selectedSheetIndex() === index) return;
    this.selectedSheetIndex.set(index);
    // The columns change with the sheet, so a mapping already built for the
    // previous sheet is no longer valid — drop it and everything downstream.
    if (this.mapping() !== null) {
      this.resetFrom(2);
    }
    this.validateReport.set(null);
    const file = this.selectedFile();
    if (file) {
      this.parseExcelPreview(file);
    }
  }

  private loadSavedMappings(): void {
    const entity = this.selectedEntity();
    if (!entity?.name) return;

    this.loadingMappings.set(true);
    this.mappingClient.getMappings(entity.name, undefined).subscribe({
      next: result => {
        this.savedMappings.set(result);
        this.loadingMappings.set(false);
      },
      error: error => {
        this.loadingMappings.set(false);
        this.savedMappings.set([]);
        console.error('Failed to load saved mappings', error);
      }
    });
  }

  private applySavedMapping(saved: MappingDto): void {
    const propNames = new Set(this.mappableProperties().map(p => p.name));
    this.mapping.set(
      this.excelColumns().map(col => {
        const savedCol = saved.columns.find(c => c.columnName === col);
        if (!savedCol || !savedCol.targetColName) {
          return { excelColumn: col, dbField: null };
        }
        if (!propNames.has(savedCol.targetColName)) {
          return { excelColumn: col, dbField: null };
        }
        return {
          excelColumn: col,
          dbField: savedCol.targetColName,
          isRequired: savedCol.isRequired ?? false,
          dateFormat: savedCol.dateFormat ?? undefined,
          timeFormat: savedCol.timeFormat ?? undefined,
          decimalSeparator: savedCol.decimalSeparator as '.' | ',' | undefined,
          stripSymbols: savedCol.stripSymbols ?? undefined,
          boolTrueValue: savedCol.boolTrueValue ?? undefined,
          boolFalseValue: savedCol.boolFalseValue ?? undefined,
        };
      })
    );
    this.savedMappingId.set(saved.id);
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
          isRequired: false,
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
    if (category === 'time') {
      const timeFormat = this.detectTimeFormat(this.sampleFor(excelColumn));
      return timeFormat ? { timeFormat, timeFormatDetected: true } : {};
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

  private detectTimeFormat(sample: string): string | null {
    const s = sample.trim();
    const compact = s.match(/^(\d{2})(\d{2})(\d{2})$/);
    if (compact) {
      const h = parseInt(compact[1], 10);
      const m = parseInt(compact[2], 10);
      const sec = parseInt(compact[3], 10);
      if (h <= 23 && m <= 59 && sec <= 59) return 'HHmmss';
    }
    if (/^\d{1,2}:\d{2}:\d{2}$/.test(s)) return 'HH:mm:ss';
    if (/^\d{1,2}:\d{2}$/.test(s)) return 'HH:mm';
    if (/^\d{1,2}:\d{2}\s?(am|pm)$/i.test(s)) return 'h:mm tt';
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
        this.sheetNames.set([]);
        this.selectedSheetIndex.set(0);
        this.parseError.set(null);
        this.validateReport.set(null);
        break;
      case 2:
        this.mapping.set(null);
        this.savedMappings.set([]);
        this.showSaveForm.set(false);
        this.saveError.set(null);
        this.mappingName.set('');
        this.mappingDescription.set('');
        this.savedMappingId.set(null);
        this.validateReport.set(null);
        break;
      case 3: this.previewData.set(null); break;
      case 4:
        this.verified.set(null);
        this.importResult.set(null);
        this.importReport.set(null);
        this.importProgress.set(null);
        this.importing.set(false);
        window.clearInterval(this.importPoll);
        this.importPoll = undefined;
        break;
    }
  }
}
