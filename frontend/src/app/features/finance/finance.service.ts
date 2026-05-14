import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable } from 'rxjs';

export enum LedgerEntryType {
    Purchase = 1,
    Payment = 2,
    Fee = 3,
    Interest = 4,
    Adjustment = 5,
    Refund = 6,
    Reversal = 7,
    Chargeback = 8,
    AuthorizationHold = 9,
    Clearing = 10
}

export interface LedgerEntry {
    id: string;
    accountId: string;
    type: LedgerEntryType;
    amount: number;
    description: string;
    postedOn: string;
}

export interface Statement {
    id: string;
    accountId: string;
    statementDate: string;
    dueDate: string;
    previousBalance: number;
    purchases: number;
    payments: number;
    fees: number;
    interest: number;
    newBalance: number;
    minimumPayment: number;
    totalPaymentDue: number;
    status: 1 | 2; // 1 Open, 2 Closed
}

@Injectable({
    providedIn: 'root'
})
export class FinanceService {
    private http = inject(HttpClient);

    getAccountLedger(accountId: string): Observable<LedgerEntry[]> {
        return this.http.get<LedgerEntry[]>(`${environment.apiUrl}/ledger/accounts/${accountId}/movements`);
    }

    getStatements(accountId: string): Observable<Statement[]> {
        return this.http.get<Statement[]>(`${environment.apiUrl}/billing/accounts/${accountId}/statements`, {
            params: { take: 24 }
        });
    }

    payStatement(statementId: string, amount: number): Observable<any> {
        return this.http.post(`${environment.apiUrl}/billing/statements/${statementId}/pay`, { amount });
    }

    simulatePurchase(accountId: string, amount: number, description: string): Observable<any> {
        return this.http.post(`${environment.apiUrl}/ledger/purchase`, { accountId, amount, description });
    }
}
