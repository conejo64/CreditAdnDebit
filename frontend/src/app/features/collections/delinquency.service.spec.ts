import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import {
  DelinquencyService, DelinquencyRecord, PagedResult,
  ContactAttempt, DelinquencyNote
} from './delinquency.service';
import { environment } from '../../../environments/environment';

describe('DelinquencyService', () => {
  let service: DelinquencyService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [DelinquencyService]
    });
    service = TestBed.inject(DelinquencyService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  // RED → GREEN: basic paginated call
  it('should call GET /api/collections/delinquencies with default page params', () => {
    const mockResponse: PagedResult<DelinquencyRecord> = {
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 20,
      totalPages: 0
    };

    service.getDelinquencies(1, 20).subscribe(result => {
      expect(result.page).toBe(1);
      expect(result.pageSize).toBe(20);
      expect(result.items).toEqual([]);
    });

    const req = httpMock.expectOne(r =>
      r.url === `${environment.apiUrl}/collections/delinquencies` &&
      r.params.get('page') === '1' &&
      r.params.get('pageSize') === '20'
    );
    expect(req.request.method).toBe('GET');
    req.flush(mockResponse);
  });

  // TRIANGULATE: with bucket filter
  it('should include bucket param when provided', () => {
    const mockResponse: PagedResult<DelinquencyRecord> = {
      items: [
        {
          id: 'id-1',
          accountId: 'acc-1',
          statementId: 'stmt-1',
          overdueAmount: 500,
          daysInArrears: 45,
          bucket: 2,
          bucketLabel: '31-60 days',
          status: 'Active',
          createdOn: '2026-01-01T00:00:00Z',
          updatedOn: '2026-01-01T00:00:00Z',
          resolvedOn: null
        }
      ],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      totalPages: 1
    };

    service.getDelinquencies(1, 20, 2).subscribe(result => {
      expect(result.totalCount).toBe(1);
      expect(result.items[0].bucket).toBe(2);
      expect(result.items[0].bucketLabel).toBe('31-60 days');
    });

    const req = httpMock.expectOne(r =>
      r.url === `${environment.apiUrl}/collections/delinquencies` &&
      r.params.get('bucket') === '2'
    );
    expect(req.request.method).toBe('GET');
    req.flush(mockResponse);
  });

  // TRIANGULATE: without bucket does NOT send bucket param
  it('should NOT include bucket param when not provided', () => {
    const mockResponse: PagedResult<DelinquencyRecord> = {
      items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0
    };

    service.getDelinquencies(1, 20).subscribe();

    const req = httpMock.expectOne(r =>
      r.url === `${environment.apiUrl}/collections/delinquencies`
    );
    expect(req.request.params.has('bucket')).toBeFalse();
    req.flush(mockResponse);
  });

  // ─────────────────────────────────────────────
  // v77 — Mutation methods (RED: written before methods exist)
  // ─────────────────────────────────────────────

  it('registerContactAttempt should POST to correct endpoint with payload', () => {
    const recordId = 'rec-001';
    service.registerContactAttempt(recordId, 'Phone', 'Contacted', 'Called OK').subscribe();

    const req = httpMock.expectOne(
      `${environment.apiUrl}/collections/delinquencies/${recordId}/contact-attempts`
    );
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ channel: 'Phone', outcome: 'Contacted', notes: 'Called OK' });
    req.flush({ id: 'new-attempt-id' });
  });

  it('registerContactAttempt should POST without notes when not provided', () => {
    const recordId = 'rec-002';
    service.registerContactAttempt(recordId, 'SMS', 'NoAnswer').subscribe();

    const req = httpMock.expectOne(
      `${environment.apiUrl}/collections/delinquencies/${recordId}/contact-attempts`
    );
    expect(req.request.body).toEqual({ channel: 'SMS', outcome: 'NoAnswer', notes: undefined });
    req.flush({ id: 'new-attempt-id' });
  });

  it('getContactAttempts should GET from correct endpoint', () => {
    const recordId = 'rec-003';
    const mockAttempts: ContactAttempt[] = [
      { id: 'a1', delinquencyRecordId: recordId, channel: 'Phone', outcome: 'Contacted', notes: null, attemptedBy: 'agent@bank.com', attemptedOn: '2026-05-01T10:00:00Z' }
    ];

    service.getContactAttempts(recordId).subscribe(items => {
      expect(items.length).toBe(1);
      expect(items[0].channel).toBe('Phone');
    });

    const req = httpMock.expectOne(
      `${environment.apiUrl}/collections/delinquencies/${recordId}/contact-attempts`
    );
    expect(req.request.method).toBe('GET');
    req.flush(mockAttempts);
  });

  it('addNote should POST to correct endpoint with content', () => {
    const recordId = 'rec-004';
    service.addNote(recordId, 'Escalated to supervisor.').subscribe();

    const req = httpMock.expectOne(
      `${environment.apiUrl}/collections/delinquencies/${recordId}/notes`
    );
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ content: 'Escalated to supervisor.' });
    req.flush({ id: 'new-note-id' });
  });

  it('getNotes should GET from correct endpoint', () => {
    const recordId = 'rec-005';
    const mockNotes: DelinquencyNote[] = [
      { id: 'n1', delinquencyRecordId: recordId, content: 'Note content.', createdBy: 'agent@bank.com', createdOn: '2026-05-01T11:00:00Z' }
    ];

    service.getNotes(recordId).subscribe(items => {
      expect(items.length).toBe(1);
      expect(items[0].content).toBe('Note content.');
    });

    const req = httpMock.expectOne(
      `${environment.apiUrl}/collections/delinquencies/${recordId}/notes`
    );
    expect(req.request.method).toBe('GET');
    req.flush(mockNotes);
  });
});

