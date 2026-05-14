import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable } from 'rxjs';

export interface DashboardTransaction {
    traceId: string;
    txType: string;
    status: string;
    decision: string;
    responseCode: string;
    createdOn: string;
    amount12?: string;
    currency?: string;
    terminalId?: string;
}

export interface DashboardMetrics {
    count: number;
    items: DashboardTransaction[];
}

@Injectable({
    providedIn: 'root'
})
export class DashboardService {
    private http = inject(HttpClient);

    getLatestTransactions(take: number = 5): Observable<DashboardMetrics> {
        return this.http.get<DashboardMetrics>(`${environment.isoSwitchUrl}/transactions?take=${take}`);
    }

    getCustomersCount(): Observable<any> {
        return this.http.get<any>(`${environment.apiUrl}/issuer/customers?take=1`);
    }
}
