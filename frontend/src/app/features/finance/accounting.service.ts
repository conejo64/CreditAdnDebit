import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable } from 'rxjs';

export interface JournalLine {
  id: string;
  accountCode: string;
  accountName: string;
  debit: number;
  credit: number;
}

export interface JournalEntry {
  id: string;
  businessDate: string;
  sourceModule: string;
  sourceReference: string;
  description: string;
  postedAt: string;
  lines: JournalLine[];
}

export interface LedgerAccount {
  id: string;
  code: string;
  name: string;
  type: string;
  isActive: boolean;
}

export interface AccountingMapping {
  id: string;
  eventType: string;
  debitAccountCode: string;
  creditAccountCode: string;
  isActive: boolean;
}

export interface UpsertLedgerAccountRequest {
  code: string;
  name: string;
  type: string;
}

export interface UpsertAccountingMappingRequest {
  eventType: string;
  debitAccountCode: string;
  creditAccountCode: string;
}

@Injectable({ providedIn: 'root' })
export class AccountingService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/accounting`;

  getJournalEntries(take = 50): Observable<JournalEntry[]> {
    return this.http.get<JournalEntry[]>(`${this.base}/journal-entries`, { params: { take } });
  }

  getJournalEntry(id: string): Observable<JournalEntry> {
    return this.http.get<JournalEntry>(`${this.base}/journal-entries/${id}`);
  }

  getLedgerAccounts(): Observable<LedgerAccount[]> {
    return this.http.get<LedgerAccount[]>(`${this.base}/ledger-accounts`);
  }

  upsertLedgerAccount(request: UpsertLedgerAccountRequest): Observable<any> {
    return this.http.post(`${this.base}/ledger-accounts`, request);
  }

  getMappings(): Observable<AccountingMapping[]> {
    return this.http.get<AccountingMapping[]>(`${this.base}/mappings`);
  }

  upsertMapping(request: UpsertAccountingMappingRequest): Observable<any> {
    return this.http.post(`${this.base}/mappings`, request);
  }
}
