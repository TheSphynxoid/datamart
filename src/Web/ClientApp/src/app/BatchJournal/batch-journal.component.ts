import { Component, OnInit, signal, computed } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DashboardClient, BatchDetailDto, ImportJournalSummaryDto, ImportJournalPage } from '../web-api-client';
import { batchStatusBadge, journalRowDisplay, ImportRowStatusValues } from '../util/batch-view';
import { MappingSnapshotView, parseMappingSnapshot, parseRulesSnapshot, RuleSnapshotView } from './snapshot-view';

/// <summary>
/// Per-batch journal page (tier-1 record + grouped tier-2 drill-down). Lives on
/// its own route (/journal/:id) instead of an inline dashboard panel: the batch
/// list navigates here, so the dashboard stays a list surface and the journal
/// gets room to breathe.
/// Imported rows collapse into one box (row-by-row entries would be identical);
/// rejections group by stable error code — same issue, same box. Expanding a
/// box loads its rows paginated on demand; nothing ever loads all rows.
/// </summary>
@Component({
  standalone: false,
  selector: 'app-batch-journal',
  templateUrl: './batch-journal.component.html',
  styleUrls: ['./batch-journal.component.scss']
})
export class BatchJournalComponent implements OnInit {
  protected readonly batchId = signal<number | null>(null);
  protected readonly batchDetail = signal<BatchDetailDto | null>(null);
  protected readonly batchDetailLoading = signal(false);
  protected readonly batchNotFound = signal(false);

  protected readonly journalSummary = signal<ImportJournalSummaryDto | null>(null);
  protected readonly journalSummaryLoading = signal(false);
  protected readonly importedExpanded = signal(false);
  protected readonly importedPage = signal<ImportJournalPage | null>(null);
  protected readonly expandedGroups = signal<string[]>([]);
  // Rows per expanded group, keyed by the group's error code; null = load failed.
  protected readonly groupPages = signal<Record<string, ImportJournalPage | null>>({});

  protected readonly journalShowPayload = signal(false);
  protected readonly showMappingSnapshot = signal(false);
  protected readonly showRulesSnapshot = signal(false);
  // Structured views parse the snapshots into tables; "Raw" flips back to the
  // byte-exact JSON (and is the fallback when a snapshot doesn't parse).
  protected readonly showMappingRaw = signal(false);
  protected readonly showRulesRaw = signal(false);

  protected readonly mappingSnapshotView = computed(() =>
    parseMappingSnapshot(this.batchDetail()?.mappingSnapshotJson));
  protected readonly rulesSnapshotView = computed(() =>
    parseRulesSnapshot(this.batchDetail()?.rulesSnapshotJson));

  // Page size for the rows shown inside an expanded journal box.
  private static readonly JournalBoxPageSize = 20;

  constructor(
    private readonly route: ActivatedRoute,
    private readonly dashboardClient: DashboardClient
  ) {}

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    if (!Number.isInteger(id) || id <= 0) {
      this.batchNotFound.set(true);
      return;
    }
    this.batchId.set(id);
    this.loadBatchDetail();
    this.loadJournalSummary();
  }

  protected get statusBadge() {
    return batchStatusBadge;
  }

  protected rowDisplay(entry: { rowNumber?: number | null; sourceKey: string }): string {
    return journalRowDisplay(entry);
  }

  // Journal-progress banner: "Journal uploading" while the post-COPY journal
  // upload catches up, or the audit-gap notice when it never finished.
  protected journalProgressText(detail: BatchDetailDto): string | null {
    if (detail.journalIncomplete) {
      return `Journal incomplete: ${detail.journalRowsWritten.toLocaleString()} of ${detail.totalRows.toLocaleString()} rows uploaded. The remaining rows cannot be recovered (the source file was deleted).`;
    }
    if (detail.status === 1 /* Success */ && detail.journalRowsWritten < detail.totalRows) {
      return `Journal uploading: ${detail.journalRowsWritten.toLocaleString()} of ${detail.totalRows.toLocaleString()} rows.`;
    }
    return null;
  }

  protected formatIdRange(range: { minImpactedId: number | null | undefined; maxImpactedId: number | null | undefined }): string {
    if (range.minImpactedId == null || range.maxImpactedId == null) return '—';
    return `${range.minImpactedId.toLocaleString()}–${range.maxImpactedId.toLocaleString()}`;
  }

  protected formatDuration(detail: { startedAt: string | Date | null; completedAt: string | Date | null }): string | null {
    if (!detail.startedAt || !detail.completedAt) return null;
    const seconds = (new Date(detail.completedAt).getTime() - new Date(detail.startedAt).getTime()) / 1000;
    if (!isFinite(seconds) || seconds < 0) return null;
    if (seconds < 60) return `${seconds.toFixed(seconds < 10 ? 1 : 0)}s`;
    const minutes = Math.floor(seconds / 60);
    return `${minutes}m ${Math.round(seconds - minutes * 60)}s`;
  }

  protected formatJson(raw: string | null | undefined): string {
    if (!raw) return '';
    try {
      const obj = JSON.parse(raw);
      return JSON.stringify(obj, null, 2);
    } catch {
      return raw;
    }
  }

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

  private loadBatchDetail(): void {
    const batchId = this.batchId();
    if (batchId === null) return;
    this.batchDetailLoading.set(true);
    this.dashboardClient.detail(batchId).subscribe({
      next: detail => { this.batchDetail.set(detail); this.batchDetailLoading.set(false); },
      error: () => { this.batchDetail.set(null); this.batchDetailLoading.set(false); this.batchNotFound.set(true); }
    });
  }

  private loadJournalSummary(): void {
    const batchId = this.batchId();
    if (batchId === null) return;
    this.journalSummaryLoading.set(true);
    this.dashboardClient.summary(batchId).subscribe({
      next: summary => { this.journalSummary.set(summary); this.journalSummaryLoading.set(false); },
      error: () => { this.journalSummary.set(null); this.journalSummaryLoading.set(false); }
    });
  }

  protected refreshJournal(): void {
    this.resetJournalState();
    this.loadJournalSummary();
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

  protected toggleImportedRows(): void {
    if (this.importedExpanded()) {
      this.importedExpanded.set(false);
      return;
    }
    this.importedExpanded.set(true);
    if (!this.importedPage()) this.loadImportedRows(1);
  }

  protected loadImportedRows(page: number): void {
    const batchId = this.batchId();
    if (batchId === null) return;
    this.dashboardClient.journal(batchId, page, BatchJournalComponent.JournalBoxPageSize, ImportRowStatusValues.Imported, undefined)
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
    const batchId = this.batchId();
    if (batchId === null) return;
    this.dashboardClient.journal(batchId, page, BatchJournalComponent.JournalBoxPageSize, ImportRowStatusValues.Rejected, errorCode)
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
}
