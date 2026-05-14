import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { FinanceService, LedgerEntry, LedgerEntryType } from './finance.service';
import { InstallmentService } from './installment.service';
import { CustomerService, Customer, Account } from '../issuer/customers/customer.service';
import { NotificationService } from '../../core/notification.service';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';

import { SearchSelectComponent } from '../../shared/components/search-select.component';

@Component({
  selector: 'app-ledger-list',
  standalone: true,
  imports: [CommonModule, FormsModule, SearchSelectComponent],
  template: `
    <div class="page-container">
      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">receipt_long</span>
            Movimientos Contables (Ledger)
          </h1>
          <p class="text-muted mt-1">Consulte el registro histórico y balance contable de las cuentas emitidas.</p>
        </div>
        
        <!-- Search selectors inline -->
        <div class="d-flex align-items-center gap-2">
          <div style="width: 280px;">
            <app-search-select 
              [options]="customers"
              [(value)]="selectedCustomerId"
              (valueChange)="onCustomerChange($event)"
              labelKey="fullName"
              subLabelKey="documentId"
              placeholder="Buscar Cliente..."
            ></app-search-select>
          </div>
          
          <div style="width: 250px;">
            <app-search-select 
              [options]="accountOptions"
              [(value)]="selectedAccountId"
              labelKey="label"
              [disabled]="!selectedCustomerId"
              placeholder="Seleccione Cuenta..."
            ></app-search-select>
          </div>

          <button class="btn btn-primary btn-sm ms-2" [disabled]="!selectedAccountId" (click)="loadLedger(selectedAccountId)">Consultar</button>
        </div>
      </div>

      <div class="card p-0" *ngIf="loadedAccountId">
        <div class="card-header border-bottom p-3 d-flex justify-content-between align-items-center bg-light">
           <div>
             <h3 class="m-0 text-main font-weight-600">Cuenta: {{loadedAccountId}}</h3>
             <small class="text-muted">Mostrando últimos 50 movimientos</small>
           </div>
           <div class="d-flex gap-2">
              <button class="btn btn-outline" (click)="viewStatements()">
                <span class="material-symbols-rounded text-primary">request_quote</span> Ver Estados de Cuenta
              </button>
              <button class="btn btn-outline" (click)="simulateTx()">
                <span class="material-symbols-rounded text-success">add_shopping_cart</span> Simular Compra
              </button>
           </div>
        </div>

        <div class="table-responsive">
          <table class="table">
            <thead>
              <tr>
                <th>FECHA POSTEO</th>
                <th>DESCRIPCIÓN</th>
                <th>TIPO</th>
                <th class="text-right">CARGO (+)</th>
                <th class="text-right">ABONO (-)</th>
                <th>ACCIÓN</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let entry of entries">
                <td class="text-muted">{{ entry.postedOn | date:'short' }}</td>
                <td class="font-weight-500">{{ entry.description }}</td>
                <td>
                  <span class="role-badge" [ngClass]="getEntryClass(entry.type)">
                    {{ getEntryName(entry.type) }}
                  </span>
                </td>
                <td class="text-right font-weight-600" [ngClass]="{'text-main': entry.amount > 0, 'text-muted opacity-50': entry.amount <= 0}">
                  {{ entry.amount > 0 ? (entry.amount | currency) : '' }}
                </td>
                <td class="text-right font-weight-600" [ngClass]="{'text-success': entry.amount < 0, 'text-muted opacity-50': entry.amount >= 0}">
                  {{ entry.amount < 0 ? ((entry.amount * -1) | currency) : '' }}
                </td>
                <td>
                  <button class="btn btn-outline btn-sm py-1 px-2" 
                    *ngIf="canDefer(entry)" 
                    (click)="deferEntry(entry)" 
                    title="Diferir a Cuotas">
                    <span class="material-symbols-rounded" style="font-size: 16px">account_tree</span>
                  </button>
                </td>
              </tr>
              <tr *ngIf="entries.length === 0">
                <td colspan="5" class="text-center py-5 text-muted">
                  <span class="material-symbols-rounded" style="font-size: 48px; opacity: 0.5;">receipt_long</span>
                  <p class="mt-2">No se encontraron movimientos contables en este Ledger.</p>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        
        <div class="card-footer bg-light p-3 border-top d-flex justify-content-end align-items-center gap-4">
             <div class="text-right">
                <span class="text-muted text-sm d-block">BALANCE ACTUAL</span>
                <h2 class="m-0 text-main" [class.text-danger]="calculateBalance() > 0" [class.text-success]="calculateBalance() <= 0">
                    {{ calculateBalance() | currency }}
                </h2>
             </div>
        </div>
      </div>
      
      <div class="empty-state" *ngIf="!loadedAccountId">
         <span class="material-symbols-rounded text-muted" style="font-size: 64px; opacity: 0.3">receipt_long</span>
         <h3 class="text-muted mt-3">Seleccione una cuenta para consultar su Ledger</h3>
         <p class="text-muted">Utilice los filtros superiores para ubicar al cliente y sus cuentas activas.</p>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .search-box { display: flex; align-items: center; background-color: var(--bg-paper); border: 1px solid var(--border-color); border-radius: var(--radius-md); padding: 0.5rem 1rem; width: 450px;}
    .search-icon { color: var(--text-muted); font-size: 20px;}
    .search-input { border: none; background: transparent; outline: none; width: 100%; margin-left: 0.5rem; font-family: inherit;}
    .ms-2 { margin-left: 0.5rem; }
    .bg-light { background-color: #f8fafc; }
    
    .table-responsive { overflow-x: auto; }
    .table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
    .table th, .table td { padding: 1rem 1.5rem; text-align: left; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
    .table th { color: var(--text-muted); font-weight: 600; font-size: 0.75rem; text-transform: uppercase; }
    .table tbody tr:hover { background-color: #fafbfc; }
    
    .role-badge { padding: 0.25rem 0.6rem; border-radius: var(--radius-sm); font-size: 0.70rem; font-weight: 600; text-transform: uppercase; }
    .type-purchase { background: #e0e7ff; color: #4338ca; }
    .type-payment { background: #ecfdf5; color: #047857; }
    .type-fee { background: #fef2f2; color: #b91c1c; }
    .type-interest { background: #fffbeb; color: #b45309; }
    .type-other { background: #f1f5f9; color: #475569; }

    .font-weight-600 { font-weight: 600; }
    .opacity-50 { opacity: 0.5; }
    .text-sm { font-size: 0.75rem; }
    
    .empty-state {
      display: flex; flex-direction: column; align-items: center; justify-content: center;
      padding: 5rem 0; text-align: center;
    }
  `]
})
export class LedgerListComponent implements OnInit {
  private fnService = inject(FinanceService);
  private router = inject(Router);
  private customerService = inject(CustomerService);
  private instService = inject(InstallmentService);
  private notifications = inject(NotificationService);

