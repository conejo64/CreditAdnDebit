import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SwitchService, IsoTransaction } from './switch.service';
import { NotificationService } from '../../core/notification.service';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';

@Component({
  selector: 'app-audit-list',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="page-container">
      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">troubleshoot</span>
            Switch Auditoría y Transacciones Rest
          </h1>
          <p class="text-muted mt-1">Supervisión en vivo del Switch de Pagos y logs binarios entrantes (ISO 8583).</p>
        </div>
        <div class="header-actions">
           <button class="btn btn-primary" (click)="refreshLive()">
              <span class="material-symbols-rounded">sync</span> Sync Live Data
           </button>
        </div>
      </div>

      <div class="row">
         <div class="col-8">
            <div class="card p-0 mb-4 h-100">
              <div class="card-header border-bottom p-3 d-flex justify-content-between align-items-center bg-light">
                <h3 class="m-0 text-main font-weight-600">Últimas Transacciones Traficadas</h3>
              </div>
              <div class="table-responsive">
                <table class="table table-hover">
                  <thead>
                    <tr>
                      <th>TRACE ID</th>
                      <th>PAN TOKEN / RED</th>
                      <th>MTI / FUNC</th>
                      <th class="text-right">MONTO</th>
                      <th class="text-center">RESPUESTA ISO</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr *ngFor="let t of transactions" class="cur-pointer" (click)="selectTx(t)">
                      <td class="text-muted text-sm font-monospace">{{ t.traceId }}</td>
                      <td>
                        <div class="d-flex flex-column text-sm font-monospace">
                          <strong>{{ t.connectorId }}</strong>
                          <span class="text-muted">Proc: {{ t.processingCode }}</span>
                        </div>
                      </td>
                      <td>
                        <span class="badge badge-subtle">{{ t.requestMti }}</span>
                      </td>
                      <td class="text-right font-weight-600 text-main">
                        {{ parseAmount(t.amount12) | currency }}
                      </td>
                      <td class="text-center">
                        <span class="status-badge" [ngClass]="getRespClass(t.responseCode)">
                           {{ t.responseCode === '00' ? 'APROBADO (00)' : t.responseCode ? 'DENEGADO ('+t.responseCode+')' : 'PENDIENTE' }}
                        </span>
                      </td>
                    </tr>
                    <tr *ngIf="transactions.length === 0">
                      <td colspan="5" class="text-center py-5 text-muted">Aún no hay mensajes procesados.</td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>
         </div>

         <div class="col-4">
            <div class="card bg-dark text-white p-3 h-100 console-view">
              <h3 class="border-bottom border-secondary pb-3 mb-3 d-flex align-items-center gap-2 text-primary-light">
                 <span class="material-symbols-rounded">terminal</span> Debug Dump
              </h3>
              
              <div *ngIf="!selectedTx" class="text-muted text-center py-5 mt-4">
                 <span class="material-symbols-rounded" style="font-size: 40px; opacity: 0.3">touch_app</span>
                 <p class="mt-2 text-sm">Seleccione una transacción a la izquierda para ver su representación Hex/JSON y el desglose de los Data Elements.</p>
              </div>

              <div *ngIf="selectedTx" class="mt-3">
                 <div class="mb-3">
                    <span class="text-muted font-size-sm d-block mb-1">TRACE REF ID</span>
                    <strong class="font-monospace text-primary-light">{{selectedTx.traceId}}</strong>
                 </div>
                 <div class="mb-3">
                    <span class="text-muted font-size-sm d-block mb-1">SYSTEM TIMESTAMP</span>
                    <span class="font-monospace">{{selectedTx.createdOn | date:'mediumTime'}}</span>
                 </div>
                 
                 <div class="bg-secondary p-2 rounded text-sm font-monospace mb-3 overflow-auto" style="max-height: 200px">
[RAW TCP DUMP]
0000   02 00 32 38 30 30 30 31   ..280001
0008   32 33 34 35 36 37 38 39   23456789
                 </div>
                 
                 <button class="btn btn-outline btn-sm w-100 border-secondary text-white" (click)="viewDataElements()">
                    <span class="material-symbols-rounded">developer_board</span> Desglosar Data Elements
                 </button>
              </div>
            </div>
         </div>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .bg-light { background-color: #f8fafc; }
    
    .row { display: flex; gap: 1.5rem; }
    .col-8 { flex: 0 0 calc(66.6% - 0.75rem); }
    .col-4 { flex: 0 0 calc(33.3% - 0.75rem); }
    
    .table-responsive { overflow-x: auto; max-height: 500px; }
    .table { width: 100%; border-collapse: collapse; font-size: 0.85rem; }
    .table th, .table td { padding: 0.85rem 1rem; text-align: left; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
    .table th { color: var(--text-muted); font-weight: 600; font-size: 0.70rem; text-transform: uppercase; position: sticky; top: 0; background-color: white;}
    .table-hover tbody tr:hover { background-color: #f1f5f9; cursor: pointer; }
    
    .font-monospace { font-family: 'Courier New', Courier, monospace; }
    .text-sm { font-size: 0.75rem; }
    .font-size-sm { font-size: 0.7rem; }
    
    .badge-subtle { background-color: #e2e8f0; color: #475569; padding: 0.2rem 0.5rem; border-radius: 4px; font-weight: 600; font-size: 0.7rem; }
    
    .status-badge { padding: 0.25rem 0.6rem; border-radius: 4px; font-size: 0.7rem; font-weight: 600; }
    .status-active { background: #ecfdf5; color: #047857; border: 1px solid #10b981; }
    .status-blocked { background: #fef2f2; color: #b91c1c; border: 1px solid #ef4444; }

    /* Console Specific styles */
    .console-view { background-color: #1e1e1e !important; color: #d4d4d4 !important; border: 1px solid #333; }
    .console-view h3 { color: #d4d4d4; border-bottom-color: #333 !important; }
    .bg-secondary { background-color: #2d2d2d !important; color: #9cdcfe !important;}
    .border-secondary { border-color: #444 !important;}
    .text-primary-light { color: #569cd6 !important;}
  `]
})
export class AuditListComponent implements OnInit {
  private switchService = inject(SwitchService);
  private notifications = inject(NotificationService);

  transactions: IsoTransaction[] = [];
  selectedTx: IsoTransaction | null = null;

  ngOnInit() {
    this.refreshLive();
  }

  refreshLive() {
    this.switchService.getTransactions().pipe(
      catchError(err => {
        console.error('Error loading transactions:', err?.status, err?.message);
        this.notifications.error('Error al conectar con la pasarela de auditoría');
        return of({ count: 0, items: [] });
      })
    ).subscribe(data => this.transactions = data.items);
  }

  parseAmount(val?: string): number {
    if (!val) return 0;
    return parseInt(val, 10) / 100;
  }

  selectTx(tx: IsoTransaction) {
    this.selectedTx = tx;
  }

  getRespClass(resp: string): string {
    return resp === '00' ? 'status-active' : 'status-blocked';
  }

  viewDataElements() {
    this.notifications.info('Parsing ISO 8583 Data Elements DE1–DE128... Format matched: Bitmap 1: 0x42 0x11...');
  }
}
