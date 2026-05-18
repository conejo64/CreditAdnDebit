import { TestBed } from '@angular/core/testing';
import {
    provideHttpClient
} from '@angular/common/http';
import {
    HttpTestingController,
    provideHttpClientTesting
} from '@angular/common/http/testing';
import { FinanceService, Statement } from './finance.service';
import { environment } from '../../../environments/environment';

const API = environment.apiUrl;

const mockStatement: Statement = {
    id: 'stmt-001',
    accountId: 'acc-001',
    statementDate: '2026-04-01T00:00:00Z',
    dueDate: '2026-04-15T00:00:00Z',
    previousBalance: 0,
    purchases: 500,
    payments: 0,
    fees: 10,
    interest: 5,
    newBalance: 515,
    minimumPayment: 51.5,
    totalPaymentDue: 515,
    status: 1
};

describe('FinanceService (v76 gate)', () => {
    let service: FinanceService;
    let httpMock: HttpTestingController;

    beforeEach(() => {
        TestBed.configureTestingModule({
            providers: [
                FinanceService,
                provideHttpClient(),
                provideHttpClientTesting()
            ]
        });

        service = TestBed.inject(FinanceService);
        httpMock = TestBed.inject(HttpTestingController);
    });

    afterEach(() => httpMock.verify());

    it('getStatements() calls GET /billing/accounts/:id/statements and returns statements', (done) => {
        service.getStatements('acc-001').subscribe(statements => {
            expect(statements.length).toBe(1);
            expect(statements[0].newBalance).toBe(515);
            done();
        });

        const req = httpMock.expectOne(r =>
            r.url === `${API}/billing/accounts/acc-001/statements`
        );
        expect(req.request.method).toBe('GET');
        req.flush([mockStatement]);
    });

    it('getStatements() for account with no statements returns empty list', (done) => {
        service.getStatements('acc-empty').subscribe(statements => {
            expect(statements.length).toBe(0);
            done();
        });

        const req = httpMock.expectOne(r =>
            r.url === `${API}/billing/accounts/acc-empty/statements`
        );
        req.flush([]);
    });
});
