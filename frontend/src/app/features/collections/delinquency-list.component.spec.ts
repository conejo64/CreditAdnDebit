import { TestBed, ComponentFixture } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { DelinquencyListComponent } from './delinquency-list.component';
import { DelinquencyService, PagedResult, DelinquencyRecord } from './delinquency.service';

const mockRecord: DelinquencyRecord = {
  id: 'rec-1',
  accountId: 'acc-1',
  statementId: 'stmt-1',
  overdueAmount: 1200.50,
  daysInArrears: 45,
  bucket: 2,
  bucketLabel: '31-60 days',
  status: 'Active',
  createdOn: '2026-01-01T00:00:00Z',
  updatedOn: '2026-01-01T00:00:00Z',
  resolvedOn: null
};

const mockPagedResult: PagedResult<DelinquencyRecord> = {
  items: [mockRecord],
  totalCount: 1,
  page: 1,
  pageSize: 20,
  totalPages: 1
};

const emptyPagedResult: PagedResult<DelinquencyRecord> = {
  items: [],
  totalCount: 0,
  page: 1,
  pageSize: 20,
  totalPages: 0
};

describe('DelinquencyListComponent', () => {
  let fixture: ComponentFixture<DelinquencyListComponent>;
  let component: DelinquencyListComponent;
  let serviceSpy: jasmine.SpyObj<DelinquencyService>;

  beforeEach(async () => {
    serviceSpy = jasmine.createSpyObj('DelinquencyService', ['getDelinquencies']);
    serviceSpy.getDelinquencies.and.returnValue(of(mockPagedResult));

    await TestBed.configureTestingModule({
      imports: [DelinquencyListComponent, RouterTestingModule],
      providers: [
        { provide: DelinquencyService, useValue: serviceSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(DelinquencyListComponent);
    component = fixture.componentInstance;
  });

  // RED→GREEN: table rows from mock data
  it('should display a table row for each delinquency record', () => {
    fixture.detectChanges();
    const rows = fixture.nativeElement.querySelectorAll('[data-testid="delinquency-row"]');
    expect(rows.length).toBe(1);
  });

  // GREEN: bucket badge rendered with bucketLabel
  it('should display the bucketLabel in each row', () => {
    fixture.detectChanges();
    const badge = fixture.nativeElement.querySelector('[data-testid="bucket-badge"]');
    expect(badge).not.toBeNull();
    expect(badge.textContent.trim()).toBe('31-60 days');
  });

  // TRIANGULATE: empty state when no records
  it('should show empty state message when no records are returned', () => {
    serviceSpy.getDelinquencies.and.returnValue(of(emptyPagedResult));
    fixture.detectChanges();
    const rows = fixture.nativeElement.querySelectorAll('[data-testid="delinquency-row"]');
    expect(rows.length).toBe(0);
    const emptyMsg = fixture.nativeElement.querySelector('[data-testid="empty-state"]');
    expect(emptyMsg).not.toBeNull();
  });

  // TRIANGULATE: pagination — next page triggers service call
  it('should call getDelinquencies with page 2 when nextPage is called', () => {
    // Set up a multi-page scenario
    const multiPageResult: PagedResult<DelinquencyRecord> = { ...mockPagedResult, totalCount: 25, totalPages: 2 };
    serviceSpy.getDelinquencies.and.returnValue(of(multiPageResult));
    fixture.detectChanges();

    component.nextPage();
    fixture.detectChanges();

    expect(serviceSpy.getDelinquencies).toHaveBeenCalledWith(2, jasmine.any(Number), undefined);
  });

  // TRIANGULATE: bucket filter — changing filter reloads from page 1
  it('should reset to page 1 and reload when bucket filter changes', () => {
    fixture.detectChanges();
    component.currentPage = 2;
    component.onBucketChange(1);
    expect(serviceSpy.getDelinquencies).toHaveBeenCalledWith(1, jasmine.any(Number), 1);
  });

  // RED→GREEN: 403 must surface as error, NOT as empty state
  it('should show authorization error message when the API returns 403', () => {
    const forbiddenError = new HttpErrorResponse({ status: 403, statusText: 'Forbidden' });
    serviceSpy.getDelinquencies.and.returnValue(throwError(() => forbiddenError));
    fixture.detectChanges();
    const errorEl = fixture.nativeElement.querySelector('[data-testid="auth-error"]');
    expect(errorEl).not.toBeNull();
    expect(errorEl.textContent).toContain('403');
    // empty-state must NOT appear when there is an auth error
    const emptyEl = fixture.nativeElement.querySelector('[data-testid="empty-state"]');
    expect(emptyEl).toBeNull();
  });

  // TRIANGULATE: generic server error (500) also surfaces truthfully, not as empty state
  it('should show generic error message when the API returns a 500 error', () => {
    const serverError = new HttpErrorResponse({ status: 500, statusText: 'Internal Server Error' });
    serviceSpy.getDelinquencies.and.returnValue(throwError(() => serverError));
    fixture.detectChanges();
    const errorEl = fixture.nativeElement.querySelector('[data-testid="auth-error"]');
    expect(errorEl).not.toBeNull();
    // empty-state must NOT appear when there is a server error
    const emptyEl = fixture.nativeElement.querySelector('[data-testid="empty-state"]');
    expect(emptyEl).toBeNull();
  });

  // v77: View Details link present for each row
  it('should render a view-details link for each row', () => {
    fixture.detectChanges();
    const links = fixture.nativeElement.querySelectorAll('[data-testid="view-details-link"]');
    expect(links.length).toBe(1);
    expect(links[0].getAttribute('href')).toContain('rec-1');
  });
});
