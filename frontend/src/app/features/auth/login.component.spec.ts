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
