import { Component, OnInit, signal, computed } from '@angular/core';
import {
  EntitySchemaClient, EntitySchemaDto, EntityPropertyDto,
  EntityRuleEndpointsClient, EntityValidationRuleDto,
  CreateEntityRuleCommand, UpdateEntityRuleCommand,
} from '../web-api-client';
import { explainRuleText, formatRuleText } from './rule-format';

type FieldKind = 'text' | 'numeric' | 'date' | 'boolean' | 'unsupported';

interface RuleDraft {
  id: number | null;
  field: string | null;
  operator: string;
  valueSource: string;
  value: string;
  valuesText: string;
  function: string | null;
  functionArg: string;
  message: string;
  isActive: boolean;
}

@Component({
  standalone: false,
  selector: 'app-entity-rules',
  templateUrl: './entity-rules.component.html',
  styleUrls: ['./entity-rules.component.scss']
})
export class EntityRulesComponent implements OnInit {
  protected readonly entitySchemas = signal<EntitySchemaDto[]>([]);
  protected readonly selectedEntity = signal<string | null>(null);
  protected readonly rules = signal<EntityValidationRuleDto[]>([]);
  protected readonly loadingRules = signal(false);
  protected readonly rulesError = signal<string | null>(null);
  protected readonly showInactive = signal(true);

