import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, ActivatedRouteSnapshot } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { of } from 'rxjs';
import { roleGuard } from './role.guard';
import { AuthService } from '../auth.service';

describe('roleGuard (v76 gate)', () => {
    let authServiceSpy: jasmine.SpyObj<AuthService>;
    let router: Router;

    beforeEach(() => {
        authServiceSpy = jasmine.createSpyObj<AuthService>('AuthService', ['ensureAuthorized']);

        TestBed.configureTestingModule({
            imports: [RouterTestingModule],
            providers: [
                { provide: AuthService, useValue: authServiceSpy }
            ]
        });

        router = TestBed.inject(Router);
    });

    function buildRoute(roles: string[]): ActivatedRouteSnapshot {
        const snap = new ActivatedRouteSnapshot();
        (snap as any)._routeConfig = {};
        (snap as any).data = { roles };
        return snap;
    }

    it('allows navigation when user has an allowed role', (done) => {
        authServiceSpy.ensureAuthorized.and.returnValue(of(true));

        TestBed.runInInjectionContext(() => {
            const result$ = roleGuard(buildRoute(['Admin']), {} as any);
            (result$ as any).subscribe((result: boolean | UrlTree) => {
                expect(result).toBe(true);
                expect(authServiceSpy.ensureAuthorized).toHaveBeenCalledWith(['Admin']);
                done();
            });
        });
    });

    it('redirects to dashboard when user lacks required role', (done) => {
        const dashboardTree = router.createUrlTree(['/app/dashboard']);
        authServiceSpy.ensureAuthorized.and.returnValue(of(dashboardTree));

        TestBed.runInInjectionContext(() => {
            const result$ = roleGuard(buildRoute(['Admin']), {} as any);
            (result$ as any).subscribe((result: boolean | UrlTree) => {
                expect(result).toBeInstanceOf(UrlTree);
                expect((result as UrlTree).toString()).toContain('dashboard');
                done();
            });
        });
    });
});
