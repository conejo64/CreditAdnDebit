import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable } from 'rxjs';

export interface CustomerNotification {
  id: string;
  customerId: string | null;
  accountId: string | null;
  type: string;
  channel: string;
  subject: string;
  body: string;
  sentAt: string;
  status: string;
}

@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/notifications`;

  list(filters: { customerId?: string; accountId?: string; type?: string; take?: number }): Observable<CustomerNotification[]> {
    const params: any = { take: filters.take ?? 50 };
    if (filters.customerId) params['customerId'] = filters.customerId;
    if (filters.accountId) params['accountId'] = filters.accountId;
    if (filters.type) params['type'] = filters.type;
    return this.http.get<CustomerNotification[]>(this.base, { params });
  }

  get(id: string): Observable<CustomerNotification> {
    return this.http.get<CustomerNotification>(`${this.base}/${id}`);
  }
}
