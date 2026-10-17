import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { LoginComponent } from './login.component';
import { environment } from '../../../environments/environment';

/**
 * G1-T1 / G1-T2: the login form must start empty (no prefilled development credentials —
 * audit finding) and must show one generic Spanish message for a failed or rate-limited
 * login, without disclosing whether the account exists or is locked.
 */
describe('LoginComponent — credentials and failure messaging', () => {
  const GENERIC_MESSAGE = 'No se pudo iniciar sesión. Intente nuevamente en unos minutos.';
  let fixture: ComponentFixture<LoginComponent>;
  let component: LoginComponent;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [LoginComponent, HttpClientTestingModule],
      providers: [provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('starts with empty e-mail and password fields', () => {
    expect(component.loginForm.get('email')?.value).toBe('');
    expect(component.loginForm.get('password')?.value).toBe('');

    const emailInput: HTMLInputElement = fixture.nativeElement.querySelector('#email');
    const passwordInput: HTMLInputElement = fixture.nativeElement.querySelector('#password');
    expect(emailInput.value).toBe('');
    expect(passwordInput.value).toBe('');
  });

  it('does not reference the development credentials anywhere in the template', () => {
    const html: string = fixture.nativeElement.innerHTML;
    expect(html).not.toContain('Admin1234!');
    expect(html).not.toContain('admin@demo.com');
  });

  it('renders the generic message after a 401 login failure', () => {
    component.loginForm.setValue({ email: 'user@test.com', password: 'WrongPassword1234!', rememberMe: false });
    component.onSubmit();

    httpMock.expectOne(`${environment.apiUrl}/auth/login`)
      .flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();

    const error: HTMLElement | null = fixture.nativeElement.querySelector('.auth-error');
    expect(error).not.toBeNull();
    expect(error!.textContent).toContain(GENERIC_MESSAGE);
  });

  it('renders the same generic message after a 429 (rate limited)', () => {
    component.loginForm.setValue({ email: 'user@test.com', password: 'Password1234!', rememberMe: false });
    component.onSubmit();

    httpMock.expectOne(`${environment.apiUrl}/auth/login`)
      .flush(null, { status: 429, statusText: 'Too Many Requests' });
    fixture.detectChanges();

    const error: HTMLElement | null = fixture.nativeElement.querySelector('.auth-error');
    expect(error).not.toBeNull();
    expect(error!.textContent).toContain(GENERIC_MESSAGE);
    expect(error!.textContent).not.toContain('bloquead');
  });
});

/**
 * G1-T3: when login answers mfaRequired the component shows the verification-code step in
 * place of the credentials form and submits the code through AuthService.
 */
describe('LoginComponent — two-step MFA (G1-T3)', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let component: LoginComponent;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [LoginComponent, HttpClientTestingModule],
      providers: [provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  function reachMfaStep(): void {
    component.loginForm.setValue({ email: 'mfa@test.com', password: 'Password123!', rememberMe: false });
    component.onSubmit();
    httpMock.expectOne(`${environment.apiUrl}/auth/login`)
      .flush({ mfaRequired: true, mfaPendingToken: 'pending-1', message: 'MFA_REQUIRED', user: null });
    fixture.detectChanges();
  }

  it('replaces the credentials form with the verification-code step', () => {
    reachMfaStep();

    const host: HTMLElement = fixture.nativeElement;
    expect(host.querySelector('#mfaCode')).not.toBeNull();
    expect(host.querySelector('#password')).toBeNull();
    expect(host.textContent).not.toContain('aun no esta conectado');
    expect(host.querySelector('.auth-error')).toBeNull();
  });

  it('submits the code with the pending token through the auth service', () => {
    reachMfaStep();

    component.mfaForm.setValue({ code: '123456' });
    component.onSubmitMfa();

    const verify = httpMock.expectOne(`${environment.apiUrl}/auth/mfa/verify`);
    expect(verify.request.body).toEqual({ mfaPendingToken: 'pending-1', code: '123456' });
    verify.flush({
      mfaRequired: false,
      message: 'OK',
      user: { id: '2', email: 'mfa@test.com', name: 'MFA User', primaryRole: 'Operator', roles: ['Operator'], permissions: [] }
    });
    fixture.detectChanges();

    expect(component.authService.currentUser()?.email).toBe('mfa@test.com');
    expect(fixture.nativeElement.querySelector('#mfaCode')).toBeNull();
  });

  it('offers a way back to the credentials step', () => {
    reachMfaStep();

    component.cancelMfa();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#password')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#mfaCode')).toBeNull();
  });
});
