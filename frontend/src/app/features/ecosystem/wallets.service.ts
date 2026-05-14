import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable } from 'rxjs';

export interface WalletEnrollment {
  id: string;
  cardId: string;
  walletProvider: string;
  status: string;
  createdAt: string;
}

export interface WalletToken {
  id: string;
  cardId: string;
  walletProvider: string;
  maskedPan: string;
  status: string;
  activatedAt: string | null;
}

export interface WalletAuthorization {
  id: string;
  walletTokenId: string;
  amount: number;
  currency: string;
  status: string;
  authorizedAt: string;
}

export interface RegisterWalletTokenRequest {
  cardId: string;
  walletProvider: string;
  deviceId: string;
}

export interface ActivateWalletTokenRequest {
  otp: string;
}

export interface AuthorizeWalletPaymentRequest {
  walletTokenId: string;
  amount: number;
  currency: string;
  merchantId: string;
}

@Injectable({ providedIn: 'root' })
export class WalletsService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/wallets`;

  register(request: RegisterWalletTokenRequest): Observable<WalletEnrollment> {
    return this.http.post<WalletEnrollment>(`${this.base}/enrollments`, request);
  }

  activate(walletTokenId: string, request: ActivateWalletTokenRequest): Observable<WalletToken> {
    return this.http.post<WalletToken>(`${this.base}/enrollments/${walletTokenId}/activate`, request);
  }

  getByCard(cardId: string): Observable<WalletToken[]> {
    return this.http.get<WalletToken[]>(`${this.base}/cards/${cardId}/tokens`);
  }

  authorize(request: AuthorizeWalletPaymentRequest): Observable<WalletAuthorization> {
    return this.http.post<WalletAuthorization>(`${this.base}/authorizations`, request);
  }
}
