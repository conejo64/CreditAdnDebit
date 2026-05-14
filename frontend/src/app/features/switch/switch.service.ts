import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable } from 'rxjs';

export interface AuditLog {
    id: string;
    traceId: string;
    timestamp: string;
    severity: 'INFO' | 'WARN' | 'ERROR';
    component: string;
    message: string;
    payload: any;
}

export interface IsoTransaction {
    traceId: string;
    correlationId?: string;
    txType: string;
    status: string;
    decision: string;
    responseCode: string;
    connectorId: string;
    createdOn: string;
    updatedOn?: string;
    originalTraceId?: string;
    reversalState?: string;
    requestMti: string;
    amount12?: string;
    currency?: string;
    processingCode?: string;
    stan?: string;
}

export interface TransactionResponse {
    count: number;
    items: IsoTransaction[];
}

@Injectable({
    providedIn: 'root'
})
export class SwitchService {
    private http = inject(HttpClient);
    // Depending on whether it's CardVault or IsoSwitch, ports may vary in a real environment.
    // Using the base apiUrl for now. It can be extended if we have a separate Switch API URL.

    getAuditLogs(): Observable<AuditLog[]> {
        return this.http.get<AuditLog[]>(`${environment.apiUrl}/audit/latest`);
    }

    getTransactions(): Observable<TransactionResponse> {
        return this.http.get<TransactionResponse>(`${environment.isoSwitchUrl}/transactions`);
    }

    simulateAuthorize(payload: any): Observable<any> {
        return this.http.post(`${environment.isoSwitchUrl}/iso/authorize`, payload);
    }

    simulateReversal(payload: any): Observable<any> {
        return this.http.post(`${environment.isoSwitchUrl}/iso/reversal`, payload);
    }

    simulateCapture(payload: any): Observable<any> {
        return this.http.post(`${environment.isoSwitchUrl}/iso/capture`, payload);
    }
}
