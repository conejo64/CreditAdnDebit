import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { of } from 'rxjs';
import { authGuard } from './auth.guard';
import { AuthService } from '../auth.service';

describe('authGuard (v76 gate)', () => {
    let authServiceSpy: jasmine.SpyObj<AuthService>;
    let router: Router;

    beforeEach(() => {
        authServiceSpy = jasmine.createSpyObj<AuthService>('AuthService', ['ensureAuthenticated']);

        TestBed.configureTestingModule({
            imports: [RouterTestingModule],
            providers: [
                { provide: AuthService, useValue: authServiceSpy }
            ]
        });

        router = TestBed.inject(Router);
    });

    it('allows navigation when ensureAuthenticated returns true', (done) => {
        authServiceSpy.ensureAuthenticated.and.returnValue(of(true));

        TestBed.runInInjectionContext(() => {
            const result$ = authGuard({} as any, {} as any);
            (result$ as any).subscribe((result: boolean | UrlTree) => {
                expect(result).toBe(true);
                done();
            });
        });
    });

    it('redirects to /auth/login when ensureAuthenticated returns a UrlTree', (done) => {
        const loginTree = router.createUrlTree(['/auth/login']);
        authServiceSpy.ensureAuthenticated.and.returnValue(of(loginTree));

        TestBed.runInInjectionContext(() => {
            const result$ = authGuard({} as any, {} as any);
            (result$ as any).subscribe((result: boolean | UrlTree) => {
                expect(result).toBeInstanceOf(UrlTree);
                expect((result as UrlTree).toString()).toContain('auth/login');
                done();
            });
        });
    });
});
