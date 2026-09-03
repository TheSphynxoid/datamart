import { Component, OnDestroy, OnInit, signal, computed, effect } from '@angular/core';
import { Subject, Subscription } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { DashboardClient, ImportStatisticsDto, ImportBatchListItemDto, ImportBatchesPage, ImportJournalEntryDto, ImportJournalPage } from '../web-api-client';
import { MappingEndpointsClient, MappingDto } from '../web-api-client';
import { PollerHandle, startPoller } from '../util/poll';

@Component({
  standalone: false,
  selector: 'app-dashboard',
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.scss']
})
export class DashboardComponent implements OnInit, OnDestroy {
  // ImportStatus enum (server-side integers): Processing=0, Success=1, Failed=2, Reverted=3.
  private static readonly StatusProcessing = 0;
  private static readonly StatusSuccess = 1;
  private static readonly StatusFailed = 2;

  // ImportRowStatus enum (server-side integers): Imported=0, Rejected=1.
  private static readonly RowImported = 0;
  private static readonly RowRejected = 1;

  protected readonly statistics = signal<ImportStatisticsDto | null>(null);
  protected readonly statisticsLoading = signal(false);

  protected readonly batches = signal<ImportBatchListItemDto[]>([]);
  protected readonly batchesPage = signal(1);
  protected readonly batchesPageSize = signal(20);
  protected readonly batchesTotalCount = signal(0);
  protected readonly batchesLoading = signal(false);

  // Batches filter state.
  protected readonly batchesStatus = signal<number | null>(null);
  protected readonly batchesTable = signal<string | null>(null);
  protected readonly batchesFrom = signal<Date | null>(null);
  protected readonly batchesTo = signal<Date | null>(null);
  protected readonly batchesSearch = signal<string>('');

  // Search debounce: the user types into a UI control bound to `batchesSearchInput`;
  // after 300ms of no further keystrokes, the value flows into `batchesSearch` and
  // triggers the effect that re-fetches.
  protected readonly batchesSearchInput = signal<string>('');
  private readonly searchInput$ = new Subject<string>();
  private searchSub: Subscription | null = null;

  // Page-size dropdown options.
  protected readonly pageSizeOptions = [20, 50, 100];

  // Status options for the batches filter dropdown.
  protected readonly statusFilterOptions = [
    { value: null, label: 'All statuses' },
    { value: 0, label: 'Processing' },
    { value: 1, label: 'Success' },
    { value: 2, label: 'Failed' },
    { value: 3, label: 'Reverted' },
  ];

  // Quick date-range presets.
  protected readonly datePresets = [
    { label: 'Last 24h',  getRange: () => [this.daysAgo(1),  new Date()] as [Date, Date] },
    { label: 'Last 7d',   getRange: () => [this.daysAgo(7),  new Date()] as [Date, Date] },
    { label: 'Last 30d',  getRange: () => [this.daysAgo(30), new Date()] as [Date, Date] },
    { label: 'All time',  getRange: () => [null, null] as [Date | null, Date | null] },
  ];

  protected readonly selectedBatchId = signal<number | null>(null);
  protected readonly journal = signal<ImportJournalEntryDto[]>([]);
  protected readonly journalTotalCount = signal(0);
  protected readonly journalPage = signal(1);
  protected readonly journalPageSize = signal(50);
  protected readonly journalLoading = signal(false);

  // Journal filter state.
  protected readonly journalStatus = signal<number | null>(null);
  protected readonly journalErrorSearch = signal<string>('');
  protected readonly journalShowPayload = signal(false);

  protected readonly journalStatusOptions = [
    { value: null, label: 'All rows' },
    { value: 0, label: 'Imported' },
    { value: 1, label: 'Rejected' },
  ];

  // Client-side filtered journal (after the server-side filter has been applied).
  // We filter on the error message text only — that's the typical "find me the
  // rows that errored on X" use case. The server already handles the row-status
  // filter.
  protected readonly journalFiltered = computed(() => {
    const q = this.journalErrorSearch().trim().toLowerCase();
    const items = this.journal();
    if (!q) return items;
    return items.filter(e => (e.errorMessage ?? '').toLowerCase().includes(q));
  });

  protected readonly mappings = signal<MappingDto[]>([]);

  // Distinct target tables for the table filter — taken from the saved mappings.
  // (The distinct list could be derived from the loaded batches instead, but
  // mappings are loaded once at startup and stay in sync with the wizard's
  // dropdowns.)
  protected readonly tableOptions = computed(() => {
    const set = new Set<string>();
    for (const m of this.mappings()) {
      if (m.targetEntity) set.add(m.targetEntity);
    }
    return [{ value: null, label: 'All tables' }, ...[...set].sort().map(t => ({ value: t, label: t }))];
  });

