import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { environment } from '../../environments/environment';

/**
 * SEC-03 (task 4.16): outgoing API requests must carry withCredentials: true (so the
 * HttpOnly cv_at/cv_rt cookies ride along) and must NOT carry an Authorization header
 * sourced from storage. On 401 the retry flow calls /auth/refresh with withCredentials
 * and an empty body (the cookie carries the refresh token), then retries the original
 * request. RED before the interceptor drops attachBearerToken and sets withCredentials.
 */
describe('authInterceptor — cookie-based session (SEC-03)', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([])
      ]
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should set withCredentials=true on API requests', () => {
    http.get(`${environment.apiUrl}/auth/me`).subscribe();

    const req = httpMock.expectOne(`${environment.apiUrl}/auth/me`);
    expect(req.request.withCredentials).toBeTrue();
    req.flush({});
  });

  it('should NOT attach an Authorization header sourced from storage', () => {
    http.get(`${environment.apiUrl}/auth/me`).subscribe();

    const req = httpMock.expectOne(`${environment.apiUrl}/auth/me`);
    expect(req.request.headers.has('Authorization')).toBeFalse();
    req.flush({});
  });

  it('on 401 should call /auth/refresh with withCredentials and no body token, then retry the original request', () => {
    http.get(`${environment.apiUrl}/protected`).subscribe();

    const first = httpMock.expectOne(`${environment.apiUrl}/protected`);
    first.flush(null, { status: 401, statusText: 'Unauthorized' });

    const refreshReq = httpMock.expectOne(`${environment.apiUrl}/auth/refresh`);
    expect(refreshReq.request.withCredentials).toBeTrue();
    expect(refreshReq.request.body).toEqual({});
    refreshReq.flush({ mfaRequired: false, message: 'OK', user: null });

    const retry = httpMock.expectOne(`${environment.apiUrl}/protected`);
    expect(retry.request.withCredentials).toBeTrue();
    retry.flush({});
  });

  // G1-T1: a rate-limited response is not an identity failure. Neither a 429 on
  // /auth/login nor a 429 on a protected endpoint may trigger the refresh/logout flow.
  it('on 429 from /auth/login should propagate the error without calling /auth/refresh or /auth/logout', (done) => {
    http.post(`${environment.apiUrl}/auth/login`, { email: 'a@b.c', password: 'x' }).subscribe({
      next: () => fail('a 429 must surface as an error'),
      error: (err) => {
        expect(err.status).toBe(429);
        done();
      }
    });

    httpMock.expectOne(`${environment.apiUrl}/auth/login`)
      .flush(null, { status: 429, statusText: 'Too Many Requests' });

    httpMock.expectNone(`${environment.apiUrl}/auth/refresh`);
    httpMock.expectNone(`${environment.apiUrl}/auth/logout`);
  });

  it('on 429 from a protected endpoint should not refresh the session', (done) => {
    http.get(`${environment.apiUrl}/protected`).subscribe({
      next: () => fail('a 429 must surface as an error'),
      error: (err) => {
        expect(err.status).toBe(429);
        done();
      }
    });

    httpMock.expectOne(`${environment.apiUrl}/protected`)
      .flush(null, { status: 429, statusText: 'Too Many Requests' });

    httpMock.expectNone(`${environment.apiUrl}/auth/refresh`);
  });
});