  loadedAccountId: string | null = null;
  entries: LedgerEntry[] = [];
  enumType = LedgerEntryType;

  customers: Customer[] = [];
  selectedCustomerId: string = '';
  selectedCustomerAccounts: Account[] = [];
  selectedAccountId: string = '';

  get accountOptions() {
    return this.selectedCustomerAccounts.map(a => ({
      id: a.id,
      label: `[${a.accountType === 2 ? 'CRÉDITO' : 'DÉBITO'}] Lim: $${a.creditLimit.toFixed(2)}`
    }));
  }

  ngOnInit() {
    this.customerService.getCustomers().subscribe(data => {
      this.customers = data;
    });
  }

  onCustomerChange(custId: string) {
    this.selectedAccountId = '';
    this.selectedCustomerAccounts = [];
    if (!custId) return;

    this.customerService.getCustomer(custId).subscribe(detail => {
      if (detail && detail.accounts) {
        this.selectedCustomerAccounts = detail.accounts;
      }
    });
  }

  loadLedger(id: string) {
    if (!id) return;
    this.loadedAccountId = id;
    this.fnService.getAccountLedger(id).pipe(
      catchError(err => {
        console.error('Error loading ledger:', err?.status, err?.message);
        return of([] as LedgerEntry[]);
      })
    ).subscribe(data => {
      this.entries = data;
    });
  }

  getEntryName(type: LedgerEntryType): string {
    return LedgerEntryType[type] || 'Unknown';
  }

  getEntryClass(type: LedgerEntryType): string {
    switch (type) {
      case LedgerEntryType.Purchase: return 'type-purchase';
      case LedgerEntryType.Payment: return 'type-payment';
      case LedgerEntryType.Fee: return 'type-fee';
      case LedgerEntryType.Interest: return 'type-interest';
      default: return 'type-other';
    }
  }

  calculateBalance(): number {
    return this.entries.reduce((acc, current) => acc + current.amount, 0);
  }

  viewStatements() {
    this.router.navigate(['/app/finance/billing'], { queryParams: { account: this.loadedAccountId } });
  }

  simulateTx() {
    this.notifications.info('Usá el Simulador de Canales (ATM/POS) para inyectar transacciones reales al Switch.');
  }

  canDefer(entry: LedgerEntry): boolean {
    return entry.amount > 0 && (entry.type === LedgerEntryType.Purchase || entry.type === LedgerEntryType.Clearing);
  }

  deferEntry(entry: LedgerEntry) {
    const months = prompt('¿A cuántos meses desea diferir esta compra?', '12');
    if (months) {
      this.instService.deferPurchase({
        accountId: this.loadedAccountId!,
        ledgerEntryId: entry.id,
        installments: parseInt(months)
      }).subscribe({
        next: () => {
          this.notifications.success('Compra diferida exitosamente.');
          this.loadLedger(this.loadedAccountId!);
        },
        error: (err) => {
          console.error('Error deferring purchase:', err?.status, err?.message);
          this.notifications.error('Error al diferir la compra. Verificá que el backend esté disponible.');
        }
      });
    }
  }
}
