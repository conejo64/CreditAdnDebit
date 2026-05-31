import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ResetPasswordComponent } from './reset-password.component';
import { AuthService } from '../../core/auth.service';
import { Router, ActivatedRoute, convertToParamMap } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { of, throwError } from 'rxjs';

describe('ResetPasswordComponent', () => {
  let component: ResetPasswordComponent;
  let fixture: ComponentFixture<ResetPasswordComponent>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let router: Router;

  beforeEach(async () => {
    authServiceSpy = jasmine.createSpyObj('AuthService', ['resetPassword']);

    await TestBed.configureTestingModule({
      imports: [ResetPasswordComponent, RouterTestingModule],
      providers: [
        { provide: AuthService, useValue: authServiceSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap({ token: 'abc123' }) } }
        }
      ]
    }).compileComponents();

    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.returnValue(Promise.resolve(true));

    fixture = TestBed.createComponent(ResetPasswordComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should read the token from query params on init', () => {
    expect(component.token).toBe('abc123');
  });

  it('should do nothing when token is empty', () => {
    component.token = '';
    component.newPassword = 'NewPass123!';
    component.resetPassword();
    expect(authServiceSpy.resetPassword).not.toHaveBeenCalled();
  });

  it('should call authService.resetPassword with token and newPassword', () => {
    authServiceSpy.resetPassword.and.returnValue(of(undefined));
    component.token = 'my-token';
    component.newPassword = 'NewPass123!';
    component.resetPassword();
    expect(authServiceSpy.resetPassword).toHaveBeenCalledWith('my-token', 'NewPass123!');
  });

  /**
   * GAP-6 (RED): Spec IAM-PR-4-S4 requires the component to SHOW a success state
   * instead of immediately redirecting.  The current implementation navigates to
   * /auth/login on success, which bypasses the "success" template branch.
   * This test fails until router.navigate() is removed from the success callback.
   */
  it('should NOT navigate automatically on success — show success state instead', () => {
    authServiceSpy.resetPassword.and.returnValue(of(undefined));
    component.token = 'my-token';
    component.newPassword = 'NewPass123!';
    component.resetPassword();
    // Spec: show success state; the user decides when to go to login via the link
    expect(router.navigate).not.toHaveBeenCalled();
    expect(component.submitted).toBeTrue();
  });

  it('should not navigate on HTTP error', () => {
    authServiceSpy.resetPassword.and.returnValue(throwError(() => new Error('Bad token')));
    component.token = 'bad-token';
    component.newPassword = 'NewPass123!';
    component.resetPassword();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('should set an error message on failure', () => {
    authServiceSpy.resetPassword.and.returnValue(throwError(() => new Error('Bad token')));
    component.token = 'bad-token';
    component.newPassword = 'NewPass123!';
    component.resetPassword();
    expect(component.error).toBeTruthy();
  });

  /**
   * GAP-6 (RED): Spec IAM-PR-4-S3 requires the component to detect a missing token
   * on init and show an invalid-link error state.
   * This test fails until an `invalidLink` property is added and set when token is absent.
   */
  describe('when no token is in the query params', () => {
    let noTokenComponent: ResetPasswordComponent;
    let noTokenFixture: any;

    beforeEach(async () => {
      TestBed.resetTestingModule();
      await TestBed.configureTestingModule({
        imports: [ResetPasswordComponent, RouterTestingModule],
        providers: [
          { provide: AuthService, useValue: authServiceSpy },
          {
            provide: ActivatedRoute,
            useValue: { snapshot: { queryParamMap: convertToParamMap({}) } }
          }
        ]
      }).compileComponents();

      noTokenFixture = TestBed.createComponent(ResetPasswordComponent);
      noTokenFixture.detectChanges();
      noTokenComponent = noTokenFixture.componentInstance;
    });

    it('should set invalidLink to true when token query param is absent', () => {
      expect(noTokenComponent.invalidLink).withContext(
        'invalidLink must be true when ?token= is missing (spec IAM-PR-4-S3)'
      ).toBeTrue();
    });

    it('should not call resetPassword when invalidLink is true', () => {
      noTokenComponent.newPassword = 'NewPass123!';
      noTokenComponent.resetPassword();
      expect(authServiceSpy.resetPassword).not.toHaveBeenCalled();
    });

    /**
     * GAP-3 (RED): Template has no element bound to invalidLink.
     * Fails until a *ngIf="invalidLink" block is added to the template.
     */
    it('should render invalid-link section in the DOM when token is absent', () => {
      const compiled: HTMLElement = noTokenFixture.nativeElement;
      expect(compiled.textContent).toContain('Enlace Inválido',
        'invalid-link section must appear in DOM when token query param is missing');
    });
  });
});
