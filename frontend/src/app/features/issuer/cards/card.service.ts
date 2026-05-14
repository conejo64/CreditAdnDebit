import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../../environments/environment';
import { Observable } from 'rxjs';

export enum CardStatus {
    Created = 1,
    Personalized = 2,
    Printed = 3,
    Delivered = 4,
    Active = 5,
    Blocked = 6,
    Cancelled = 7,
    Expired = 8
}

export interface Card {
    id: string;
    accountId: string;
    customerName?: string;
    bin: string;
    panToken: string; // token in vault
    maskedPan: string;
    expiryYyMm: string;
    last4: string;
    status: CardStatus;
    createdOn: string;
}

@Injectable({
    providedIn: 'root'
})
export class CardService {
    private http = inject(HttpClient);
    private baseUrl = `${environment.apiUrl}/issuer/cards`; // Note: Adjust if backend endpoint is different

    getCards(): Observable<Card[]> {
        return this.http.get<Card[]>(this.baseUrl);
    }

    getCard(id: string): Observable<Card> {
        return this.http.get<Card>(`${this.baseUrl}/${id}`);
    }

    issueCard(accountId: string, bin: string, pan: string, expiryYyMm: string): Observable<Card> {
        return this.http.post<Card>(`${this.baseUrl}/issue`, { accountId, bin, pan, expiryYyMm });
    }

    activateCard(id: string): Observable<any> {
        return this.http.post(`${this.baseUrl}/${id}/activate`, {});
    }

    blockCard(id: string): Observable<any> {
        return this.http.post(`${this.baseUrl}/${id}/block`, {});
    }

    setPin(id: string, pin: string): Observable<any> {
        return this.http.post(`${this.baseUrl}/${id}/pin`, { pin });
    }
}
