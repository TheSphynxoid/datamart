import { EntityValidationRuleDto } from '../web-api-client';

const OPERATOR_SYMBOLS: Record<string, string> = {
  Equals: '==',
  NotEquals: '!=',
  GreaterThan: '>',
  GreaterThanOrEqual: '>=',
  LessThan: '<',
  LessThanOrEqual: '<=',
  Contains: 'containing',
  In: 'one of',
  NotIn: 'not one of',
};

export function formatFunction(fn: string | undefined, arg: number | undefined): string {
  switch (fn) {
    case 'Today': return 'today';
    case 'Now': return 'now';
    case 'TodayMinusYears': return `today - ${arg ?? '?'} years`;
    case 'TodayMinusDays': return `today - ${arg ?? '?'} days`;
    default: return fn ?? '';
  }
}

/**
 * One-line human rendering of a rule definition (the *what*, not the verdict).
 * Functions render unresolved ("today - 18 years"): this is the rule as
 * authored. Failure messages from the server carry the resolved boundary.
 */
export function formatRuleText(rule: EntityValidationRuleDto): string {
  const op = OPERATOR_SYMBOLS[rule.operator] ?? rule.operator;
  let rhs: string;
  if (rule.valueSource === 'Function') {
    rhs = formatFunction(rule.function, rule.functionArg);
  } else if (rule.operator === 'In' || rule.operator === 'NotIn') {
    rhs = `[${(rule.values ?? []).join(', ')}]`;
  } else {
    rhs = rule.value ?? '';
  }
  return `${rule.field} ${op} ${rhs}`;
}

/**
 * Plain-language gloss for the trickier shapes. Relative dates read
 * backwards ("dob <= today - 18 years" means "age 18 or older"), which is the
 * part users trip over — this states the intent outright. Null when the rule
 * needs no explanation. Shown under the formatted rule in the wizard list and
 * as a live preview in the admin form.
 */
export function explainRuleText(rule: EntityValidationRuleDto): string | null {
  if (rule.valueSource !== 'Function') {
    return null;
  }
  const arg = rule.functionArg ?? '?';
  switch (rule.function) {
    case 'TodayMinusYears':
      if (rule.operator === 'LessThanOrEqual') return `i.e. age ${arg} or older`;
      if (rule.operator === 'GreaterThanOrEqual') return `i.e. age ${arg} or younger`;
      return null;
    case 'TodayMinusDays':
      if (rule.operator === 'GreaterThanOrEqual') return `i.e. dated within the last ${arg} days`;
      return null;
    case 'Today':
    case 'Now':
      if (rule.operator === 'LessThanOrEqual') return 'i.e. not in the future';
      return null;
    default:
      return null;
  }
}
