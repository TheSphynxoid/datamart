import { TestBed, ComponentFixture } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { of } from 'rxjs';
import { ExcelMapComponent } from './excelmap.component';
import { EntitySchemaClient, MappingEndpointsClient, ImportClient, EntityRuleEndpointsClient, EntitySchemaDto, EntityPropertyDto, MappingDto } from '../web-api-client';

// Minimal DTO builders: only the fields the component reads.
function prop(name: string, dataType = 'String'): EntityPropertyDto {
  return {
    name,
    dataType,
    isNullable: true,
    isPrimary: false,
    isForeignKey: false,
    isUnique: false,
    isExcluded: false,
  } as unknown as EntityPropertyDto;
}

function schema(name: string, props: EntityPropertyDto[]): EntitySchemaDto {
  return { name, properties: props } as unknown as EntitySchemaDto;
}

const txSchema = schema('CustomerTransactions', [
  prop('TransactionId', 'Int32'), prop('Amount', 'Decimal'), prop('BookedAt', 'DateTime'),
]);
const caSchema = schema('ClientAccounts', [
  prop('AccountNumber'), prop('ClientName'), prop('Balance', 'Decimal'),
]);
const caFieldNames = caSchema.properties!.map(p => p.name!);

async function flush(fixture: ComponentFixture<ExcelMapComponent>): Promise<void> {
  // Effects are scheduled: settle microtasks, then run change detection twice
  // (once to flush effects, once to re-render from them).
  await fixture.whenStable();
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
}

describe('ExcelMapComponent — wizard navigation & step invalidation', () => {
  let fixture: ComponentFixture<ExcelMapComponent>;
  let component: ExcelMapComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ExcelMapComponent],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        { provide: EntitySchemaClient, useValue: { getEntitySchemas: () => of([]) } },
        { provide: MappingEndpointsClient, useValue: { getMappings: () => of([] as MappingDto[]), createMapping: () => of(1) } },
        { provide: ImportClient, useValue: {} },
        { provide: EntityRuleEndpointsClient, useValue: { getEntityRules: () => of([]) } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ExcelMapComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  // Commits entity + file state the way Confirm does, without the file input.
  function commitEntity(s: EntitySchemaDto): void {
    component['entitySchemas'].set([txSchema, caSchema]);
    component['draftEntity'].set(s);
    component['confirmEntity']();
  }

  function commitFakeFile(): void {
    const file = new File([], 'fake.xlsx');
    component['draftFile'].set(file);
    // The worker parse completes BEFORE the user confirms (preview is visible
    // in the upload panel), so the columns exist when confirmFile runs.
    component['excelColumns'].set(['TransactionId', 'Amount', 'BookedAt']);
    component['previewRows'].set([{ TransactionId: 1, Amount: 10, BookedAt: '2024-01-01' }]);
    component['confirmFile']();
  }

  it('map step auto-matches against the FIRST selected entity', async () => {
    commitEntity(txSchema);
    commitFakeFile();

    component['navigateTo'](2); // open Map columns
    await flush(fixture);

    const mapping = component['mapping']();
    expect(mapping).not.toBeNull();
    expect(mapping!.map(m => m.dbField).filter(Boolean).length).toBeGreaterThan(0);
    expect(mapping!.every(m => m.dbField === null || txSchema.properties!.some(p => p.name === m.dbField))).toBeTrue();
  });

  it('changing the entity via stepper navigation re-matches the map step to the NEW entity', async () => {
    // Wizard ran through Map columns once with CustomerTransactions.
    commitEntity(txSchema);
    commitFakeFile();
    component['navigateTo'](2);
    await flush(fixture);
    component['confirmMapping']();
    await flush(fixture);
    expect(component['mapping']()).not.toBeNull();

    // Back to step 1, pick a different entity, then click "Map columns" in the stepper.
    component['navigateTo'](0);
    await flush(fixture);
    component['onEntityChanged'](caSchema);
    component['onStepSelected']({ component: { option: (n: string) => (n === 'selectedIndex' ? 2 : -1) } });
    await flush(fixture);

    expect(component['selectedEntity']()!.name).toBe('ClientAccounts');
    const mapping = component['mapping']();
    expect(mapping).not.toBeNull();
    // Every mapped field must belong to the NEWLY selected entity.
    expect(mapping!.every(m => m.dbField === null || caFieldNames.includes(m.dbField!))).toBeTrue();
  });

  it('changing the entity via Confirm re-matches the map step to the NEW entity', async () => {
    commitEntity(txSchema);
    commitFakeFile();
    component['navigateTo'](2);
    await flush(fixture);
    component['confirmMapping']();
    await flush(fixture);

    component['navigateTo'](0);
    await flush(fixture);
    component['onEntityChanged'](caSchema);
    component['confirmEntity'](); // Confirm button path
    await flush(fixture);
    component['navigateTo'](2); // user walks back to Map columns
    await flush(fixture);

    expect(component['selectedEntity']()!.name).toBe('ClientAccounts');
    const mapping = component['mapping']();
    expect(mapping).not.toBeNull();
    expect(mapping!.every(m => m.dbField === null || caFieldNames.includes(m.dbField!))).toBeTrue();
  });
});
