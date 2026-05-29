import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { DelinquencyService, DelinquencyRecord, PagedResult } from './delinquency.service';
import { catchError, EMPTY } from 'rxjs';

@Component({
  selector: 'app-delinquency-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  template: `
    <div class="page-container">
      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">warning</span>
            Mora Temprana
          </h1>
          <p class="text-muted mt-1">Cuentas en mora con sus respectivos buckets de antigüedad.</p>
        </div>

        <div class="d-flex align-items-center gap-2">
          <select class="form-input" style="width: 180px" [(ngModel)]="selectedBucket" (ngModelChange)="onBucketChange($event)">
            <option [ngValue]="undefined">Todos los buckets</option>
            <option [ngValue]="1">1-30 días</option>
            <option [ngValue]="2">31-60 días</option>
            <option [ngValue]="3">61-90 días</option>
            <option [ngValue]="4">&gt;90 días</option>
          </select>
          <button class="btn btn-outline d-flex align-items-center gap-2" (click)="loadData()">
            <span class="material-symbols-rounded">refresh</span>
          </button>
        </div>
      </div>

        <div class="card p-0">
          <div *ngIf="errorState" class="error-banner" data-testid="auth-error">
            <span class="material-symbols-rounded">lock</span>
            <span>
              <ng-container *ngIf="errorStatus === 403">No tenés permiso para acceder a esta sección (403 Forbidden).</ng-container>
              <ng-container *ngIf="errorStatus !== 403">Ocurrió un error al cargar los datos ({{ errorStatus }}). Intentá de nuevo.</ng-container>
            </span>
          </div>
          <div class="table-responsive" *ngIf="!errorState">
          <table class="table">
            <thead>
              <tr>
                <th>CUENTA</th>
                <th>ESTADO</th>
                <th>BUCKET</th>
                <th class="text-right">DÍAS EN MORA</th>
                <th class="text-right">MONTO VENCIDO</th>
                <th>CREADO</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let record of records" data-testid="delinquency-row">
                <td class="font-weight-500">{{ record.accountId }}</td>
                <td>
                  <span class="role-badge status-active">{{ record.status }}</span>
                </td>
                <td>
                  <span class="role-badge" [ngClass]="getBucketClass(record.bucket)" data-testid="bucket-badge">
                    {{ record.bucketLabel }}
                  </span>
                </td>
                <td class="text-right font-weight-600 text-danger">{{ record.daysInArrears }}</td>
                <td class="text-right font-weight-600">{{ record.overdueAmount | currency }}</td>
                <td class="text-muted">{{ record.createdOn | date:'short' }}</td>
                <td>
                  <a [routerLink]="['/app/collections/delinquencies', record.id]"
                     class="btn btn-outline btn-sm" data-testid="view-details-link">
                    Ver detalle
                  </a>
                </td>
              </tr>
              <tr *ngIf="records.length === 0">
                <td colspan="7" class="text-center py-5 text-muted" data-testid="empty-state">
                  <span class="material-symbols-rounded" style="font-size: 48px; opacity: 0.5;">warning</span>
                  <p class="mt-2">No se encontraron cuentas en mora.</p>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <div class="card-footer bg-light p-3 border-top d-flex justify-content-between align-items-center" *ngIf="totalCount > 0 && !errorState">
          <span class="text-muted text-sm">
            Mostrando {{ (currentPage - 1) * pageSize + 1 }}–{{ getUpperBound() }} de {{ totalCount }} registros
          </span>
          <div class="d-flex gap-2">
            <button class="btn btn-outline btn-sm" [disabled]="currentPage <= 1" (click)="prevPage()">
              <span class="material-symbols-rounded">chevron_left</span>
            </button>
            <span class="text-muted text-sm d-flex align-items-center px-2">{{ currentPage }} / {{ totalPages }}</span>
            <button class="btn btn-outline btn-sm" [disabled]="currentPage >= totalPages" (click)="nextPage()">
              <span class="material-symbols-rounded">chevron_right</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .bg-light { background-color: #f8fafc; }
    .table-responsive { overflow-x: auto; }
    .table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
    .table th, .table td { padding: 1rem 1.5rem; text-align: left; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
    .table th { color: var(--text-muted); font-weight: 600; font-size: 0.75rem; text-transform: uppercase; }
    .table tbody tr:hover { background-color: #fafbfc; }
    .role-badge { padding: 0.25rem 0.6rem; border-radius: var(--radius-sm); font-size: 0.70rem; font-weight: 600; text-transform: uppercase; }
    .status-active { background: #ecfdf5; color: #047857; }
    .bucket-1 { background: #fffbeb; color: #b45309; }
    .bucket-2 { background: #fef3c7; color: #d97706; }
    .bucket-3 { background: #fee2e2; color: #dc2626; }
    .bucket-4 { background: #7f1d1d; color: #fca5a5; }
    .font-weight-500 { font-weight: 500; }
    .font-weight-600 { font-weight: 600; }
    .text-sm { font-size: 0.75rem; }
    .text-danger { color: var(--danger, #dc2626); }
    .error-banner { display: flex; align-items: center; gap: 0.75rem; padding: 1.25rem 1.5rem; background: #fef2f2; color: #991b1b; border-radius: var(--radius-sm); font-size: 0.875rem; }
  `]
})
export class DelinquencyListComponent implements OnInit {
  private delinquencyService = inject(DelinquencyService);

  records: DelinquencyRecord[] = [];
  totalCount = 0;
  currentPage = 1;
  pageSize = 20;
  totalPages = 1;
  selectedBucket: number | undefined = undefined;
  errorState = false;
  errorStatus: number | null = null;

  ngOnInit(): void {
    this.loadData();
  }

  loadData(): void {
    this.errorState = false;
    this.errorStatus = null;
    this.delinquencyService.getDelinquencies(this.currentPage, this.pageSize, this.selectedBucket)
      .pipe(
        catchError((err: HttpErrorResponse) => {
          this.errorState = true;
          this.errorStatus = err.status ?? null;
          return EMPTY;
        })
      )
      .subscribe(result => {
        this.records = result.items;
        this.totalCount = result.totalCount;
        this.totalPages = result.totalPages;
      });
  }

  onBucketChange(bucket: number | undefined): void {
    this.selectedBucket = bucket;
    this.currentPage = 1;
    this.loadData();
  }

  nextPage(): void {
    if (this.currentPage < this.totalPages) {
      this.currentPage++;
      this.loadData();
    }
  }

  prevPage(): void {
    if (this.currentPage > 1) {
      this.currentPage--;
      this.loadData();
    }
  }

  getBucketClass(bucket: number): string {
    return `bucket-${bucket}`;
  }

  getUpperBound(): number {
    return Math.min(this.currentPage * this.pageSize, this.totalCount);
  }
}
