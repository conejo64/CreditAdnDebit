import { TestBed } from '@angular/core/testing';
import {
    HttpClient,
    HttpErrorResponse,
    provideHttpClient,
    withInterceptors
} from '@angular/common/http';
import {
    HttpTestingController,
    provideHttpClientTesting
} from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { environment } from '../../environments/environment';

describe('authInterceptor (v76 gate)', () => {
    let http: HttpClient;
    let httpMock: HttpTestingController;
    let authServiceSpy: jasmine.SpyObj<AuthService>;

    const apiUrl = environment.apiUrl;

    beforeEach(() => {
        authServiceSpy = jasmine.createSpyObj<AuthService>(
            'AuthService',
            ['getAccessToken', 'refreshSession', 'logout']
        );
        authServiceSpy.getAccessToken.and.returnValue('test-token');

        TestBed.configureTestingModule({
            providers: [
                { provide: AuthService, useValue: authServiceSpy },
                provideHttpClient(withInterceptors([authInterceptor])),
                provideHttpClientTesting()
            ]
        });

        http = TestBed.inject(HttpClient);
        httpMock = TestBed.inject(HttpTestingController);
    });

    afterEach(() => {
        httpMock.verify();
    });

    it('attaches Bearer token to API requests', () => {
        http.get(`${apiUrl}/issuer/customers`).subscribe();

        const req = httpMock.expectOne(`${apiUrl}/issuer/customers`);
        expect(req.request.headers.get('Authorization')).toBe('Bearer test-token');
        req.flush([]);
    });

    it('attempts token refresh on 401 response from API', () => {
        authServiceSpy.refreshSession.and.returnValue(of('new-token'));

        http.get(`${apiUrl}/issuer/customers`).subscribe({ error: () => {} });

        const req = httpMock.expectOne(`${apiUrl}/issuer/customers`);
        req.flush(null, { status: 401, statusText: 'Unauthorized' });

        const retryReq = httpMock.expectOne(`${apiUrl}/issuer/customers`);
        expect(retryReq.request.headers.get('Authorization')).toBe('Bearer new-token');
        retryReq.flush([]);
    });

    it('calls logout when refresh fails after 401', (done) => {
        authServiceSpy.refreshSession.and.returnValue(
            throwError(() => new HttpErrorResponse({ status: 401 }))
        );

        http.get(`${apiUrl}/issuer/customers`).subscribe({
            error: () => {
                expect(authServiceSpy.logout).toHaveBeenCalled();
                done();
            }
        });

        const req = httpMock.expectOne(`${apiUrl}/issuer/customers`);
        req.flush(null, { status: 401, statusText: 'Unauthorized' });
    });
});
