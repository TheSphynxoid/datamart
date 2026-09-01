import { Component, OnInit, signal, computed } from '@angular/core';
import { DashboardClient, ImportStatisticsDto, ImportBatchListItemDto, ImportBatchesPage, ImportJournalEntryDto, ImportJournalPage } from '../web-api-client';
import { MappingEndpointsClient, MappingDto } from '../web-api-client';

@Component({
  standalone: false,
  selector: 'app-dashboard',
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.scss']
})
export class DashboardComponent implements OnInit {
  // ImportStatus enum (server-side integers): Processing=0, Success=1, Failed=2, Reverted=3.
  private static readonly StatusProcessing = 0;
  private static readonly StatusSuccess = 1;
  private static readonly StatusFailed = 2;

  protected readonly statistics = signal<ImportStatisticsDto | null>(null);
  protected readonly statisticsLoading = signal(false);

  protected readonly batches = signal<ImportBatchListItemDto[]>([]);
  protected readonly batchesPage = signal(1);
  protected readonly batchesPageSize = signal(20);
  protected readonly batchesTotalCount = signal(0);
  protected readonly batchesLoading = signal(false);

  protected readonly selectedBatchId = signal<number | null>(null);
  protected readonly journal = signal<ImportJournalEntryDto[]>([]);
  protected readonly journalTotalCount = signal(0);
  protected readonly journalPage = signal(1);
  protected readonly journalPageSize = signal(50);
  protected readonly journalLoading = signal(false);

  protected readonly mappings = signal<MappingDto[]>([]);

  // Derived: the currently selected batch's summary line (file, table, status, rows).
  protected readonly selectedBatch = computed(() => {
    const id = this.selectedBatchId();
    if (id === null) return null;
    return this.batches().find(b => b.id === id) ?? null;
  });

  // Status badge metadata for cells (label + css class).
  protected statusBadge(status: number): { label: string; cls: string } {
    switch (status) {
      case DashboardComponent.StatusSuccess: return { label: 'Success', cls: 'badge-success' };
      case DashboardComponent.StatusFailed: return { label: 'Failed', cls: 'badge-failed' };
      case DashboardComponent.StatusProcessing: return { label: 'Processing', cls: 'badge-processing' };
      default: return { label: 'Reverted', cls: 'badge-reverted' };
    }
  }

  protected journalStatusBadge(status: number): { label: string; cls: string } {
    // ImportRowStatus: Imported=0, Rejected=1.
    return status === 1
      ? { label: 'Rejected', cls: 'badge-failed' }
      : { label: 'Imported', cls: 'badge-success' };
  }

  // Pretty-print the row-level payload (JSON) for the journal cell. Falls back to the
  // raw string if parsing fails or it's empty.
  protected formatPayload(raw: string | null | undefined): string {
    if (!raw) return '';
    try {
      const obj = JSON.parse(raw);
      return JSON.stringify(obj, null, 2);
    } catch {
      return raw;
    }
  }

  constructor(
    private readonly dashboardClient: DashboardClient,
    private readonly mappingClient: MappingEndpointsClient
  ) {}

  ngOnInit(): void {
    this.loadStatistics();
    this.loadBatches();
    this.mappingClient.getMappings(undefined, undefined).subscribe({
      next: result => this.mappings.set(result ?? []),
      error: () => this.mappings.set([])
    });
  }

  protected loadStatistics(): void {
    this.statisticsLoading.set(true);
    this.dashboardClient.statistics().subscribe({
      next: result => { this.statistics.set(result); this.statisticsLoading.set(false); },
      error: () => { this.statistics.set(null); this.statisticsLoading.set(false); }
    });
  }

  protected loadBatches(): void {
    this.batchesLoading.set(true);
    this.dashboardClient.batches(
      this.batchesPage(),
      this.batchesPageSize(),
      undefined, // status
      undefined, // affectedTable
      undefined, // from
      undefined  // to
    ).subscribe({
      next: result => {
        this.batches.set(result.items ?? []);
        this.batchesTotalCount.set(result.totalCount);
        this.batchesLoading.set(false);
      },
      error: () => { this.batches.set([]); this.batchesTotalCount.set(0); this.batchesLoading.set(false); }
    });
  }

  protected onBatchRowClick(event: { data?: ImportBatchListItemDto }): void {
    const batch = event.data;
    if (!batch) return;
    this.selectedBatchId.set(batch.id);
    this.journal.set([]);
    this.journalTotalCount.set(0);
    this.journalPage.set(1);
    this.loadJournal();
  }

  protected loadJournal(): void {
    const batchId = this.selectedBatchId();
    if (batchId === null) return;
    this.journalLoading.set(true);
    this.dashboardClient.journal(batchId, this.journalPage(), this.journalPageSize()).subscribe({
      next: (result: ImportJournalPage) => {
        this.journal.set(result.items ?? []);
        this.journalTotalCount.set(result.totalCount);
        this.journalLoading.set(false);
      },
      error: () => { this.journal.set([]); this.journalTotalCount.set(0); this.journalLoading.set(false); }
    });
  }

  protected closeDetail(): void {
    this.selectedBatchId.set(null);
    this.journal.set([]);
    this.journalTotalCount.set(0);
  }
}
