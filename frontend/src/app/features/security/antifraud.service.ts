import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable } from 'rxjs';

export enum AntifraudRuleType {
    BlockCountry = 1,
    MonitorCountry = 2,
    BlockMerchant = 3,
    RiskScoreMultiplier = 4,
    VelocityPerCard = 5
}

export interface AntifraudRule {
    id: string;
    type: AntifraudRuleType;
    targetValue: string;
    riskScore: number;
    isEnabled: boolean;
    description: string;
    createdOn: string;
}

@Injectable({
  providedIn: 'root'
})
export class AntifraudService {
  private http = inject(HttpClient);
  private baseUrl = `${environment.apiUrl}/risk/rules`;

  getRules(): Observable<AntifraudRule[]> {
    return this.http.get<AntifraudRule[]>(this.baseUrl);
  }

  upsertRule(rule: Partial<AntifraudRule>): Observable<AntifraudRule> {
    return this.http.post<AntifraudRule>(this.baseUrl, rule);
  }

  deleteRule(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
