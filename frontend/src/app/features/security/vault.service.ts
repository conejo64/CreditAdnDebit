import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable } from 'rxjs';

export interface VaultKey {
    id: string;
    version: number;
    algorithm: string;
    isActive: boolean;
    createdOn: string;
    expiresOn: string;
}

export interface VaultStats {
    totalTokens: number;
    activeKeyId: string;
    lastRotation: string;
    hsmStatus: 'online' | 'standby' | 'offline';
}

@Injectable({
    providedIn: 'root'
})
export class VaultService {
    private http = inject(HttpClient);
    private baseUrl = `${environment.apiUrl}/vault`;

    getActiveKey(): Observable<{ activeKeyId: string; availableKeyIds: string[] }> {
        return this.http.get<{ activeKeyId: string; availableKeyIds: string[] }>(`${this.baseUrl}/active-key`);
    }

    rotateKey(keyId: string): Observable<any> {
        return this.http.post(`${this.baseUrl}/rotate-active-key?keyId=${encodeURIComponent(keyId)}`, {});
    }

    reEncrypt(take: number = 100): Observable<any> {
        return this.http.post(`${this.baseUrl}/reencrypt?take=${take}`, {});
    }

    getAuditLogs(limit: number = 20): Observable<any[]> {
        return this.http.get<any[]>(`${environment.apiUrl}/audit/latest?take=${limit}`);
    }
}
