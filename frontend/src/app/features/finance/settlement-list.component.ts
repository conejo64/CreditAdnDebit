import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettlementService, SettlementBatch } from './settlement.service';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';
import { NotificationService } from '../../core/notification.service';

@Component({
  selector: 'app-settlement-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-container">
      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">account_balance_wallet</span>
            Liquidación y Compensación (Settlement)
          </h1>
          <p class="text-muted mt-1">Gestione los lotes de compensación recibidos de las redes Visa/Mastercard.</p>
        </div>
        
        <div class="d-flex align-items-center gap-2">
          <select class="form-control form-control-sm" [(ngModel)]="runNetwork" style="width: 130px">
            <option value="VISA">VISA</option>
            <option value="MASTERCARD">MASTERCARD</option>
            <option value="AMEX">AMEX</option>
          </select>
          <input type="date" class="form-control form-control-sm" [(ngModel)]="runDate" style="width: 160px">
          <button class="btn btn-primary d-flex align-items-center gap-2" (click)="runSettlement()" [disabled]="isProcessing">
            <span class="material-symbols-rounded">{{ isProcessing ? 'sync' : 'play_circle' }}</span>
            {{ isProcessing ? 'Procesando...' : 'Ejecutar Liquidación' }}
          </button>
        </div>
      </div>

      <div class="card p-0">
        <div class="table-responsive">
          <table class="table">
            <thead>
              <tr>
                <th>RED</th>
                <th>FECHA NEGOCIO</th>
                <th>REGISTROS</th>
                <th class="text-right">MONTO TOTAL</th>
                <th>ESTADO</th>
                <th>CREADO EL</th>
                <th class="text-center">ACCIONES</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let batch of batches">
                <td>
                  <div class="d-flex align-items-center gap-2">
                    <span class="brand-icon" [ngClass]="batch.network.toLowerCase()"></span>
                    <span class="font-weight-600">{{ batch.network }}</span>
                  </div>
                </td>
                <td>{{ batch.businessDate | date:'mediumDate' }}</td>
                <td>{{ batch.itemCount }} txs</td>
                <td class="text-right font-weight-600 text-main">{{ batch.totalAmount | currency }}</td>
                <td>
                  <span class="role-badge" [ngClass]="getStatusClass(batch.status)">
                    {{ batch.status }}
                  </span>
                </td>
                <td class="text-muted">{{ batch.createdAt | date:'short' }}</td>
                <td class="text-center">
                   <button class="btn btn-icon" (click)="viewDetails(batch)" title="Ver Detalle">
                     <span class="material-symbols-rounded">visibility</span>
                   </button>
                   <button class="btn btn-icon" (click)="viewReconciliation(batch)" title="Ver Conciliación">
                     <span class="material-symbols-rounded">Compare_arrows</span>
                   </button>
                </td>
              </tr>
              <tr *ngIf="batches.length === 0">
                <td colspan="7" class="text-center py-5 text-muted">
                  <span class="material-symbols-rounded" style="font-size: 48px; opacity: 0.5;">account_balance_wallet</span>
                  <p class="mt-2">No se han procesado lotes de liquidación aún.</p>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <!-- Simple Detail Modal (Conditional Inline) -->
    <div class="modal-backdrop" *ngIf="selectedBatch">
      <div class="modal-card" style="max-width: 800px; width: 90%;">
        <div class="modal-header d-flex justify-content-between">
          <h3 class="m-0">Detalle de Lote: {{ selectedBatch.network }} - {{ selectedBatch.businessDate | date }}</h3>
          <button class="btn-close" (click)="selectedBatch = null">×</button>
        </div>
        <div class="modal-body p-0">
          <div class="table-responsive" style="max-height: 400px;">
            <table class="table table-sm">
              <thead class="bg-light">
                <tr>
                  <th>RRN</th>
                  <th>STAN</th>
                  <th class="text-right">MONTO</th>
                  <th>FECHA</th>
                  <th>ESTADO</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let item of batchItems">
                  <td class="font-weight-500">{{ item.rrn }}</td>
                  <td>{{ item.stan }}</td>
                  <td class="text-right">{{ item.amount | currency }}</td>
                  <td>{{ item.postedOn | date:'short' }}</td>
                  <td><span class="text-success text-xs font-weight-600">{{ item.status }}</span></td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
        <div class="modal-footer d-flex justify-content-end p-3">
          <button class="btn btn-outline" (click)="selectedBatch = null">Cerrar</button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .font-weight-600 { font-weight: 600; }
    .text-main { color: var(--text-main); }
    .text-xs { font-size: 0.75rem; }
    
    .table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
    .table th, .table td { padding: 1rem 1.5rem; text-align: left; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
    .table th { color: var(--text-muted); font-weight: 600; font-size: 0.75rem; text-transform: uppercase; }
    .table-sm th, .table-sm td { padding: 0.75rem 1rem; }
    
    .brand-icon { width: 32px; height: 20px; display: inline-block; background-size: contain; background-repeat: no-repeat; background-position: center; }
    .visa { background-image: url('https://upload.wikimedia.org/wikipedia/commons/5/5e/Visa_Inc._logo.svg'); }
    .mastercard { background-image: url('https://upload.wikimedia.org/wikipedia/commons/2/2a/Mastercard-logo.svg'); }
    
    .role-badge { padding: 0.25rem 0.6rem; border-radius: var(--radius-sm); font-size: 0.70rem; font-weight: 600; text-transform: uppercase; }
    .status-processed { background: #ecfdf5; color: #047857; }
    .status-error { background: #fef2f2; color: #b91c1c; }
    .status-received { background: #e0e7ff; color: #4338ca; }

    .modal-backdrop {
      position: fixed; top: 0; left: 0; width: 100%; height: 100%;
      background: rgba(0,0,0,0.5); display: flex; align-items: center; justify-content: center; z-index: 1000;
    }
    .modal-card { background: white; border-radius: 8px; box-shadow: 0 10px 25px rgba(0,0,0,0.2); overflow: hidden; }
    .modal-header { padding: 1.25rem; border-bottom: 1px solid #eee; }
    .btn-close { border: none; background: none; font-size: 1.5rem; cursor: pointer; }
  `]
})
export class SettlementListComponent implements OnInit {
  private settlementService = inject(SettlementService);
  private notifications = inject(NotificationService);

  batches: SettlementBatch[] = [];
  selectedBatch: SettlementBatch | null = null;
  batchItems: any[] = [];
  isProcessing = false;
  runNetwork = 'VISA';
  runDate = new Date().toISOString().split('T')[0];

  ngOnInit() {
    this.loadBatches();
  }

  loadBatches() {
    this.settlementService.getBatches().pipe(
      catchError(err => {
        console.error('Error loading batches:', err?.status, err?.message);
        this.notifications.warning('No se pudo cargar la lista de lotes');
        return of([] as SettlementBatch[]);
      })
    ).subscribe(data => {
      this.batches = data;
    });
  }

  runSettlement() {
    this.isProcessing = true;
    this.settlementService.runSettlement(this.runNetwork, this.runDate).subscribe({
      next: () => {
        this.isProcessing = false;
        this.notifications.success('Proceso de liquidación completado');
        this.loadBatches();
      },
      error: (err) => {
        this.isProcessing = false;
        console.error('Error running settlement:', err?.status, err?.message);
        this.notifications.error('Error al ejecutar liquidación. Verifique la conexión.');
      }
    });
  }

  getStatusClass(status: string): string {
    switch (status) {
      case 'PROCESSED': return 'status-processed';
      case 'ERROR': return 'status-error';
      case 'RECEIVED': return 'status-received';
      default: return '';
    }
  }

  viewDetails(batch: SettlementBatch) {
    this.selectedBatch = batch;
    this.settlementService.getBatchDetails(batch.id).pipe(
      catchError(err => {
        console.error('Error loading batch details:', err?.status, err?.message);
        this.notifications.warning('No se pudieron cargar los detalles del lote');
        return of([]);
      })
    ).subscribe(items => {
      this.batchItems = items;
    });
  }

  viewReconciliation(batch: SettlementBatch) {
    this.notifications.info(`Iniciando reconciliación para el lote ${batch.id}...`);
  }
}
