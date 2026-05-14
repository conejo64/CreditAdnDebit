import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable } from 'rxjs';

export interface OverlimitEvent {
  id: string;
  accountId: string;
  occurredAt: string;
  requestedAmount: number;
  currentLimit: number;
  overAmount: number;
  action: string;
}

export interface CreditLimitEvaluation {
  accountId: string;
  currentLimit: number;
  recommendedLimit: number;
  score: number;
  rationale: string;
  proposalId: string | null;
}

export interface CreditLimitProposal {
  id: string;
  accountId: string;
  currentLimit: number;
  proposedLimit: number;
  status: string;
  rationale: string;
  createdAt: string;
  appliedAt: string | null;
}

@Injectable({ providedIn: 'root' })
export class CreditLimitService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/credit-limits`;

  getOverlimitEvents(accountId: string, take = 20): Observable<OverlimitEvent[]> {
    return this.http.get<OverlimitEvent[]>(`${this.base}/accounts/${accountId}/overlimit-events`, { params: { take } });
  }

  evaluate(accountId: string): Observable<CreditLimitEvaluation> {
    return this.http.post<CreditLimitEvaluation>(`${this.base}/accounts/${accountId}/evaluate`, {});
  }

  getProposals(status?: string, take = 50): Observable<CreditLimitProposal[]> {
    const params: any = { take };
    if (status) params['status'] = status;
    return this.http.get<CreditLimitProposal[]>(`${this.base}/proposals`, { params });
  }

  applyProposal(proposalId: string): Observable<CreditLimitProposal> {
    return this.http.post<CreditLimitProposal>(`${this.base}/proposals/${proposalId}/apply`, {});
  }
}
