import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from './auth.service';
import { environment } from '../../environments/environment';

/**
 * SEC-03 (tasks 4.14, 4.18): after the cookie-based cutover, AuthService must never
 * persist access/refresh tokens to localStorage (the browser holds them as HttpOnly
 * cookies automatically), and session validity must be driven by /auth/me + /auth/refresh
 * rather than client-side JWT parsing.
 *
 * RED before the cutover: the current implementation writes ACCESS_TOKEN_KEY /
 * REFRESH_TOKEN_KEY to localStorage from applySessionResponse, and ensureAuthenticated
 * reads getAccessToken()/isTokenExpired() instead of calling /auth/me first.
 */
describe('AuthService — cookie-based session (SEC-03)', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  const mockUser = {
    id: '1',
    email: 'user@test.com',
    name: 'Test User',
    primaryRole: 'Admin',
    roles: ['Admin'],
    permissions: []
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [provideRouter([])]
    });
    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('should NOT write an access token to localStorage after a successful login', () => {
    service.login('user@test.com', 'Password123!');

    httpMock.expectOne(`${environment.apiUrl}/auth/login`)
      .flush({ mfaRequired: false, message: 'OK', user: mockUser });

    expect(localStorage.getItem('auth.access_token')).toBeNull();
  });

  it('should NOT write a refresh token to localStorage after a successful login', () => {
    service.login('user@test.com', 'Password123!');

    httpMock.expectOne(`${environment.apiUrl}/auth/login`)
      .flush({ mfaRequired: false, message: 'OK', user: mockUser });

    expect(localStorage.getItem('auth.refresh_token')).toBeNull();
  });

  it('should set currentUser from the login response user field (login body no longer carries tokens)', () => {
    service.login('user@test.com', 'Password123!');

    httpMock.expectOne(`${environment.apiUrl}/auth/login`)
      .flush({ mfaRequired: false, message: 'OK', user: mockUser });

    expect(service.currentUser()?.email).toBe('user@test.com');
  });

  it('should not expose getAccessToken/getRefreshToken (tokens are HttpOnly, unreadable by JS)', () => {
    expect((service as unknown as Record<string, unknown>)['getAccessToken']).toBeUndefined();
    expect((service as unknown as Record<string, unknown>)['getRefreshToken']).toBeUndefined();
  });

  it('ensureAuthenticated should authenticate via /auth/me without any stored token', (done) => {
    service.ensureAuthenticated().subscribe((result) => {
      expect(result).toBeTrue();
      done();
    });

    httpMock.expectOne(`${environment.apiUrl}/auth/me`).flush(mockUser);
  });

  it('ensureAuthenticated should try refresh then retry /auth/me on a 401', (done) => {
    service.ensureAuthenticated().subscribe((result) => {
      expect(result).toBeTrue();
      done();
    });

    httpMock.expectOne(`${environment.apiUrl}/auth/me`)
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    httpMock.expectOne(`${environment.apiUrl}/auth/refresh`)
      .flush({ mfaRequired: false, message: 'OK', user: mockUser });

    httpMock.expectOne(`${environment.apiUrl}/auth/me`).flush(mockUser);
  });

  it('ensureAuthenticated should NOT refresh or clear the session on a transient /auth/me error (500)', (done) => {
    // Seed an authenticated session first.
    service.login('user@test.com', 'Password123!');
    httpMock.expectOne(`${environment.apiUrl}/auth/login`)
      .flush({ mfaRequired: false, message: 'OK', user: mockUser });
    expect(service.currentUser()?.email).toBe('user@test.com');

    service.ensureAuthenticated().subscribe({
      next: () => fail('a transient server error must not resolve the guard'),
      error: (err) => {
        expect(err.status).toBe(500);
        // A backend blip is not an identity failure — the session must survive.
        expect(service.currentUser()?.email).toBe('user@test.com');
        done();
      }
    });

    httpMock.expectOne(`${environment.apiUrl}/auth/me`)
      .flush(null, { status: 500, statusText: 'Server Error' });
    // afterEach httpMock.verify() asserts NO /auth/refresh request was ever issued.
  });

  it('ensureAuthenticated should redirect to login when both /auth/me and refresh fail', (done) => {
    service.ensureAuthenticated().subscribe((result) => {
      expect(result).not.toBe(true);
      done();
    });

    httpMock.expectOne(`${environment.apiUrl}/auth/me`)
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    httpMock.expectOne(`${environment.apiUrl}/auth/refresh`)
      .flush(null, { status: 401, statusText: 'Unauthorized' });
  });
});
