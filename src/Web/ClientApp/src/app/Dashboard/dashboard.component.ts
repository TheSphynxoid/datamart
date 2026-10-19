import { Component, OnDestroy, OnInit, signal, computed, effect } from '@angular/core';
import { Subject, Subscription } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { DashboardClient, ImportStatisticsDto, ImportBatchListItemDto, ImportBatchesPage, ImportJournalEntryDto, ImportJournalPage, BatchDetailDto, ImportJournalSummaryDto } from '../web-api-client';
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

  // Grouped journal drill-down (tier 2): imported rows collapse into one box
  // (their entries would be identical); rejections group by stable error code
  // — same issue, same box. Expanding a box loads its rows paginated on demand;
  // nothing ever loads all rows of a batch.
  protected readonly journalSummary = signal<ImportJournalSummaryDto | null>(null);
  protected readonly journalSummaryLoading = signal(false);
  protected readonly importedExpanded = signal(false);
  protected readonly importedPage = signal<ImportJournalPage | null>(null);
  protected readonly expandedGroups = signal<string[]>([]);
  // Rows per expanded group, keyed by the group's error code; null = load failed.
  protected readonly groupPages = signal<Record<string, ImportJournalPage | null>>({});

  // Page size for the rows shown inside an expanded journal box.
  private static readonly JournalBoxPageSize = 20;

  // Batch detail (tier-1 record incl. snapshot payloads): fetched once when a
  // batch is opened — never polled, snapshots are immutable after terminal status.
  protected readonly batchDetail = signal<BatchDetailDto | null>(null);
  protected readonly batchDetailLoading = signal(false);
  // Snapshot views are collapsed by default (progressive disclosure): the JSON
  // blocks are audit evidence, not information the common "how did it go" scan needs.
  protected readonly showMappingSnapshot = signal(false);
  protected readonly showRulesSnapshot = signal(false);

  // Journal payload visibility (shared by every expanded box).
  protected readonly journalShowPayload = signal(false);

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

  // Compact text chips for the per-batch config snapshots (tier-1 list). Letter
  // chips + tooltips keep the cell small; the full JSON lives in the detail panel.
  protected snapshotChips(batch: { hasMappingSnapshot: boolean; hasRulesSnapshot: boolean }): { label: string; title: string }[] {
    const chips: { label: string; title: string }[] = [];
    if (batch.hasMappingSnapshot) chips.push({ label: 'M', title: 'Mapping snapshot available — open the batch for the full JSON.' });
    if (batch.hasRulesSnapshot) chips.push({ label: 'R', title: 'Rules snapshot available — open the batch for the full JSON.' });
    return chips;
  }

  // "min–max" for the impacted-ID range cell; an em dash reads better than
  // "0" or "null" for pre-framework batches with no recorded range.
  protected formatIdRange(range: { minImpactedId: number | null | undefined; maxImpactedId: number | null | undefined }): string {
    if (range.minImpactedId == null || range.maxImpactedId == null) return '—';
    return `${range.minImpactedId.toLocaleString()}–${range.maxImpactedId.toLocaleString()}`;
  }

  // Wall-clock duration between processing start and completion, humanized
  // ("42s", "1m 12s"). Null while Processing or for pre-framework batches.
  protected formatDuration(detail: { startedAt: string | Date | null; completedAt: string | Date | null }): string | null {
    if (!detail.startedAt || !detail.completedAt) return null;
    const seconds = (new Date(detail.completedAt).getTime() - new Date(detail.startedAt).getTime()) / 1000;
    if (!isFinite(seconds) || seconds < 0) return null;
    if (seconds < 60) return `${seconds.toFixed(seconds < 10 ? 1 : 0)}s`;
    const minutes = Math.floor(seconds / 60);
    return `${minutes}m ${Math.round(seconds - minutes * 60)}s`;
  }

  // Journal drill-down row label: the typed source-file row number; legacy
  // (pre-framework) rows have none, so fall back to their provenance string.
  protected rowDisplay(entry: ImportJournalEntryDto): string {
    return entry.rowNumber ? entry.rowNumber.toLocaleString() : entry.sourceKey;
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

  // Pretty-print a JSON block (row payloads, snapshots) for a <pre>. Falls
  // back to the raw string if parsing fails or it's empty.
  protected formatJson(raw: string | null | undefined): string {
    if (!raw) return '';
    try {
      const obj = JSON.parse(raw);
      return JSON.stringify(obj, null, 2);
    } catch {
      return raw;
    }
  }

  // Download a snapshot JSON as a file (audit hand-off: regulators get the exact
  // config copy without copy-pasting from the screen).
  protected downloadSnapshot(kind: 'mapping' | 'rules'): void {
    const detail = this.batchDetail();
    if (!detail) return;
    const raw = kind === 'mapping' ? detail.mappingSnapshotJson : detail.rulesSnapshotJson;
    if (!raw) return;
    const blob = new Blob([raw], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `batch-${detail.id}-${kind}-snapshot.json`;
    a.click();
    URL.revokeObjectURL(url);
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
    // Signal writes only: the constructor's effect() refetches on any filter
    // change — an explicit loadBatches() here would fire a duplicate request.
    this.batchesPageSize.set(value ?? 20);
    this.batchesPage.set(1);
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
      // The poll is a liveness check: it always fetches the unfiltered top of
      // the list, regardless of the user's page/filters. Results are merged
      // only when doing so cannot yank a filtered view out from under the user.
      () => this.dashboardClient.batches(1, this.batchesPageSize(), undefined, undefined, undefined, undefined, undefined),
      () => false, // isDone driven manually from onUpdate
      (result) => {
        const anyProcessing = (result.items ?? []).some(
          b => b.status === DashboardComponent.StatusProcessing
        );

        // Stop deciding FIRST: when nothing is Processing the poll has no
        // reason to run — even when filters are active. Doing this check
        // before the merge-guard return is what prevents "polls forever
        // while filtered".
        if (!anyProcessing && this.pollHandle) {
          this.pollHandle.stop();
          this.pollHandle = null;
        }

        // Only merge if the user is on page 1 and no filters are applied — otherwise
        // a poll would yank the visible list out from under them.
        if (this.batchesPage() !== 1 || this.hasActiveFilters()) return;
        this.batches.set(result.items ?? []);
        this.batchesTotalCount.set(result.totalCount);
        this.batchesLoading.set(false);
        this.loadStatistics();
      },
      (outcome) => {
        // The poller gave up (too many consecutive errors). Release the handle
        // so maybeRestartPoll can re-arm it on the next user-driven refresh —
        // leaving it set would silently disable auto-refresh forever.
        if (outcome.kind === 'error') {
          this.pollHandle = null;
        }
      },
      { intervalMs: 2000, maxConsecutiveErrors: 15 } // ~30s of failures before giving up
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
    this.resetJournalState();
    this.batchDetail.set(null);
    this.showMappingSnapshot.set(false);
    this.showRulesSnapshot.set(false);
    this.loadJournalSummary();
    this.loadBatchDetail();
  }

  private resetJournalState(): void {
    this.journalSummary.set(null);
    this.journalSummaryLoading.set(false);
    this.importedExpanded.set(false);
    this.importedPage.set(null);
    this.expandedGroups.set([]);
    this.groupPages.set({});
    this.journalShowPayload.set(false);
  }

  protected toggleMappingSnapshot(): void {
    this.showMappingSnapshot.update(v => !v);
  }

  protected toggleRulesSnapshot(): void {
    this.showRulesSnapshot.update(v => !v);
  }

  protected loadBatchDetail(): void {
    const batchId = this.selectedBatchId();
    if (batchId === null) return;
    this.batchDetailLoading.set(true);
    this.dashboardClient.detail(batchId).subscribe({
      next: detail => { this.batchDetail.set(detail); this.batchDetailLoading.set(false); },
      error: () => { this.batchDetail.set(null); this.batchDetailLoading.set(false); }
    });
  }

  protected loadJournalSummary(): void {
    const batchId = this.selectedBatchId();
    if (batchId === null) return;
    this.journalSummaryLoading.set(true);
    this.dashboardClient.summary(batchId).subscribe({
      next: summary => { this.journalSummary.set(summary); this.journalSummaryLoading.set(false); },
      error: () => { this.journalSummary.set(null); this.journalSummaryLoading.set(false); }
    });
  }

  protected toggleImportedRows(): void {
    if (this.importedExpanded()) {
      this.importedExpanded.set(false);
      return;
    }
    this.importedExpanded.set(true);
    if (!this.importedPage()) this.loadImportedRows(1);
  }

  protected loadImportedRows(page: number): void {
    const batchId = this.selectedBatchId();
    if (batchId === null) return;
    this.dashboardClient.journal(batchId, page, DashboardComponent.JournalBoxPageSize, DashboardComponent.RowImported, undefined)
      .subscribe({
        next: p => this.importedPage.set(p),
        error: () => this.importedPage.set(null),
      });
  }

  protected isGroupExpanded(errorCode: string): boolean {
    return this.expandedGroups().includes(errorCode);
  }

  protected toggleGroup(errorCode: string): void {
    if (this.isGroupExpanded(errorCode)) {
      this.expandedGroups.update(list => list.filter(c => c !== errorCode));
      return;
    }
    this.expandedGroups.update(list => [...list, errorCode]);
    // Load on first expand; re-expands reuse the loaded page.
    if (!(errorCode in this.groupPages())) this.loadGroupRows(errorCode, 1);
  }

  protected loadGroupRows(errorCode: string, page: number): void {
    const batchId = this.selectedBatchId();
    if (batchId === null) return;
    this.dashboardClient.journal(batchId, page, DashboardComponent.JournalBoxPageSize, DashboardComponent.RowRejected, errorCode)
      .subscribe({
        next: p => this.groupPages.update(map => ({ ...map, [errorCode]: p })),
        error: () => this.groupPages.update(map => ({ ...map, [errorCode]: null })),
      });
  }

  protected groupPage(errorCode: string, delta: number): void {
    const p = this.groupPages()[errorCode];
    if (!p) return;
    const next = p.page + delta;
    if (next < 1 || next > this.maxPage(p)) return;
    this.loadGroupRows(errorCode, next);
  }

  protected maxPage(p: ImportJournalPage): number {
    return Math.max(1, Math.ceil(p.totalCount / p.pageSize));
  }

  protected closeDetail(): void {
    this.selectedBatchId.set(null);
    this.resetJournalState();
    this.batchDetail.set(null);
    this.showMappingSnapshot.set(false);
    this.showRulesSnapshot.set(false);
  }

  private daysAgo(days: number): Date {
    const d = new Date();
    d.setDate(d.getDate() - days);
    d.setHours(0, 0, 0, 0);
    return d;
  }

  private formatDate(d: Date): string {
    // Local calendar date — toISOString() would show the UTC-shifted day
    // for users west of UTC (evening picks rendering as the next day).
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
  }
}
