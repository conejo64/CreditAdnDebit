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

/**
 * G1-T1: the login screen must not disclose why a login failed. Unknown user, wrong
 * password, a locked account (all 401 from the backend) and a rate-limited client (429)
 * all surface the same generic Spanish message.
 */
describe('AuthService — generic login failure message (G1-T1)', () => {
  const GENERIC_MESSAGE = 'No se pudo iniciar sesión. Intente nuevamente en unos minutos.';
  let service: AuthService;
  let httpMock: HttpTestingController;

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

  it('shows the generic message on 401 (unknown user / wrong password / locked account)', () => {
    service.login('user@test.com', 'WrongPassword1234!');

    httpMock.expectOne(`${environment.apiUrl}/auth/login`)
      .flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(service.authError()).toBe(GENERIC_MESSAGE);
    expect(service.currentUser()).toBeNull();
  });

  it('shows the same generic message on 429 (rate limited)', () => {
    service.login('user@test.com', 'Password1234!');

    httpMock.expectOne(`${environment.apiUrl}/auth/login`)
      .flush(null, { status: 429, statusText: 'Too Many Requests' });

    expect(service.authError()).toBe(GENERIC_MESSAGE);
    expect(service.authError()).not.toContain('bloquead');
    expect(service.authError()).not.toContain('locked');
  });

  it('does not issue a refresh or logout request after a 429 on login', () => {
    service.login('user@test.com', 'Password1234!');

    httpMock.expectOne(`${environment.apiUrl}/auth/login`)
      .flush(null, { status: 429, statusText: 'Too Many Requests' });

    httpMock.expectNone(`${environment.apiUrl}/auth/refresh`);
    httpMock.expectNone(`${environment.apiUrl}/auth/logout`);
  });
});

/**
 * G1-T3: login with MFA enabled answers { mfaRequired: true, mfaPendingToken } and no
 * session. The service must keep that token in memory only (never localStorage), send it
 * with the code to /auth/mfa/verify, and show one generic message when the verification is
 * rejected without disclosing whether the token or the code was wrong.
 */
describe('AuthService — two-step MFA login (G1-T3)', () => {
  const GENERIC_MFA_MESSAGE = 'No se pudo verificar el código. Intente nuevamente.';
  let service: AuthService;
  let httpMock: HttpTestingController;

  const mockUser = {
    id: '2',
    email: 'mfa@test.com',
    name: 'MFA User',
    primaryRole: 'Operator',
    roles: ['Operator'],
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

  function loginUntilMfaPending(): void {
    service.login('mfa@test.com', 'Password123!');
    httpMock.expectOne(`${environment.apiUrl}/auth/login`)
      .flush({ mfaRequired: true, mfaPendingToken: 'pending-1', message: 'MFA_REQUIRED', user: null });
  }

  it('enters the MFA step, keeps the pending token out of localStorage and sets no user', () => {
    loginUntilMfaPending();

    expect(service.mfaPending()).toBeTrue();
    expect(service.currentUser()).toBeNull();
    expect(service.authError()).toBeNull();
    const persisted = Object.keys(localStorage).map((key) => localStorage.getItem(key) ?? '');
    expect(persisted.some((value) => value.includes('pending-1'))).toBeFalse();
  });

  it('sends the pending token and the code to /auth/mfa/verify and applies the session', () => {
    loginUntilMfaPending();

    service.verifyMfa('123456');

    const verify = httpMock.expectOne(`${environment.apiUrl}/auth/mfa/verify`);
    expect(verify.request.body).toEqual({ mfaPendingToken: 'pending-1', code: '123456' });
    verify.flush({ mfaRequired: false, message: 'OK', user: mockUser });

    expect(service.currentUser()?.email).toBe('mfa@test.com');
    expect(service.mfaPending()).toBeFalse();
  });

  it('shows one generic message on a rejected verification and keeps the MFA step open', () => {
    loginUntilMfaPending();

    service.verifyMfa('000000');
    httpMock.expectOne(`${environment.apiUrl}/auth/mfa/verify`)
      .flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(service.authError()).toBe(GENERIC_MFA_MESSAGE);
    expect(service.mfaPending()).toBeTrue();
    expect(service.currentUser()).toBeNull();
  });

  it('cancelMfa returns to the credentials step and forgets the pending token', () => {
    loginUntilMfaPending();

    service.cancelMfa();
    service.verifyMfa('123456');

    expect(service.mfaPending()).toBeFalse();
    httpMock.expectNone(`${environment.apiUrl}/auth/mfa/verify`);
  });

  it('treats an mfaRequired answer without a pending token as an invalid session', () => {
    service.login('mfa@test.com', 'Password123!');
    httpMock.expectOne(`${environment.apiUrl}/auth/login`)
      .flush({ mfaRequired: true, mfaPendingToken: null, message: 'MFA_REQUIRED', user: null });

    expect(service.mfaPending()).toBeFalse();
    expect(service.authError()).not.toBeNull();
  });
});
