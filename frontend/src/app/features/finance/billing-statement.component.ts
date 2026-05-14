import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { FinanceService, Statement } from './finance.service';
import { NotificationService } from '../../core/notification.service';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';

@Component({
  selector: 'app-billing-statement',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-container">
      <div class="page-header mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">fact_check</span>
            Estados de Cuenta y Facturación
          </h1>
          <p class="text-muted mt-1">Consulte los cortes de facturación, intereses y gestione pagos (Cashier).</p>
        </div>
      </div>

      <!-- Account Filter Header -->
      <div class="card mb-4 p-3 d-flex flex-row justify-content-between align-items-center" style="background-color: var(--primary-light)">
        <div class="d-flex align-items-center gap-3">
           <span class="material-symbols-rounded" style="color: var(--primary-dark)">account_balance_wallet</span>
           <div>
             <label class="text-muted text-sm font-weight-600 mb-1 d-block">CUENTA EN CONTEXTO:</label>
             <h3 class="m-0 text-main" style="letter-spacing: 1px">{{accountId || 'NO SELECCIONADA'}}</h3>
           </div>
        </div>
        <button class="btn btn-outline" (click)="refresh()">
          <span class="material-symbols-rounded">sync</span> Cargar / Refrescar Cortes
        </button>
      </div>

      <!-- Statements Grid -->
      <div class="card p-0">
        <div class="table-responsive">
          <table class="table">
            <thead>
              <tr>
                <th>FECHA DE CORTE</th>
                <th>VENCIMIENTO</th>
                <th class="text-right">CARGOS DEL MES (+)</th>
                <th class="text-right">PAGOS RECIBIDOS (-)</th>
                <th class="text-right text-primary-dark">PAGO MÍNIMO</th>
                <th class="text-right text-main">BALANCE CERRADO</th>
                <th class="text-center">RECAUDO</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let st of statements" [class.bg-light]="st.status === 2">
                <td class="font-weight-500">{{ st.statementDate | date:'mediumDate' }}</td>
                <td [class.text-danger]="isPastDue(st.dueDate) && st.status === 1">
                  {{ st.dueDate | date:'mediumDate' }}
                  <span class="material-symbols-rounded text-danger ms-1" style="font-size:14px; vertical-align:middle" *ngIf="isPastDue(st.dueDate) && st.status === 1">warning</span>
                </td>
                <td class="text-right font-weight-600 text-muted opacity-80">{{ (st.purchases + st.fees + st.interest) | currency }}</td>
                <td class="text-right font-weight-600 text-success">{{ (st.payments * -1) | currency }}</td>
                <td class="text-right font-weight-600 text-warning">{{ st.minimumPayment | currency }}</td>
                <td class="text-right font-weight-700 font-size-lg">{{ st.newBalance | currency }}</td>
                <td class="text-center">
                  <span class="status-badge" [ngClass]="st.status === 2 ? 'status-active' : 'status-blocked'">
                     {{st.status === 2 ? 'Cerrado/Pagado' : 'Pendiente'}}
                  </span>
                  <button class="icon-btn mt-1 d-block mx-auto text-primary" title="Procesar Pago" *ngIf="st.status === 1" (click)="openPaymentModal(st)">
                    <span class="material-symbols-rounded">payments</span> Pay
                  </button>
                </td>
              </tr>
              <tr *ngIf="statements.length === 0">
                <td colspan="7" class="text-center py-5 text-muted">
                  <span class="material-symbols-rounded" style="font-size: 48px; opacity: 0.5;">folder_open</span>
                  <p class="mt-2">No hay Estados de Cuenta generados.</p>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Payment Modal -->
      <div class="modal-overlay" [class.show]="isModalOpen">
        <div class="modal-card card">
          <div class="modal-header">
            <h3>Procesar Pago (Cashier)</h3>
            <button class="icon-btn" (click)="closePaymentModal()">
              <span class="material-symbols-rounded">close</span>
            </button>
          </div>
          <div class="modal-body" *ngIf="selectedStatement">
            <div class="d-flex justify-content-between mb-4 bg-light p-3 border-radius-md">
              <div class="text-center">
                 <span class="text-muted text-sm d-block">PAGO MÍNIMO</span>
                 <strong class="text-warning font-size-lg">{{selectedStatement.minimumPayment | currency}}</strong>
              </div>
              <div class="text-center">
                 <span class="text-muted text-sm d-block">PAGO DE CONTADO (TOTAL)</span>
                 <strong class="text-danger font-size-lg">{{selectedStatement.totalPaymentDue | currency}}</strong>
              </div>
            </div>
            
            <div class="input-group">
              <label>Monto a Pagar ($ USD)</label>
              <input type="number" class="form-control input-xl" placeholder="0.00" [(ngModel)]="paymentAmount">
            </div>
            <div class="d-flex gap-2">
              <button class="btn btn-outline btn-sm" (click)="shortcutPay(selectedStatement.minimumPayment)">Min</button>
              <button class="btn btn-outline btn-sm" (click)="shortcutPay(selectedStatement.totalPaymentDue)">Total</button>
            </div>
          </div>
          <div class="modal-footer d-flex justify-content-between">
            <button class="btn btn-outline" (click)="closePaymentModal()">Cancelar</button>
            <button class="btn btn-primary" (click)="submitPayment()">Postear Pago en Ledger</button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .bg-light { background-color: #f8fafc; }
    .border-radius-md { border-radius: var(--radius-md); }
    .font-size-lg { font-size: 1.15rem; }
    
    .table-responsive { overflow-x: auto; }
    .table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
    .table th, .table td { padding: 1rem 1.25rem; text-align: left; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
    .table th { color: var(--text-muted); font-weight: 600; font-size: 0.75rem; text-transform: uppercase; }
    .table tbody tr:hover { background-color: #fcfdfe; }
    .opacity-80 { opacity: 0.8; }
    
    .status-badge { padding: 0.25rem 0.5rem; border-radius: 4px; font-size: 0.7rem; font-weight: 600; display: inline-block; }
    .status-active { background: #ecfdf5; color: #047857; border: 1px solid #a7f3d0;}
    .status-blocked { background: #fef2f2; color: #b91c1c; border: 1px solid #fecaca;}

    .icon-btn { background: none; border: none; cursor: pointer; color: var(--text-muted); padding: 0; display: flex; align-items: center; justify-content: center; font-size: 0.75rem; font-weight: 600; gap: 0.25rem; transition: color 0.2s;}
    .icon-btn:hover { color: var(--primary) !important; }
    
    .input-xl { font-size: 1.5rem; font-weight: 700; padding: 1rem; text-align: right; color: var(--primary-dark); }
    .btn-sm { padding: 0.25rem 0.5rem; font-size: 0.75rem; }

    /* Modal */
    .modal-overlay { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.5); display: none; align-items: center; justify-content: center; z-index: 1000; backdrop-filter: blur(2px);}
    .modal-overlay.show { display: flex; }
    .modal-card { width: 100%; max-width: 450px; padding: 0; animation: fadeUp 0.3s ease; }
    .modal-header { display: flex; justify-content: space-between; align-items: center; padding: 1.5rem; border-bottom: 1px solid var(--border-color); }
    .modal-header h3 { margin: 0; font-size: 1.25rem; }
    .modal-body { padding: 1.5rem; }
    .modal-footer { padding: 1rem 1.5rem; border-top: 1px solid var(--border-color); background: var(--bg-main); border-radius: 0 0 var(--radius-lg) var(--radius-lg); }
    
    @keyframes fadeUp { from { transform: translateY(20px); opacity: 0; } to { transform: translateY(0); opacity: 1; } }
  `]
})
export class BillingStatementComponent implements OnInit {
  private fnService = inject(FinanceService);
  private route = inject(ActivatedRoute);
  private notifications = inject(NotificationService);

  accountId: string | null = null;
  statements: Statement[] = [];

  isModalOpen = false;
  selectedStatement: Statement | null = null;
  paymentAmount: number = 0;

  ngOnInit() {
    this.route.queryParams.subscribe(params => {
      if (params['account']) {
        this.accountId = params['account'];
        this.refresh();
      }
    });
  }

  isPastDue(dateString: string): boolean {
    return new Date(dateString) < new Date();
  }

  refresh() {
    if (!this.accountId) return;
    this.fnService.getStatements(this.accountId).pipe(
      catchError(err => {
        console.error('Error loading statements:', err?.status, err?.message);
        return of([] as Statement[]);
      })
    ).subscribe(data => this.statements = data);
  }

  openPaymentModal(st: Statement) {
    this.selectedStatement = st;
    this.paymentAmount = st.totalPaymentDue;
    this.isModalOpen = true;
  }

  closePaymentModal() {
    this.isModalOpen = false;
    this.selectedStatement = null;
    this.paymentAmount = 0;
  }

  shortcutPay(amount: number) {
    this.paymentAmount = amount;
  }

  submitPayment() {
    if (!this.selectedStatement || this.paymentAmount <= 0) return;

    this.fnService.payStatement(this.selectedStatement.id, this.paymentAmount).subscribe({
      next: () => {
        this.notifications.success(`Pago de $${this.paymentAmount} registrado exitosamente.`);
        this.closePaymentModal();
        this.refresh();
      },
      error: (err) => {
        console.error('Error posting payment:', err?.status, err?.message);
        this.notifications.error('Error al procesar el pago. Verificá que el backend esté disponible.');
      }
    });
  }
}
