import { Component, Input } from '@angular/core';
import { EntityValidationRuleDto } from '../web-api-client';
import { explainRuleText, formatRuleText } from './rule-format';

@Component({
  standalone: false,
  selector: 'app-rule-list',
  templateUrl: './rule-list.component.html',
  styleUrls: ['./rule-list.component.scss']
})
export class RuleListComponent {
  @Input() rules: EntityValidationRuleDto[] | null = null;
  @Input() loading = false;

  protected readonly formatRuleText = formatRuleText;
  protected readonly explainRuleText = explainRuleText;
}
