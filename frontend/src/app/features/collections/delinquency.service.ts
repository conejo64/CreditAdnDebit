import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable } from 'rxjs';

export interface DelinquencyRecord {
  id: string;
  accountId: string;
  statementId: string;
  overdueAmount: number;
  daysInArrears: number;
  /** Numeric bucket value: 1=1-30, 2=31-60, 3=61-90, 4=>90 */
  bucket: number;
  /** Human-readable bucket label e.g. "1-30 days" */
  bucketLabel: string;
  status: string;
  createdOn: string;
  updatedOn: string;
  resolvedOn: string | null;
}

/**
 * PagedResult<T> — kept local to the collections feature module (v76-mora-temprana decision).
 *
 * Decision: do NOT promote to BuildingBlocks yet. Rationale: only one feature currently
 * uses pagination. Premature promotion would couple the frontend shared layer to a single
 * feature's contract. Revisit when a second feature requires the same shape — at that point
 * extract to `shared/models/paged-result.model.ts` and update imports.
 */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

// ─────────────────────────────────────────────
// v77 — Collections mutation interfaces
// ─────────────────────────────────────────────

export interface ContactAttempt {
  id: string;
  delinquencyRecordId: string;
  channel: string;
  outcome: string;
  notes: string | null;
  attemptedBy: string;
  attemptedOn: string;
}

export interface DelinquencyNote {
  id: string;
  delinquencyRecordId: string;
  content: string;
  createdBy: string;
  createdOn: string;
}

@Injectable({ providedIn: 'root' })
export class DelinquencyService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/collections/delinquencies`;

  getDelinquencies(
    page: number,
    pageSize: number,
    bucket?: number
  ): Observable<PagedResult<DelinquencyRecord>> {
    let params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());

    if (bucket !== undefined && bucket !== null) {
      params = params.set('bucket', bucket.toString());
    }

    return this.http.get<PagedResult<DelinquencyRecord>>(this.base, { params });
  }

  // ─────────────────────────────────────────────
  // v77 — Mutation methods
  // ─────────────────────────────────────────────

  registerContactAttempt(
    delinquencyRecordId: string,
    channel: string,
    outcome: string,
    notes?: string
  ): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(
      `${this.base}/${delinquencyRecordId}/contact-attempts`,
      { channel, outcome, notes }
    );
  }

  getContactAttempts(delinquencyRecordId: string): Observable<ContactAttempt[]> {
    return this.http.get<ContactAttempt[]>(
      `${this.base}/${delinquencyRecordId}/contact-attempts`
    );
  }

  addNote(delinquencyRecordId: string, content: string): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(
      `${this.base}/${delinquencyRecordId}/notes`,
      { content }
    );
  }

  getNotes(delinquencyRecordId: string): Observable<DelinquencyNote[]> {
    return this.http.get<DelinquencyNote[]>(
      `${this.base}/${delinquencyRecordId}/notes`
    );
  }
}
