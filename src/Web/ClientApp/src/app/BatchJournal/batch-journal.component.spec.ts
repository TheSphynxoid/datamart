import { TestBed, ComponentFixture } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';
import { BatchJournalComponent } from './batch-journal.component';
import { DashboardClient, ImportJournalSummaryDto, ImportJournalPage, BatchDetailDto } from '../web-api-client';

function summaryOf(overrides?: Partial<ImportJournalSummaryDto>): ImportJournalSummaryDto {
  return new ImportJournalSummaryDto({
    batchId: 1,
    totalRows: 10,
    importedRows: 7,
    rejectedRows: 3,
    rejectionGroups: [
      { errorCode: 'required.AccountNumber', message: 'Row 3: required field is empty', count: 2, minRowNumber: 3, maxRowNumber: 8 },
      { errorCode: 'parse.Balance.Decimal', message: 'Row 5: Cannot parse decimal', count: 1, minRowNumber: 5, maxRowNumber: 5 },
    ],
    ...overrides,
  } as never);
}

function pageOf(page: number, totalCount: number): ImportJournalPage {
  return new ImportJournalPage({
    batchId: 1, page, pageSize: 20, totalCount,
    items: [{ id: 1, sourceKey: '3', rowNumber: 3, status: 1, errorMessage: 'Row 3: required field is empty', payload: '{"AccountNumber":null}', created: new Date().toISOString() }],
  } as never);
}

function detailOf(): BatchDetailDto {
  return new BatchDetailDto({
    id: 1, fileName: 'transactions_1000.xlsx', affectedTable: 'CustomerTransactions',
    status: 1, totalRows: 1000, processedRows: 1000, skippedRows: 0,
    journalRowsWritten: 1000, journalIncomplete: false,
    skippedRowsPreview: null, failedReason: null, executedBy: 'admin',
    created: new Date().toISOString(), startedAt: new Date().toISOString(), completedAt: new Date().toISOString(),
    minImpactedId: 1, maxImpactedId: 1000,
    controlTotalsJson: null, mappingSnapshotJson: '{"name":"m"}', rulesSnapshotJson: null, journalExtrasJson: null,
  } as never);
}

describe('BatchJournalComponent — grouped journal boxes', () => {
  let fixture: ComponentFixture<BatchJournalComponent>;
  let component: BatchJournalComponent;
  let journalCalls: { page: number; status: number | undefined; errorCode: string | undefined }[];

  beforeEach(async () => {
    journalCalls = [];
    const dashboardClient = {
      // Login flow redirects here via returnUrl; the component reads the route param.
      detail: () => of(detailOf()),
      summary: () => of(summaryOf()),
      journal: (batchId: number, page: number, pageSize: number, status?: number, errorCode?: string) => {
        journalCalls.push({ page, status, errorCode });
        return of(pageOf(page, status === 1 /* Rejected */ ? 3 : 7));
      },
    };

    await TestBed.configureTestingModule({
      declarations: [BatchJournalComponent],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        { provide: DashboardClient, useValue: dashboardClient },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '1' }) } } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(BatchJournalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads the batch detail and the grouped summary on open', () => {
    expect(component['batchDetail']()).not.toBeNull();
    const summary = component['journalSummary']();
    expect(summary).not.toBeNull();
    expect(summary!.importedRows).toBe(7);
    expect(summary!.rejectionGroups.length).toBe(2);
  });

  it('expands a group (loads its rows by exact code), then collapses it (not stuck)', () => {
    expect(component['isGroupExpanded']('required.AccountNumber')).toBeFalse();

    component['toggleGroup']('required.AccountNumber');
    expect(component['isGroupExpanded']('required.AccountNumber')).toBeTrue();
    const call = journalCalls.at(-1)!;
    expect(call.status).toBe(1); // Rejected
    expect(call.errorCode).toBe('required.AccountNumber');

    // Re-expanding reuses the loaded page (no duplicate fetch).
    const callsBefore = journalCalls.length;
    component['toggleGroup']('required.AccountNumber');
    component['toggleGroup']('required.AccountNumber');
    expect(journalCalls.length).toBe(callsBefore);

    // Collapse: the box goes away — this was the reported "stuck expanded" bug.
    component['toggleGroup']('required.AccountNumber');
    expect(component['isGroupExpanded']('required.AccountNumber')).toBeFalse();
  });

  it('expands and collapses the imported box', () => {
    component['toggleImportedRows']();
    expect(component['importedExpanded']()).toBeTrue();
    expect(journalCalls.at(-1)!.status).toBe(0); // Imported
    expect(journalCalls.at(-1)!.errorCode).toBeUndefined();

    component['toggleImportedRows']();
    expect(component['importedExpanded']()).toBeFalse();
  });

  it('the collapsed box texts carry no hint noise', () => {
    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).not.toContain('expand to inspect');
  });
});
