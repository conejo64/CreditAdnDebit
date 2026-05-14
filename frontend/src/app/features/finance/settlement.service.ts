import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable } from 'rxjs';

export interface SettlementBatch {
    id: string;
    network: string;
    businessDate: string;
    totalAmount: number;
    itemCount: number;
    status: 'RECEIVED' | 'PROCESSED' | 'ERROR';
    createdAt: string;
}

export interface SettlementItem {
    id: string;
    batchId: string;
    rrn: string;
    stan: string;
    amount: number;
    postedOn: string;
    status: string;
}

@Injectable({
    providedIn: 'root'
})
export class SettlementService {
    private http = inject(HttpClient);

    getBatches(): Observable<SettlementBatch[]> {
        return this.http.get<SettlementBatch[]>(`${environment.apiUrl}/settlement/batches`);
    }

    getBatchDetails(batchId: string): Observable<SettlementItem[]> {
        return this.http.get<SettlementItem[]>(`${environment.apiUrl}/settlement/batches/${batchId}`);
    }

    runSettlement(network: string, businessDate: string): Observable<any> {
        return this.http.post(
            `${environment.apiUrl}/settlement/run?network=${encodeURIComponent(network)}&businessDate=${businessDate}`,
            {}
        );
    }

    getReconciliation(batchId: string): Observable<any> {
        return this.http.get(`${environment.apiUrl}/reconciliation/settlement/${batchId}`);
    }
}
