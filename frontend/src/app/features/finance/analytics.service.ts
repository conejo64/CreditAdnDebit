import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable } from 'rxjs';

export interface AnalyticsBreakdownItem {
  key: string;
  amount: number;
  count: number;
  sharePercent: number;
}

export interface AnalyticsTimeSeriesPoint {
  date: string;
  amount: number;
  count: number;
}

export interface AnalyticsPortfolioSummary {
  customers: number;
  accounts: number;
  activeAccounts: number;
  activeCards: number;
  totalCreditLimit: number;
  availableCredit: number;
  outstandingBalance: number;
  openStatementBalance: number;
  openDisputeCount: number;
  openDisputeAmount: number;
}

export interface ConsumptionAnalytics {
  days: number;
  fromDate: string;
  toDate: string;
  grossConsumptionAmount: number;
  netConsumptionAmount: number;
  movementCount: number;
  averageTicket: number;
  categoryBreakdown: AnalyticsBreakdownItem[];
  productBreakdown: AnalyticsBreakdownItem[];
  networkBreakdown: AnalyticsBreakdownItem[];
  dailyTrend: AnalyticsTimeSeriesPoint[];
}

export interface FraudAnalytics {
  days: number;
  fromDate: string;
  toDate: string;
  totalCases: number;
  openCases: number;
  wonCases: number;
  lostCases: number;
  totalExposureAmount: number;
  openExposureAmount: number;
  casesPerThousandPurchases: number;
  networkBreakdown: AnalyticsBreakdownItem[];
  reasonCodeBreakdown: AnalyticsBreakdownItem[];
  statusBreakdown: AnalyticsBreakdownItem[];
  openedTrend: AnalyticsTimeSeriesPoint[];
}

export interface AnalyticsDashboard {
  portfolio: AnalyticsPortfolioSummary;
  consumption: ConsumptionAnalytics;
  fraud: FraudAnalytics;
}

@Injectable({ providedIn: 'root' })
export class AnalyticsService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/analytics`;

  getDashboard(days = 30): Observable<AnalyticsDashboard> {
    return this.http.get<AnalyticsDashboard>(`${this.base}/dashboard`, { params: { days } });
  }

  getConsumption(days = 30): Observable<ConsumptionAnalytics> {
    return this.http.get<ConsumptionAnalytics>(`${this.base}/consumption`, { params: { days } });
  }

  getFraud(days = 30): Observable<FraudAnalytics> {
    return this.http.get<FraudAnalytics>(`${this.base}/fraud`, { params: { days } });
  }
}