  // Active filter chips — anything non-default that would narrow the list.
  protected readonly activeFilters = computed(() => {
    const chips: { key: string; label: string; clear: () => void }[] = [];
    const s = this.batchesStatus();
    if (s !== null) {
      const opt = this.statusFilterOptions.find(o => o.value === s);
      chips.push({ key: 'status', label: `Status: ${opt?.label ?? s}`, clear: () => this.batchesStatus.set(null) });
    }
    const t = this.batchesTable();
    if (t) {
      chips.push({ key: 'table', label: `Table: ${t}`, clear: () => this.batchesTable.set(null) });
    }
    const from = this.batchesFrom();
    if (from) {
      chips.push({ key: 'from', label: `From: ${this.formatDate(from)}`, clear: () => this.batchesFrom.set(null) });
    }
    const to = this.batchesTo();
    if (to) {
      chips.push({ key: 'to', label: `To: ${this.formatDate(to)}`, clear: () => this.batchesTo.set(null) });
    }
    const q = this.batchesSearch().trim();
    if (q) {
      chips.push({ key: 'search', label: `Search: "${q}"`, clear: () => { this.batchesSearch.set(''); this.batchesSearchInput.set(''); } });
    }
    return chips;
  });

  protected readonly hasActiveFilters = computed(() => this.activeFilters().length > 0);

  private pollHandle: PollerHandle | null = null;

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

  // Journal sub-state indicator for the table cell. Returns the kind of dot to
  // render next to the status badge, or null when no indicator is needed.
  // The full "(N / M)" progress text lives in the batch detail panel header.
  protected journalIndicator(batch: ImportBatchListItemDto): { cls: string; title: string } | null {
    if (batch.journalIncomplete) {
      return {
        cls: 'journal-indicator journal-indicator-incomplete',
        title: `Journal incomplete: only ${batch.journalRowsWritten.toLocaleString()} of ${batch.totalRows.toLocaleString()} rows were uploaded before the process ended. The remaining rows cannot be recovered (the source file is deleted).`,
      };
    }
    if (batch.status === DashboardComponent.StatusSuccess && batch.journalRowsWritten < batch.totalRows) {
      return {
        cls: 'journal-indicator',
        title: `Journal uploading: ${batch.journalRowsWritten.toLocaleString()} of ${batch.totalRows.toLocaleString()} rows.`,
      };
    }
    return null;
  }

  // Full journal progress text for the batch detail panel header. Returns null
  // when there's nothing extra to show (journal complete or batch not Success).
  protected journalProgressText(batch: ImportBatchListItemDto): string | null {
    if (batch.journalIncomplete) {
      return `Journal incomplete: ${batch.journalRowsWritten.toLocaleString()} of ${batch.totalRows.toLocaleString()} rows uploaded. The remaining rows cannot be recovered (the source file was deleted).`;
    }
    if (batch.status === DashboardComponent.StatusSuccess && batch.journalRowsWritten < batch.totalRows) {
      return `Journal uploading: ${batch.journalRowsWritten.toLocaleString()} of ${batch.totalRows.toLocaleString()} rows.`;
    }
    return null;
  }

