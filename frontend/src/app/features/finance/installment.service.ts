import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable } from 'rxjs';

export enum InstallmentPlanStatus {
    Active = 1,
    Completed = 2,
    Cancelled = 3,
    Delinquent = 4
}

export enum InstallmentStatus {
    Pending = 1,
    Invoiced = 2,
    Paid = 3,
    Skipped = 4
}

export interface AmortizationSchedule {
    id: string;
    installmentNumber: number;
    principalAmount: number;
    interestAmount: number;
    totalInstallmentAmount: number;
    dueDate: string;
    status: InstallmentStatus;
}

export interface InstallmentPlan {
    id: string;
    accountId: string;
    totalAmount: number;
    totalInstallments: number;
    remainingInstallments: number;
    interestApr: number;
    status: InstallmentPlanStatus;
    description: string;
    createdOn: string;
    amortizationSchedule: AmortizationSchedule[];
}

export interface DeferPurchaseRequest {
    accountId: string;
    ledgerEntryId: string;
    installments: number;
    apr?: number;
}

@Injectable({
  providedIn: 'root'
})
export class InstallmentService {
  private http = inject(HttpClient);
  private baseUrl = `${environment.apiUrl}/billing`;

  getPlans(accountId: string): Observable<InstallmentPlan[]> {
    return this.http.get<InstallmentPlan[]>(`${this.baseUrl}/accounts/${accountId}/installments`);
  }

  deferPurchase(request: DeferPurchaseRequest): Observable<InstallmentPlan> {
    return this.http.post<InstallmentPlan>(`${this.baseUrl}/installments/defer`, request);
  }
}
