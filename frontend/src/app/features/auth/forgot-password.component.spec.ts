import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ForgotPasswordComponent } from './forgot-password.component';
import { AuthService } from '../../core/auth.service';
import { RouterModule } from '@angular/router';
import { of, throwError } from 'rxjs';

describe('ForgotPasswordComponent', () => {
  let component: ForgotPasswordComponent;
  let fixture: ComponentFixture<ForgotPasswordComponent>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;

  beforeEach(async () => {
    authServiceSpy = jasmine.createSpyObj('AuthService', ['forgotPassword']);

    await TestBed.configureTestingModule({
      imports: [ForgotPasswordComponent, RouterModule.forRoot([])],
      providers: [{ provide: AuthService, useValue: authServiceSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(ForgotPasswordComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should do nothing when email is empty', () => {
    component.email = '';
    component.sendResetLink();
    expect(authServiceSpy.forgotPassword).not.toHaveBeenCalled();
  });

  it('should call authService.forgotPassword with the provided email', () => {
    authServiceSpy.forgotPassword.and.returnValue(of(undefined));
    component.email = 'user@test.com';
    component.sendResetLink();
    expect(authServiceSpy.forgotPassword).toHaveBeenCalledWith('user@test.com');
  });

  it('should set emailSent to true on success', () => {
    authServiceSpy.forgotPassword.and.returnValue(of(undefined));
    component.email = 'user@test.com';
    component.sendResetLink();
    expect(component.emailSent).toBeTrue();
  });

  it('should not set emailSent to true on HTTP error', () => {
    authServiceSpy.forgotPassword.and.returnValue(throwError(() => new Error('Network error')));
    component.email = 'user@test.com';
    component.sendResetLink();
    expect(component.emailSent).toBeFalse();
  });

  /**
   * GAP-6 (RED): Spec IAM-PR-4-S2 requires the component to surface an error state
   * when the HTTP call fails.  The current implementation silently swallows errors.
   * This test fails until an `errorMessage` property is added and set in the error callback.
   */
  it('should set errorMessage when the HTTP call fails', () => {
    authServiceSpy.forgotPassword.and.returnValue(throwError(() => new Error('Network error')));
    component.email = 'user@test.com';
    component.sendResetLink();
    expect(component.errorMessage).toBeTruthy('errorMessage must be non-empty on HTTP error');
  });

  /**
   * GAP-6: Error message must be cleared when the user retries successfully.
   */
  it('should clear errorMessage on successful retry', () => {
    // First attempt fails
    authServiceSpy.forgotPassword.and.returnValue(throwError(() => new Error('Fail')));
    component.email = 'user@test.com';
    component.sendResetLink();

    // Second attempt succeeds
    authServiceSpy.forgotPassword.and.returnValue(of(undefined));
    component.sendResetLink();
    expect(component.errorMessage).toBeFalsy('errorMessage must be cleared on success');
  });

  /**
   * GAP-3 (RED): The template has no element bound to errorMessage.
   * This DOM test fails until a *ngIf="errorMessage" div is added to the template.
   */
  it('should render error message element in the DOM when sendResetLink fails', () => {
    authServiceSpy.forgotPassword.and.returnValue(throwError(() => new Error('Network error')));
    component.email = 'user@test.com';
    component.sendResetLink();
    fixture.detectChanges();

    const errorEl: HTMLElement | null = fixture.nativeElement.querySelector('.alert-danger');
    expect(errorEl).not.toBeNull('error div must exist in DOM when errorMessage is set');
    expect(errorEl?.textContent?.trim()).toContain('Hubo un problema');
  });
});

