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
});
