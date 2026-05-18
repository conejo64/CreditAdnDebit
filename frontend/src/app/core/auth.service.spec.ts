import { TestBed } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import {
    provideHttpClient
} from '@angular/common/http';
import {
    HttpTestingController,
    provideHttpClientTesting
} from '@angular/common/http/testing';
import { AuthService, User } from './auth.service';
import { environment } from '../../environments/environment';

const mockUser: User = {
    id: 'u1',
    name: 'Test Admin',
    email: 'admin@test.com',
    role: 'Admin',
    roles: ['Admin'],
    permissions: ['switch:operate']
};

function buildJwt(expSeconds: number): string {
    const payload = btoa(JSON.stringify({ exp: expSeconds }));
    return `header.${payload}.signature`;
}

function configureTestBed() {
    TestBed.configureTestingModule({
        imports: [RouterTestingModule],
        providers: [
            AuthService,
            provideHttpClient(),
            provideHttpClientTesting()
        ]
    });
}

describe('AuthService (v76 gate)', () => {
    let httpMock: HttpTestingController;

    afterEach(() => {
        httpMock?.verify();
        localStorage.clear();
        TestBed.resetTestingModule();
    });

    describe('ensureAuthenticated()', () => {
        it('redirects to /auth/login when no access token is stored', (done) => {
            localStorage.clear();
            configureTestBed();
            const service = TestBed.inject(AuthService);
            httpMock = TestBed.inject(HttpTestingController);

            service.ensureAuthenticated().subscribe(result => {
                expect(result).not.toBe(true);
                expect(result.toString()).toContain('auth/login');
                done();
            });
        });

        it('returns true when a valid (non-expired) token and user are in session', (done) => {
            const futureExp = Math.floor(Date.now() / 1000) + 3600;
            localStorage.setItem('auth.access_token', buildJwt(futureExp));
            localStorage.setItem('auth.user', JSON.stringify(mockUser));

            configureTestBed();
            const service = TestBed.inject(AuthService);
            httpMock = TestBed.inject(HttpTestingController);

            service.ensureAuthenticated().subscribe(result => {
                expect(result).toBe(true);
                done();
            });
        });
    });

    describe('ensureAuthorized()', () => {
        it('returns true when user has the required role', (done) => {
            const futureExp = Math.floor(Date.now() / 1000) + 3600;
            localStorage.setItem('auth.access_token', buildJwt(futureExp));
            localStorage.setItem('auth.user', JSON.stringify(mockUser));

            configureTestBed();
            const service = TestBed.inject(AuthService);
            httpMock = TestBed.inject(HttpTestingController);

            service.ensureAuthorized(['Admin']).subscribe(result => {
                expect(result).toBe(true);
                done();
            });
        });

        it('redirects to /app/dashboard when user lacks required role', (done) => {
            const operatorUser: User = { ...mockUser, role: 'Operator', roles: ['Operator'] };
            const futureExp = Math.floor(Date.now() / 1000) + 3600;
            localStorage.setItem('auth.access_token', buildJwt(futureExp));
            localStorage.setItem('auth.user', JSON.stringify(operatorUser));

            configureTestBed();
            const service = TestBed.inject(AuthService);
            httpMock = TestBed.inject(HttpTestingController);

            service.ensureAuthorized(['Admin']).subscribe(result => {
                expect(result).not.toBe(true);
                expect(result.toString()).toContain('dashboard');
                done();
            });
        });
    });
});
