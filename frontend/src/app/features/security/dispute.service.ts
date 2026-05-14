import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable } from 'rxjs';

export interface DisputeCase {
    id: string;
    accountId: string;
    rrn: string;
    network: string;
    amount: number;
    reasonCode: string;
    status: 'OPEN' | 'IN_PROGRESS' | 'WON' | 'LOST' | 'CLOSED';
    openedAt: string;
}

export interface DisputeEvent {
    id: string;
    disputeId: string;
    action: string;
    notes?: string;
    createdOn: string;
}

@Injectable({
    providedIn: 'root'
})
export class DisputeService {
    private http = inject(HttpClient);

    getDisputesByAccount(accountId: string): Observable<DisputeCase[]> {
        return this.http.get<DisputeCase[]>(`${environment.apiUrl}/disputes/accounts/${accountId}`);
    }

    getDisputeEvents(disputeId: string): Observable<DisputeEvent[]> {
        return this.http.get<DisputeEvent[]>(`${environment.apiUrl}/disputes/${disputeId}/events`);
    }

    transitionDispute(disputeId: string, action: string, notes?: string): Observable<any> {
        return this.http.post(`${environment.apiUrl}/disputes/${disputeId}/transition`, { action, notes });
    }

    closeDispute(disputeId: string, won: boolean = false): Observable<any> {
        return this.http.post(`${environment.apiUrl}/disputes/${disputeId}/close?won=${won}`, {});
    }
}
