import { TestBed } from '@angular/core/testing';
import {
    HttpClient,
    provideHttpClient
} from '@angular/common/http';
import {
    HttpTestingController,
    provideHttpClientTesting
} from '@angular/common/http/testing';
import { CustomerService, Customer } from './customer.service';
import { environment } from '../../../../environments/environment';

const BASE = `${environment.apiUrl}/issuer`;

const mockCustomer: Customer = {
    id: 'cust-001',
    customerNumber: 'CN-001',
    fullName: 'Ana Ríos',
    documentId: '1234567890',
    email: 'ana@example.com',
    phone: '+593999000111',
    createdOn: '2026-01-01T00:00:00Z',
    accounts: []
};

describe('CustomerService (v76 gate)', () => {
    let service: CustomerService;
    let httpMock: HttpTestingController;

    beforeEach(() => {
        TestBed.configureTestingModule({
            providers: [
                CustomerService,
                provideHttpClient(),
                provideHttpClientTesting()
            ]
        });

        service = TestBed.inject(CustomerService);
        httpMock = TestBed.inject(HttpTestingController);
    });

    afterEach(() => httpMock.verify());

    it('getCustomers() calls GET /issuer/customers and returns customer list', (done) => {
        service.getCustomers().subscribe(customers => {
            expect(customers.length).toBe(1);
            expect(customers[0].fullName).toBe('Ana Ríos');
            done();
        });

        const req = httpMock.expectOne(`${BASE}/customers`);
        expect(req.request.method).toBe('GET');
        req.flush([mockCustomer]);
    });

    it('getCustomer() calls GET /issuer/customers/:id and returns single customer', (done) => {
        service.getCustomer('cust-001').subscribe(customer => {
            expect(customer.id).toBe('cust-001');
            expect(customer.email).toBe('ana@example.com');
            done();
        });

        const req = httpMock.expectOne(`${BASE}/customers/cust-001`);
        expect(req.request.method).toBe('GET');
        req.flush(mockCustomer);
    });
});
