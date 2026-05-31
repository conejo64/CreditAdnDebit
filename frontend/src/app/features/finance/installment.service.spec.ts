import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { InstallmentService, DeferPurchaseRequest } from './installment.service';
import { environment } from '../../../environments/environment';

describe('InstallmentService — URL contract (RED → GREEN)', () => {
  let service: InstallmentService;
  let httpMock: HttpTestingController;

  const base = `${environment.apiUrl}/billing`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [InstallmentService]
    });
    service = TestBed.inject(InstallmentService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  // RED: fails when baseUrl is `/api/billing` because apiUrl already ends with `/api`
  it('getPlans() must NOT produce a doubled /api/api/ segment', () => {
    const accountId = 'acc-001';

    service.getPlans(accountId).subscribe();

    const req = httpMock.expectOne(r => r.url.includes('/billing/'));
    expect(req.request.url).not.toContain('/api/api/');
    req.flush([]);
  });

  // RED: asserts the exact URL shape
  it('getPlans() should GET to <apiUrl>/billing/accounts/{accountId}/installments', () => {
    const accountId = 'acc-001';
    const expected = `${base}/accounts/${accountId}/installments`;

    service.getPlans(accountId).subscribe();

    const req = httpMock.expectOne(expected);
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  // RED: deferPurchase POST URL
  it('deferPurchase() should POST to <apiUrl>/billing/installments/defer', () => {
    const payload: DeferPurchaseRequest = {
      accountId: 'acc-001',
      ledgerEntryId: 'led-001',
      installments: 6,
      apr: 18
    };
    const expected = `${base}/installments/defer`;

    service.deferPurchase(payload).subscribe();

    const req = httpMock.expectOne(expected);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush({});
  });

  // TRIANGULATE: no apr field — still hits correct endpoint
  it('deferPurchase() without apr still POSTs to correct endpoint', () => {
    const payload: DeferPurchaseRequest = {
      accountId: 'acc-002',
      ledgerEntryId: 'led-002',
      installments: 12
    };

    service.deferPurchase(payload).subscribe();

    const req = httpMock.expectOne(`${base}/installments/defer`);
    expect(req.request.body.apr).toBeUndefined();
    req.flush({});
  });
});
