import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../../environments/environment';
import { Observable } from 'rxjs';
 
export enum AccountType {
    Debit = 1,
    Credit = 2
}

export interface Customer {
    id: string;
    customerNumber: string;
    fullName: string;
    documentId: string;
    email: string;
    phone: string;
    documentType?: string;
    gender?: string;
    billingAddress?: string;
    statementAddress?: string;
    residenceCity?: string;
    statementCity?: string;
    cardDeliveryCity?: string;
    createdOn: string;
    accounts: Account[];
}

export interface Account {
    id: string;
    accountNumber: string;
    customerId: string;
    customerName?: string;
    accountType: number; // 1 = Debit, 2 = Credit
    currencyCode: string;
    productCode: string;
    creditLimit: number;
    availableLimit: number;
    ledgerBalance: number;
    status: number; // 1 = Active, 2 = Blocked
    createdOn: string;
}

export interface AccountLimit {
    id?: string;
    accountId: string;
    dailyAtmLimit: number;
    dailyPosLimit: number;
    dailyEcommerceLimit: number;
    dailyAtmAuculated?: number;
    dailyPosAccumulated?: number;
    dailyEcommerceAccumulated?: number;
    lastResetDate?: string;
    updatedAt?: string;
}

@Injectable({
    providedIn: 'root'
})
export class CustomerService {
    private http = inject(HttpClient);
    private baseUrl = `${environment.apiUrl}/issuer`;

    getCustomers(): Observable<Customer[]> {
        return this.http.get<Customer[]>(`${this.baseUrl}/customers`);
    }

    getCustomer(id: string): Observable<Customer> {
        return this.http.get<Customer>(`${this.baseUrl}/customers/${id}`);
    }

    createCustomer(data: Partial<Customer>): Observable<Customer> {
        return this.http.post<Customer>(`${this.baseUrl}/customers`, data);
    }

    createAccount(data: any): Observable<Account> {
        return this.http.post<Account>(`${this.baseUrl}/accounts`, data);
    }

    getAccounts(query: string = '', take: number = 50): Observable<Account[]> {
        return this.http.get<Account[]>(`${this.baseUrl}/accounts?q=${query}&take=${take}`);
    }

    getAccountLimits(accountId: string): Observable<AccountLimit> {
        return this.http.get<AccountLimit>(`${this.baseUrl}/accounts/${accountId}/limits`);
    }

    updateAccountLimits(accountId: string, limits: AccountLimit): Observable<any> {
        return this.http.put(`${this.baseUrl}/accounts/${accountId}/limits`, limits);
    }
}

