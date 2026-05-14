import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable } from 'rxjs';

export interface OpenBankingClient {
  clientId: string;
  name: string;
  redirectUri: string;
  scopes: string[];
  allowedAccountIds: string[];
  createdAt: string;
  isActive: boolean;
}

export interface OpenBankingBalance {
  accountId: string;
  availableBalance: number;
  currentBalance: number;
  currency: string;
  asOf: string;
}

export interface OpenBankingTransaction {
  id: string;
  accountId: string;
  amount: number;
  currency: string;
  description: string;
  type: string;
  postedAt: string;
}

export interface CreateOpenBankingClientRequest {
  name: string;
  redirectUri: string;
  scopes: string[];
}

@Injectable({ providedIn: 'root' })
export class OpenBankingService {
  private http = inject(HttpClient);
  private adminBase = `${environment.apiUrl}/admin/open-banking`;
  private publicBase = `${environment.apiUrl}/open-banking`;

  // Admin endpoints
  getClients(): Observable<OpenBankingClient[]> {
    return this.http.get<OpenBankingClient[]>(`${this.adminBase}/clients`);
  }

  createClient(request: CreateOpenBankingClientRequest): Observable<OpenBankingClient> {
    return this.http.post<OpenBankingClient>(`${this.adminBase}/clients`, request);
  }

  grantAccountAccess(clientId: string, accountId: string): Observable<OpenBankingClient> {
    return this.http.post<OpenBankingClient>(`${this.adminBase}/clients/${clientId}/accounts/${accountId}`, {});
  }

  // Read endpoints (for demo/monitoring purposes)
  getBalance(accountId: string): Observable<OpenBankingBalance> {
    return this.http.get<OpenBankingBalance>(`${this.publicBase}/accounts/${accountId}/balance`);
  }

  getTransactions(accountId: string, take = 20): Observable<OpenBankingTransaction[]> {
    return this.http.get<OpenBankingTransaction[]>(`${this.publicBase}/accounts/${accountId}/transactions`, { params: { take } });
  }
}