  protected readonly draft = signal<RuleDraft | null>(null);
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);

  protected readonly quickTemplate = signal('custom');
  protected readonly quickN = signal('');
  protected readonly quickValues = signal('');
  protected readonly quickError = signal<string | null>(null);
  // Exclusive form mode: quick starters or full manual details, never both.
  // New rules start in quick; edits open in custom (the rule already exists
  // as concrete values, so there is nothing to "start" from).
  protected readonly quickMode = signal(true);

  protected readonly operatorOptions: Record<Exclude<FieldKind, 'unsupported'>, { value: string; label: string }[]> = {
    text: [
      { value: 'Equals', label: 'Equals (==)' },
      { value: 'NotEquals', label: 'Not equals (!=)' },
      { value: 'Contains', label: 'Contains' },
      { value: 'In', label: 'Is one of' },
      { value: 'NotIn', label: 'Is not one of' },
    ],
    numeric: [
      { value: 'Equals', label: 'Equals (==)' },
      { value: 'NotEquals', label: 'Not equals (!=)' },
      { value: 'GreaterThan', label: 'Greater than (>)' },
      { value: 'GreaterThanOrEqual', label: 'At least (>=)' },
      { value: 'LessThan', label: 'Less than (<)' },
      { value: 'LessThanOrEqual', label: 'At most (<=)' },
      { value: 'In', label: 'Is one of' },
      { value: 'NotIn', label: 'Is not one of' },
    ],
    date: [
      { value: 'Equals', label: 'Equals (==)' },
      { value: 'NotEquals', label: 'Not equals (!=)' },
      { value: 'GreaterThan', label: 'After (>)' },
      { value: 'GreaterThanOrEqual', label: 'On or after (>=)' },
      { value: 'LessThan', label: 'Before (<)' },
      { value: 'LessThanOrEqual', label: 'On or before (<=)' },
      { value: 'In', label: 'Is one of' },
      { value: 'NotIn', label: 'Is not one of' },
    ],
    boolean: [
      { value: 'Equals', label: 'Equals (==)' },
      { value: 'NotEquals', label: 'Not equals (!=)' },
      { value: 'In', label: 'Is one of' },
      { value: 'NotIn', label: 'Is not one of' },
    ],
  };

  protected readonly sourceOptions = [
    { value: 'Constant', label: 'Fixed value' },
    { value: 'Function', label: 'Relative to import time' },
  ];

  protected readonly functionOptions = [
    { value: 'Today', label: 'Today' },
    { value: 'Now', label: 'Now' },
    { value: 'TodayMinusYears', label: 'Today minus N years' },
    { value: 'TodayMinusDays', label: 'Today minus N days' },
  ];

  protected readonly entityNames = computed(() =>
    this.entitySchemas().map(e => e.name!).filter((n): n is string => !!n).sort());

  protected readonly fieldOptions = computed<EntityPropertyDto[]>(() => {
    const entity = this.entitySchemas().find(e => e.name === this.selectedEntity());
    const props = entity?.properties ?? [];
    // System fields (audit, batch linkage) and primary keys cannot carry rules.
    return props.filter(p => !p.isExcluded && !p.isPrimary);
  });

  protected readonly draftKind = computed<FieldKind>(() => {
    const field = this.draft()?.field;
    if (!field) return 'unsupported';
    return this.kindOf(this.fieldOptions().find(p => p.name === field)?.dataType);
  });

  protected readonly draftOperators = computed(() => {
    const kind = this.draftKind();
    return kind === 'unsupported' ? [] : this.operatorOptions[kind];
  });

  protected readonly isMembership = computed(() => {
    const op = this.draft()?.operator;
    return op === 'In' || op === 'NotIn';
  });

  protected readonly isFunctionSource = computed(() => this.draft()?.valueSource === 'Function');

  protected readonly needsFunctionArg = computed(() => {
    const fn = this.draft()?.function;
    return fn === 'TodayMinusYears' || fn === 'TodayMinusDays';
  });

  protected readonly canSave = computed(() => {
    const d = this.draft();
    return !!this.selectedEntity() && !!d?.field && this.draftOperators().length > 0 && !this.saving();
  });

  protected readonly ruleText = (row: EntityValidationRuleDto): string => formatRuleText(row);
  protected readonly activeText = (row: EntityValidationRuleDto): string => row.isActive ? 'Yes' : 'No';

  /** Intent-based starters ("age at least N") that fill the raw fields below.
   *  One shot: applying writes into the draft, which stays freely editable. */
  protected readonly quickTemplates = computed(() => {
    const all = [
      { value: 'custom', label: 'Custom (fill the fields below)', needsN: false, nLabel: '' },
      { value: 'age-min', label: 'Age at least N years', needsN: true, nLabel: 'Years' },
      { value: 'age-max', label: 'Age at most N years', needsN: true, nLabel: 'Years' },
      { value: 'recent-days', label: 'Dated within the last N days', needsN: true, nLabel: 'Days' },
      { value: 'not-future', label: 'Not in the future', needsN: false, nLabel: '' },
      { value: 'one-of', label: 'Is one of a list of values', needsN: false, nLabel: '' },
      { value: 'min-value', label: 'Number at least N', needsN: true, nLabel: 'Number' },
      { value: 'max-value', label: 'Number at most N', needsN: true, nLabel: 'Number' },
    ];
    const allowed: Record<FieldKind, string[]> = {
      date: ['custom', 'age-min', 'age-max', 'recent-days', 'not-future'],
      text: ['custom', 'one-of'],
      numeric: ['custom', 'min-value', 'max-value'],
      boolean: ['custom'],
      unsupported: ['custom'],
    };
    return all.filter(t => allowed[this.draftKind()].includes(t.value));
  });

  protected readonly quickNeedsN = computed(() =>
    this.quickTemplates().find(t => t.value === this.quickTemplate())?.needsN ?? false);

  protected readonly quickNLabel = computed(() =>
    this.quickTemplates().find(t => t.value === this.quickTemplate())?.nLabel ?? '');

  protected readonly quickNeedsValues = computed(() => this.quickTemplate() === 'one-of');

  /** Live plain-language preview of the draft (formatted rule + intent gloss). */
  protected readonly previewText = computed(() => {
    const dto = this.previewDto();
    return dto ? formatRuleText(dto) : '';
  });

  protected readonly previewExplanation = computed(() => {
    const dto = this.previewDto();
    return dto ? explainRuleText(dto) : null;
  });

  constructor(
    private entitySchemaClient: EntitySchemaClient,
    private entityRuleClient: EntityRuleEndpointsClient,
  ) {}

  ngOnInit(): void {
    this.entitySchemaClient.getEntitySchemas().subscribe({
      next: result => this.entitySchemas.set(result ?? []),
      error: error => {
        this.rulesError.set('Could not load entities.');
        console.error(error);
      }
    });
  }

  protected onEntitySelected(name: string | null): void {
    this.selectedEntity.set(name);
    this.draft.set(null);
    this.loadRules();
  }

  protected onShowInactiveChanged(value: boolean): void {
    this.showInactive.set(value);
    this.loadRules();
  }

  protected newRule(): void {
    const first = this.fieldOptions()[0];
    if (!first?.name) return;
    this.saveError.set(null);
    this.resetQuick();
    this.quickMode.set(true);
    this.draft.set({
      id: null,
      field: first.name,
      operator: 'Equals',
      valueSource: 'Constant',
      value: '',
      valuesText: '',
      function: null,
      functionArg: '',
      message: '',
      isActive: true,
    });
  }

  protected editRule(row: EntityValidationRuleDto): void {
    this.saveError.set(null);
    this.resetQuick();
    this.quickMode.set(false);
    this.draft.set({
      id: row.id,
      field: row.field,
      operator: row.operator,
      valueSource: row.valueSource,
      value: row.value ?? '',
      valuesText: (row.values ?? []).join(', '),
      function: row.function ?? null,
      functionArg: row.functionArg?.toString() ?? '',
      message: row.message ?? '',
      isActive: row.isActive,
    });
  }

  protected cancelEdit(): void {
    this.draft.set(null);
    this.saveError.set(null);
  }

  protected patch(p: Partial<RuleDraft>): void {
    const d = this.draft();
    if (d) this.draft.set({ ...d, ...p });
  }

  private resetQuick(): void {
    this.quickTemplate.set('custom');
    this.quickN.set('');
    this.quickValues.set('');
    this.quickError.set(null);
  }

  protected onFieldChanged(field: string | null): void {
    const d = this.draft();
    if (!d) return;
    const kind = field ? this.kindOf(this.fieldOptions().find(p => p.name === field)?.dataType) : 'unsupported';
    const validOps = kind === 'unsupported' ? [] : this.operatorOptions[kind].map(o => o.value);
    const operator = validOps.includes(d.operator) ? d.operator : (validOps[0] ?? 'Equals');
    const valueSource = kind === 'date' ? d.valueSource : 'Constant';
    this.draft.set({ ...d, field, operator, valueSource, function: valueSource === 'Function' ? d.function : null });
    this.resetQuick();
  }

  protected onSourceChanged(source: string): void {
    const d = this.draft();
    if (!d) return;
    this.draft.set({ ...d, valueSource: source, function: source === 'Function' ? (d.function ?? 'Today') : null });
  }

  protected applyQuickTemplate(): void {
    const t = this.quickTemplate();
    if (t === 'custom') return;
    this.quickError.set(null);

    let n = '';
    if (this.quickNeedsN()) {
      const parsed = parseInt(this.quickN(), 10);
      if (isNaN(parsed) || parsed < 0) {
        this.quickError.set('Enter a whole number (0 or more).');
        return;
      }
      n = String(parsed);
    }
    switch (t) {
      case 'age-min':
        this.patch({ operator: 'LessThanOrEqual', valueSource: 'Function', function: 'TodayMinusYears', functionArg: n, value: '', valuesText: '' });
        break;
      case 'age-max':
        this.patch({ operator: 'GreaterThanOrEqual', valueSource: 'Function', function: 'TodayMinusYears', functionArg: n, value: '', valuesText: '' });
        break;
      case 'recent-days':
        this.patch({ operator: 'GreaterThanOrEqual', valueSource: 'Function', function: 'TodayMinusDays', functionArg: n, value: '', valuesText: '' });
        break;
      case 'not-future':
        this.patch({ operator: 'LessThanOrEqual', valueSource: 'Function', function: 'Today', functionArg: '', value: '', valuesText: '' });
        break;
      case 'one-of': {
        const items = this.quickValues().split(',').map(s => s.trim()).filter(s => s !== '');
        if (items.length === 0) {
          this.quickError.set('Enter at least one value (separate several with commas).');
          return;
        }
        this.patch({ operator: 'In', valueSource: 'Constant', function: null, value: '', valuesText: items.join(', ') });
        break;
      }
      case 'min-value':
        this.patch({ operator: 'GreaterThanOrEqual', valueSource: 'Constant', function: null, value: n, valuesText: '' });
        break;
      case 'max-value':
        this.patch({ operator: 'LessThanOrEqual', valueSource: 'Constant', function: null, value: n, valuesText: '' });
        break;
    }
    this.resetQuick();
    // Flip to the details for review: quick feeds custom, it never sits
    // alongside it, so the filled values are visible before saving.
    this.quickMode.set(false);
  }

  private previewDto(): EntityValidationRuleDto | null {
    const d = this.draft();
    if (!d?.field) return null;
    return Object.assign(new EntityValidationRuleDto(), {
      id: d.id ?? 0,
      targetEntity: this.selectedEntity() ?? '',
      field: d.field,
      operator: d.operator,
      valueSource: d.valueSource,
      value: d.value || undefined,
      values: d.valuesText.split(',').map(s => s.trim()).filter(s => s !== ''),
      function: d.function ?? undefined,
      functionArg: d.function === 'TodayMinusYears' || d.function === 'TodayMinusDays'
        ? (parseInt(d.functionArg, 10) || undefined)
        : undefined,
      message: d.message || undefined,
      isActive: d.isActive,
    });
  }

  protected saveRule(): void {
    const d = this.draft();
    const entity = this.selectedEntity();
    if (!d || !entity || !d.field) return;

    const membership = d.operator === 'In' || d.operator === 'NotIn';
    const isFunction = d.valueSource === 'Function';
    const values = membership
      ? d.valuesText.split(',').map(s => s.trim()).filter(s => s !== '')
      : [];
    const value = !isFunction && !membership && d.value.trim() !== '' ? d.value.trim() : undefined;

    let functionArg: number | undefined;
    if (isFunction && (d.function === 'TodayMinusYears' || d.function === 'TodayMinusDays')) {
      const parsed = parseInt(d.functionArg, 10);
      if (isNaN(parsed)) {
        this.saveError.set('The function argument must be a whole number (e.g. 18).');
        return;
      }
      functionArg = parsed;
    }

    this.saving.set(true);
    this.saveError.set(null);

    const finish = { next: () => this.afterSave(), error: (e: unknown) => this.onSaveError(e) };
    if (d.id === null) {
      this.entityRuleClient.createEntityRule(new CreateEntityRuleCommand({
        targetEntity: entity,
        field: d.field,
        operator: d.operator,
        valueSource: d.valueSource,
        value,
        values,
        function: isFunction ? (d.function ?? undefined) : undefined,
        functionArg,
        message: d.message.trim() || undefined,
      })).subscribe(finish);
    } else {
      this.entityRuleClient.updateEntityRule(d.id, new UpdateEntityRuleCommand({
        id: d.id,
        targetEntity: entity,
        field: d.field,
        operator: d.operator,
        valueSource: d.valueSource,
        value,
        values,
        function: isFunction ? (d.function ?? undefined) : undefined,
        functionArg,
        message: d.message.trim() || undefined,
        isActive: d.isActive,
      })).subscribe(finish);
    }
  }

  private afterSave(): void {
    this.saving.set(false);
    this.draft.set(null);
    this.loadRules();
  }

  private onSaveError(error: unknown): void {
    this.saving.set(false);
    this.saveError.set(this.describeError(error));
    console.error('Failed to save rule', error);
  }

  private describeError(error: unknown): string {
    const response = (error as { response?: string })?.response;
    if (typeof response === 'string' && response.length > 0) {
      try {
        const body = JSON.parse(response) as { title?: string; errors?: Record<string, string[]> };
        const first = body.errors ? Object.values(body.errors).flat()[0] : undefined;
        if (first ?? body.title) return (first ?? body.title) as string;
      } catch {
        // Fall through to the generic message.
      }
    }
    return 'Failed to save rule.';
  }

  private loadRules(): void {
    const entity = this.selectedEntity();
    if (!entity) {
      this.rules.set([]);
      return;
    }
    this.loadingRules.set(true);
    this.rulesError.set(null);
    this.entityRuleClient.getEntityRules(entity, !this.showInactive()).subscribe({
      next: rules => {
        this.rules.set(rules ?? []);
        this.loadingRules.set(false);
      },
      error: error => {
        this.rules.set([]);
        this.loadingRules.set(false);
        this.rulesError.set('Could not load rules.');
        console.error(error);
      }
    });
  }

  private kindOf(dataType: string | undefined): FieldKind {
    switch (dataType) {
      case 'DateTime':
      case 'DateTimeOffset':
      case 'DateOnly':
        return 'date';
      case 'Decimal':
      case 'Double':
      case 'Single':
      case 'Int32':
      case 'Int64':
      case 'Int16':
        return 'numeric';
      case 'Boolean':
        return 'boolean';
      case 'TimeOnly':
        return 'unsupported';
      default:
        return 'text';
    }
  }
}