  protected journalStatusBadge(status: number): { label: string; cls: string } {
    return status === DashboardComponent.RowRejected
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

  // Two-way binding helpers for the search input — bound to a plain string signal
  // so the template can use `value` and `onValueChanged` without ceremony.
  protected onSearchInput(value: string): void {
    this.batchesSearchInput.set(value);
    this.searchInput$.next(value);
  }

  protected onStatusFilterChange(value: number | null | undefined): void {
    this.batchesStatus.set(value ?? null);
    this.batchesPage.set(1);
  }

  protected onTableFilterChange(value: string | null | undefined): void {
    this.batchesTable.set(value ?? null);
    this.batchesPage.set(1);
  }

  protected onFromChange(value: Date | null | undefined): void {
    this.batchesFrom.set(value ?? null);
    this.batchesPage.set(1);
  }

  protected onToChange(value: Date | null | undefined): void {
    this.batchesTo.set(value ?? null);
    this.batchesPage.set(1);
  }

  protected onPageSizeChange(value: number | undefined): void {
    this.batchesPageSize.set(value ?? 20);
    this.batchesPage.set(1);
    this.loadBatches();
  }

  protected applyDatePreset(preset: { getRange: () => [Date | null, Date | null] }): void {
    const [from, to] = preset.getRange();
    this.batchesFrom.set(from);
    this.batchesTo.set(to);
    this.batchesPage.set(1);
  }

  protected clearAllFilters(): void {
    this.batchesStatus.set(null);
    this.batchesTable.set(null);
    this.batchesFrom.set(null);
    this.batchesTo.set(null);
    this.batchesSearch.set('');
    this.batchesSearchInput.set('');
    this.batchesPage.set(1);
  }

  protected onJournalStatusChange(value: number | null | undefined): void {
    this.journalStatus.set(value ?? null);
    this.journalPage.set(1);
    this.loadJournal();
  }

  protected onJournalErrorSearchChange(value: string): void {
    this.journalErrorSearch.set(value ?? '');
    // No page reset — this is a client-side filter over the loaded page.
  }

  protected toggleJournalPayload(): void {
    this.journalShowPayload.update(v => !v);
  }

  constructor(
    private readonly dashboardClient: DashboardClient,
    private readonly mappingClient: MappingEndpointsClient
  ) {
    // React to any filter or pagination change by re-fetching. We use an effect
    // so we don't have to remember to call loadBatches() from every handler.
    effect(() => {
      // Touch every signal that should trigger a re-fetch.
      this.batchesPage();
      this.batchesPageSize();
      this.batchesStatus();
      this.batchesTable();
      this.batchesFrom();
      this.batchesTo();
      this.batchesSearch();
      this.loadBatches();
    });

    // Wire the debounced search.
    this.searchSub = this.searchInput$
      .pipe(debounceTime(300), distinctUntilChanged())
      .subscribe(value => {
        this.batchesSearch.set(value.trim());
        this.batchesPage.set(1);
      });
  }

  ngOnInit(): void {
    this.loadStatistics();
    this.mappingClient.getMappings(undefined, undefined).subscribe({
      next: result => this.mappings.set(result ?? []),
      error: () => this.mappings.set([])
    });

    // Auto-poll while any batch is in Processing so the UI reflects server-side
    // progress without a manual refresh. Stops itself once no batch is in
    // Processing, and is torn down on component destroy.
    this.startPoll();
  }

  ngOnDestroy(): void {
    this.pollHandle?.stop();
    this.pollHandle = null;
    this.searchSub?.unsubscribe();
  }

  private startPoll(): void {
    if (this.pollHandle) return;
    this.pollHandle = startPoller<ImportBatchesPage>(
      () => this.dashboardClient.batches(
        1, // poll always returns the freshest top of the list, regardless of user's page
        this.batchesPageSize(),
        this.batchesStatus() ?? undefined,
        this.batchesTable() ?? undefined,
        this.batchesFrom() ?? undefined,
        this.batchesTo() ?? undefined,
        this.batchesSearch() || undefined,
      ),
      () => false, // isDone driven manually from onUpdate
      (result) => {
        // Only merge if the user is on page 1 and no filters are applied — otherwise
        // a poll would yank the visible list out from under them.
        if (this.batchesPage() !== 1 || this.hasActiveFilters()) return;
        this.batches.set(result.items ?? []);
        this.batchesTotalCount.set(result.totalCount);
        this.batchesLoading.set(false);
        this.loadStatistics();
        const anyProcessing = (result.items ?? []).some(
          b => b.status === DashboardComponent.StatusProcessing
        );
        if (!anyProcessing && this.pollHandle) {
          this.pollHandle.stop();
          this.pollHandle = null;
        }
      },
      () => { /* terminal outcome: poller already stopped above or will be on destroy */ },
      { intervalMs: 2000 }
    );
  }

  // Re-arm the poll if the user just changed filters and there's at least one
  // in-flight batch visible. Called from the effect-driven re-fetch path.
  private maybeRestartPoll(): void {
    const anyProcessing = this.batches().some(
      b => b.status === DashboardComponent.StatusProcessing
    );
    if (anyProcessing && !this.pollHandle) {
      this.startPoll();
    }
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
      this.batchesStatus() ?? undefined,
      this.batchesTable() ?? undefined,
      this.batchesFrom() ?? undefined,
      this.batchesTo() ?? undefined,
      this.batchesSearch() || undefined,
    ).subscribe({
      next: result => {
        this.batches.set(result.items ?? []);
        this.batchesTotalCount.set(result.totalCount);
        this.batchesLoading.set(false);
        this.maybeRestartPoll();
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
    this.journalErrorSearch.set('');
    this.journalShowPayload.set(false);
    this.loadJournal();
  }

  protected loadJournal(): void {
    const batchId = this.selectedBatchId();
    if (batchId === null) return;
    this.journalLoading.set(true);
    this.dashboardClient.journal(
      batchId,
      this.journalPage(),
      this.journalPageSize(),
      this.journalStatus() ?? undefined,
    ).subscribe({
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
    this.journalStatus.set(null);
    this.journalErrorSearch.set('');
    this.journalShowPayload.set(false);
  }

  private daysAgo(days: number): Date {
    const d = new Date();
    d.setDate(d.getDate() - days);
    d.setHours(0, 0, 0, 0);
    return d;
  }

  private formatDate(d: Date): string {
    return d.toISOString().slice(0, 10);
  }
}
