import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable } from 'rxjs';

export interface RewardProgram {
  id: string;
  name: string;
  description: string;
  pointsPerDollar: number;
  cashbackPercent: number;
  isActive: boolean;
}

export interface RewardCatalogItem {
  id: string;
  name: string;
  description: string;
  pointsCost: number;
  category: string;
  isAvailable: boolean;
}

export interface LoyaltyBalance {
  accountId: string;
  totalPoints: number;
  availablePoints: number;
  redeemedPoints: number;
  cashbackAccrued: number;
  programName: string;
}

export interface LoyaltyEntry {
  id: string;
  accountId: string;
  type: string;
  points: number;
  description: string;
  occurredAt: string;
}

export interface UpsertRewardProgramRequest {
  name: string;
  description: string;
  pointsPerDollar: number;
  cashbackPercent: number;
}

export interface UpsertRewardCatalogItemRequest {
  name: string;
  description: string;
  pointsCost: number;
  category: string;
}

export interface RedeemRewardRequest {
  catalogItemId: string;
  points: number;
}

@Injectable({ providedIn: 'root' })
export class LoyaltyService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/loyalty`;

  getPrograms(): Observable<RewardProgram[]> {
    return this.http.get<RewardProgram[]>(`${this.base}/programs`);
  }

  upsertProgram(request: UpsertRewardProgramRequest): Observable<RewardProgram> {
    return this.http.post<RewardProgram>(`${this.base}/programs`, request);
  }

  getCatalog(): Observable<RewardCatalogItem[]> {
    return this.http.get<RewardCatalogItem[]>(`${this.base}/catalog`);
  }

  upsertCatalogItem(request: UpsertRewardCatalogItemRequest): Observable<RewardCatalogItem> {
    return this.http.post<RewardCatalogItem>(`${this.base}/catalog`, request);
  }

  getBalance(accountId: string): Observable<LoyaltyBalance> {
    return this.http.get<LoyaltyBalance>(`${this.base}/accounts/${accountId}/balance`);
  }

  getEntries(accountId: string, take = 30): Observable<LoyaltyEntry[]> {
    return this.http.get<LoyaltyEntry[]>(`${this.base}/accounts/${accountId}/entries`, { params: { take } });
  }

  redeem(accountId: string, request: RedeemRewardRequest): Observable<LoyaltyBalance> {
    return this.http.post<LoyaltyBalance>(`${this.base}/accounts/${accountId}/redeem`, request);
  }
}
