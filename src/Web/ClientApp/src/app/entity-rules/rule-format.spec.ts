import { EntityValidationRuleDto } from '../web-api-client';
import { explainRuleText, formatFunction, formatRuleText } from './rule-format';

function makeRule(overrides: Partial<EntityValidationRuleDto>): EntityValidationRuleDto {
  return Object.assign(new EntityValidationRuleDto(), {
    id: 1,
    targetEntity: 'CustomerTransactions',
    field: 'CustomerDOB',
    operator: 'LessThanOrEqual',
    valueSource: 'Function',
    function: 'TodayMinusYears',
    functionArg: 18,
    values: [],
    isActive: true,
  }, overrides);
}

describe('formatRuleText', () => {
  it('renders a function rule unresolved', () => {
    expect(formatRuleText(makeRule({}))).toBe('CustomerDOB <= today - 18 years');
  });

  it('renders a constant comparison', () => {
    const rule = makeRule({
      field: 'TransactionAmount',
      operator: 'GreaterThanOrEqual',
      valueSource: 'Constant',
      value: '0',
      function: undefined,
      functionArg: undefined,
    });
    expect(formatRuleText(rule)).toBe('TransactionAmount >= 0');
  });

  it('renders a membership rule as a list', () => {
    const rule = makeRule({
      field: 'CustGender',
      operator: 'In',
      valueSource: 'Constant',
      values: ['M', 'F'],
      function: undefined,
      functionArg: undefined,
    });
    expect(formatRuleText(rule)).toBe('CustGender one of [M, F]');
  });

  it('falls back to the raw operator for unknown values', () => {
    const rule = makeRule({ operator: 'SomethingNew', valueSource: 'Constant', value: 'x' });
    expect(formatRuleText(rule)).toBe('CustomerDOB SomethingNew x');
  });
});

describe('formatFunction', () => {  it('renders Today and Now', () => {
    expect(formatFunction('Today', undefined)).toBe('today');
    expect(formatFunction('Now', undefined)).toBe('now');
  });

  it('renders relative functions with their argument', () => {
    expect(formatFunction('TodayMinusYears', 18)).toBe('today - 18 years');
    expect(formatFunction('TodayMinusDays', 30)).toBe('today - 30 days');
  });

  it('renders unknown functions verbatim', () => {
    expect(formatFunction('Whatever', undefined)).toBe('Whatever');
  });
});

describe('explainRuleText', () => {
  it('glosses the age pattern both ways round', () => {
    expect(explainRuleText(makeRule({}))).toBe('i.e. age 18 or older');
    expect(explainRuleText(makeRule({ operator: 'GreaterThanOrEqual' }))).toBe('i.e. age 18 or younger');
  });

  it('glosses recency and not-in-future', () => {
    const recent = makeRule({
      field: 'TransactionDate',
      operator: 'GreaterThanOrEqual',
      function: 'TodayMinusDays',
      functionArg: 30,
    });
    expect(explainRuleText(recent)).toBe('i.e. dated within the last 30 days');

    const noFuture = makeRule({
      field: 'TransactionDate',
      operator: 'LessThanOrEqual',
      function: 'Today',
      functionArg: undefined,
    });
    expect(explainRuleText(noFuture)).toBe('i.e. not in the future');
  });

  it('returns null for constants and unglossable shapes', () => {
    const constant = makeRule({
      field: 'TransactionAmount',
      operator: 'GreaterThanOrEqual',
      valueSource: 'Constant',
      value: '0',
      function: undefined,
      functionArg: undefined,
    });
    expect(explainRuleText(constant)).toBeNull();

    const odd = makeRule({ operator: 'Equals' });
    expect(explainRuleText(odd)).toBeNull();
  });
});