describe('DelinquencyService', () => {
  let service: DelinquencyService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [DelinquencyService]
    });
    service = TestBed.inject(DelinquencyService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  // RED → GREEN: basic paginated call
  it('should call GET /api/collections/delinquencies with default page params', () => {
    const mockResponse: PagedResult<DelinquencyRecord> = {
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 20,
      totalPages: 0
    };

    service.getDelinquencies(1, 20).subscribe(result => {
      expect(result.page).toBe(1);
      expect(result.pageSize).toBe(20);
      expect(result.items).toEqual([]);
    });

    const req = httpMock.expectOne(r =>
      r.url === `${environment.apiUrl}/collections/delinquencies` &&
      r.params.get('page') === '1' &&
      r.params.get('pageSize') === '20'
    );
    expect(req.request.method).toBe('GET');
    req.flush(mockResponse);
  });

  // TRIANGULATE: with bucket filter
  it('should include bucket param when provided', () => {
    const mockResponse: PagedResult<DelinquencyRecord> = {
      items: [
        {
          id: 'id-1',
          accountId: 'acc-1',
          statementId: 'stmt-1',
          overdueAmount: 500,
          daysInArrears: 45,
          bucket: 2,
          bucketLabel: '31-60 days',
          status: 'Active',
          createdOn: '2026-01-01T00:00:00Z',
          updatedOn: '2026-01-01T00:00:00Z',
          resolvedOn: null
        }
      ],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      totalPages: 1
    };

    service.getDelinquencies(1, 20, 2).subscribe(result => {
      expect(result.totalCount).toBe(1);
      expect(result.items[0].bucket).toBe(2);
      expect(result.items[0].bucketLabel).toBe('31-60 days');
    });

    const req = httpMock.expectOne(r =>
      r.url === `${environment.apiUrl}/collections/delinquencies` &&
      r.params.get('bucket') === '2'
    );
    expect(req.request.method).toBe('GET');
    req.flush(mockResponse);
  });

  // TRIANGULATE: without bucket does NOT send bucket param
  it('should NOT include bucket param when not provided', () => {
    const mockResponse: PagedResult<DelinquencyRecord> = {
      items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0
    };

    service.getDelinquencies(1, 20).subscribe();

    const req = httpMock.expectOne(r =>
      r.url === `${environment.apiUrl}/collections/delinquencies`
    );
    expect(req.request.params.has('bucket')).toBeFalse();
    req.flush(mockResponse);
  });
});
