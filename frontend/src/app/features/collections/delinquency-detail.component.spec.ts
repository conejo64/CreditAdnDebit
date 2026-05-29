import { TestBed, ComponentFixture } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { DelinquencyDetailComponent } from './delinquency-detail.component';
import { DelinquencyService, DelinquencyRecord, PagedResult, ContactAttempt, DelinquencyNote } from './delinquency.service';
import { RouterTestingModule } from '@angular/router/testing';

const mockRecord: DelinquencyRecord = {
  id: 'rec-detail-1',
  accountId: 'acc-111',
  statementId: 'stmt-111',
  overdueAmount: 900,
  daysInArrears: 20,
  bucket: 1,
  bucketLabel: '1-30 days',
  status: 'Active',
  createdOn: '2026-05-01T00:00:00Z',
  updatedOn: '2026-05-01T00:00:00Z',
  resolvedOn: null,
};

const mockPagedResult: PagedResult<DelinquencyRecord> = {
  items: [mockRecord],
  totalCount: 1,
  page: 1,
  pageSize: 200,
  totalPages: 1,
};

const mockAttempts: ContactAttempt[] = [
  { id: 'a1', delinquencyRecordId: 'rec-detail-1', channel: 'Phone', outcome: 'Contacted', notes: null, attemptedBy: 'agent@bank.com', attemptedOn: '2026-05-01T10:00:00Z' },
];

const mockNotes: DelinquencyNote[] = [
  { id: 'n1', delinquencyRecordId: 'rec-detail-1', content: 'Internal note.', createdBy: 'agent@bank.com', createdOn: '2026-05-01T11:00:00Z' },
];

describe('DelinquencyDetailComponent', () => {
  let fixture: ComponentFixture<DelinquencyDetailComponent>;
  let component: DelinquencyDetailComponent;
  let serviceSpy: jasmine.SpyObj<DelinquencyService>;

  beforeEach(async () => {
    serviceSpy = jasmine.createSpyObj('DelinquencyService', [
      'getDelinquencies', 'getContactAttempts', 'getNotes', 'registerContactAttempt', 'addNote'
    ]);
    serviceSpy.getDelinquencies.and.returnValue(of(mockPagedResult));
    serviceSpy.getContactAttempts.and.returnValue(of(mockAttempts));
    serviceSpy.getNotes.and.returnValue(of(mockNotes));

    await TestBed.configureTestingModule({
      imports: [DelinquencyDetailComponent, RouterTestingModule],
      providers: [
        { provide: DelinquencyService, useValue: serviceSpy },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'rec-detail-1' } } } },
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(DelinquencyDetailComponent);
    component = fixture.componentInstance;
  });

  it('should load the record by ID on init', () => {
    fixture.detectChanges();
    expect(component.record).toBeTruthy();
    expect(component.record?.id).toBe('rec-detail-1');
  });

  it('should display the record overview section', () => {
    fixture.detectChanges();
    const overview = fixture.nativeElement.querySelector('[data-testid="record-overview"]');
    expect(overview).not.toBeNull();
    expect(overview.textContent).toContain('acc-111');
  });

  it('should display three navigation tabs', () => {
    fixture.detectChanges();
    const tabs = fixture.nativeElement.querySelector('[data-testid="tabs"]');
    expect(tabs).not.toBeNull();
    const buttons = tabs.querySelectorAll('button');
    expect(buttons.length).toBe(2); // contacts + notes
  });

  it('should load contact attempts on init', () => {
    fixture.detectChanges();
    expect(serviceSpy.getContactAttempts).toHaveBeenCalledWith('rec-detail-1');
    expect(component.contactAttempts.length).toBe(1);
  });

  it('should load notes on init', () => {
    fixture.detectChanges();
    expect(serviceSpy.getNotes).toHaveBeenCalledWith('rec-detail-1');
    expect(component.notes.length).toBe(1);
  });

  it('should switch to notes tab when Notes button is clicked', () => {
    fixture.detectChanges();
    component.setTab('notes');
    fixture.detectChanges();
    const notesTab = fixture.nativeElement.querySelector('[data-testid="tab-notes"]');
    expect(notesTab).not.toBeNull();
  });
});
