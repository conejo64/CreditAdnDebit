import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { CardService } from './card.service';
import { environment } from '../../../../environments/environment';

describe('CardService — lifecycle endpoints (RED → GREEN)', () => {
  let service: CardService;
  let httpMock: HttpTestingController;

  const base = `${environment.apiUrl}/issuer/cards`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [CardService]
    });
    service = TestBed.inject(CardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  // ─── unblockCard ──────────────────────────────────────────────────────────

  // RED: fails until unblockCard method is added to CardService
  it('unblockCard() should POST to <baseUrl>/<id>/unblock', () => {
    const id = 'card-001';
    service.unblockCard(id).subscribe();

    const req = httpMock.expectOne(`${base}/${id}/unblock`);
    expect(req.request.method).toBe('POST');
    req.flush({});
  });

  // ─── cancelCard ──────────────────────────────────────────────────────────

  // RED: fails until cancelCard method is added to CardService
  it('cancelCard() should POST to <baseUrl>/<id>/cancel with reason body', () => {
    const id = 'card-002';
    service.cancelCard(id, 'client request').subscribe();

    const req = httpMock.expectOne(`${base}/${id}/cancel`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ reason: 'client request' });
    req.flush({});
  });

  it('cancelCard() without reason should still POST', () => {
    const id = 'card-003';
    service.cancelCard(id).subscribe();

    const req = httpMock.expectOne(`${base}/${id}/cancel`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ reason: undefined });
    req.flush({});
  });

  // ─── replaceCard ─────────────────────────────────────────────────────────

  // RED: fails until replaceCard method is added to CardService
  it('replaceCard() should POST to <baseUrl>/<id>/replace with reason body', () => {
    const id = 'card-004';
    service.replaceCard(id, 'damaged').subscribe();

    const req = httpMock.expectOne(`${base}/${id}/replace`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ reason: 'damaged' });
    req.flush({ newCardId: 'new-card-id' });
  });

  it('replaceCard() without reason should still POST', () => {
    const id = 'card-005';
    service.replaceCard(id).subscribe();

    const req = httpMock.expectOne(`${base}/${id}/replace`);
    expect(req.request.body).toEqual({ reason: undefined });
    req.flush({ newCardId: 'new-card-id' });
  });

  // ─── issueCard ───────────────────────────────────────────────────────────

  /**
   * CardVault's IssueCardRequest declares `string Bin`. The BIN selector binds its
   * value to CatalogBin.binStart, which the catalog API serialises as a JSON number
   * (BinRangeEntity.BinStart is an int). TypeScript's `bin: string` annotation is
   * erased at runtime, so a number reached the wire and System.Text.Json refused to
   * convert it, failing the whole request body with
   * "The JSON value could not be converted ... Path: $.bin" and a 400.
   *
   * The coercion belongs here, at the network boundary, so no caller can reintroduce it.
   */
  it('issueCard() should send bin as a JSON string', () => {
    service.issueCard('acc-1', '438108', '4381081234567890', '2912').subscribe();

    const req = httpMock.expectOne(`${base}/issue`);
    expect(req.request.method).toBe('POST');
    expect(typeof req.request.body.bin).toBe('string');
    expect(req.request.body).toEqual({
      accountId: 'acc-1',
      bin: '438108',
      pan: '4381081234567890',
      expiryYyMm: '2912'
    });
    req.flush({});
  });

  // RED before the fix: a numeric bin was serialised as a JSON number and rejected.
  it('issueCard() should stringify a numeric bin before sending it', () => {
    service.issueCard('acc-2', 438108 as unknown as string, '4381081234567890', '2912').subscribe();

    const req = httpMock.expectOne(`${base}/issue`);
    expect(typeof req.request.body.bin)
      .withContext('a numeric bin must not reach the wire as a JSON number')
      .toBe('string');
    expect(req.request.body.bin).toBe('438108');
    req.flush({});
  });
});
