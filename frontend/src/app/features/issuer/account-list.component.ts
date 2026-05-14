import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CustomerService, Account, AccountType } from '../issuer/customers/customer.service';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';
import { CatalogService, CatalogProduct } from '../switch/catalog.service';
import { AccountLimitsModalComponent } from './account-limits-modal.component';
import { NotificationService } from '../../core/notification.service';
import { SearchSelectComponent } from '../../shared/components/search-select.component';

@Component({
  selector: 'app-account-list',
  standalone: true,
  imports: [CommonModule, FormsModule, AccountLimitsModalComponent, SearchSelectComponent],
  template: `
    <div class="page-container">
      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">credit_card</span>
            Listado Maestro de Cuentas
          </h1>
          <p class="text-muted mt-1">Gestión administrativa global de todas las cuentas (Crédito/Débito) emitidas.</p>
        </div>
        <div class="header-actions">
           <button class="btn btn-primary d-flex align-items-center gap-2" (click)="openCreateModal()">
              <span class="material-symbols-rounded">add_card</span>
              Abrir Cuenta
           </button>
        </div>
      </div>

      <div class="card p-0">
        <div class="card-header border-bottom p-3 bg-light d-flex gap-3">
           <input type="text" class="form-control" style="width: 300px;" placeholder="Buscar por Número de Cuenta..." [(ngModel)]="searchAccount">
           <select class="form-control" style="width: 200px;" [(ngModel)]="filterType">
              <option value="">Todos los Tipos</option>
              <option [value]="1">Débito (Ahorros)</option>
              <option [value]="2">Crédito</option>
           </select>
        </div>

        <div class="table-responsive">
          <table class="table">
            <thead>
              <tr>
                <th>NÚMERO DE CUENTA</th>
                <th>CLIENTE</th>
                <th>TIPO</th>
                <th class="text-right">SALDO DISPONIBLE</th>
                <th>ESTADO</th>
                <th class="text-center">ACCIONES</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let acc of filteredAccounts()">
                <td class="font-weight-600 font-mono">{{ acc.accountNumber || acc.id }}</td>
                <td>{{ acc.customerName || 'N/A' }}</td>
                <td>
                   <span class="badge" [ngClass]="acc.accountType === 2 ? 'badge-credit' : 'badge-debit'">
                      {{ acc.accountType === 2 ? 'CRÉDITO' : 'DÉBITO' }}
                   </span>
                </td>
                <td class="text-right font-weight-600">
                   <small class="text-muted mr-1">{{ acc.currencyCode || 'USD' }}</small> {{ acc.availableLimit | currency }}
                </td>
                <td>
                   <span class="status-indicator" [ngClass]="getStatusClass(acc.status)"></span>
                   {{ getStatusName(acc.status) }}
                </td>
                <td class="text-center">
                   <button class="btn btn-icon" title="Ver Detalles">
                     <span class="material-symbols-rounded">visibility</span>
                   </button>
                   <button class="btn btn-icon text-info" title="Gestionar Límites" (click)="openLimits(acc)">
                     <span class="material-symbols-rounded">speed</span>
                   </button>
                   <button class="btn btn-icon text-danger" title="Bloquear Cuenta" (click)="toggleLock(acc)">
                     <span class="material-symbols-rounded">{{ acc.status === 2 ? 'lock_open' : 'lock' }}</span>
                   </button>
                </td>
              </tr>
              <tr *ngIf="filteredAccounts().length === 0">
                <td colspan="6" class="text-center py-5 text-muted">No se encontraron cuentas con los filtros aplicados.</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <app-account-limits-modal 
        *ngIf="selectedAccount" 
        [account]="selectedAccount" 
        (close)="selectedAccount = null"
        (saved)="loadAccounts()">
      </app-account-limits-modal>

      <!-- Confirm Lock/Unlock Modal -->
      <div class="modal-overlay" [class.show]="confirmModal.open" (click)="confirmModal.open = false">
        <div class="modal-card confirm-modal" (click)="$event.stopPropagation()">
          <div class="confirm-icon" [ngClass]="confirmModal.type">
            <span class="material-symbols-rounded">{{ confirmModal.type === 'danger' ? 'lock' : 'lock_open' }}</span>
          </div>
          <h4 class="confirm-title">{{ confirmModal.title }}</h4>
          <p class="confirm-message">{{ confirmModal.message }}</p>
          <div class="confirm-actions">
            <button class="btn btn-outline" (click)="confirmModal.open = false">Cancelar</button>
            <button class="btn" [ngClass]="confirmModal.type === 'danger' ? 'btn-danger' : 'btn-success'" (click)="confirmModal.onConfirm(); confirmModal.open = false">
              {{ confirmModal.confirmLabel }}
            </button>
          </div>
        </div>
      </div>

      <!-- Create Account Modal -->
      <div class="modal-overlay" [class.show]="isCreateModalOpen" (click)="isCreateModalOpen = false">
        <div class="modal-card" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <div class="modal-title-group">
              <span class="material-symbols-rounded modal-title-icon">add_card</span>
              <h3>Apertura de Cuenta Global</h3>
            </div>
            <button class="icon-btn" (click)="isCreateModalOpen = false">
              <span class="material-symbols-rounded">close</span>
            </button>
          </div>
          <div class="modal-body">
            <div class="field-group">
              <label class="field-label">Seleccionar Cliente</label>

              <app-search-select 
                [options]="customers"
                [(value)]="createPayload.customerId"
                (valueChange)="selectCustomerById($event)"
                labelKey="fullName"
                subLabelKey="documentId"
                placeholder="Buscar por nombre o documento..."
              ></app-search-select>
            </div>

            <div class="two-col">
               <div class="field-group">
                  <label class="field-label">Tipo de Cuenta</label>
                  <app-search-select 
                    [options]="accountTypeOptions"
                    [(value)]="createPayload.accountType"
                    placeholder="Seleccionar..."
                  ></app-search-select>
               </div>
               <div class="field-group">
                  <label class="field-label">Producto</label>
                  <app-search-select 
                    [options]="availableProducts"
                    [(value)]="createPayload.productCode"
                    valueKey="code"
                    labelKey="name"
                    placeholder="Buscar producto..."
                  ></app-search-select>
               </div>
            </div>

            <div class="field-group" *ngIf="createPayload.accountType == 2">
              <label class="field-label">Límite de Crédito Aprobado (USD)</label>
              <input type="number" class="form-control" [(ngModel)]="createPayload.creditLimit" placeholder="0.00">
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-outline" (click)="isCreateModalOpen = false">Cancelar</button>
            <button class="btn btn-primary" [disabled]="!createPayload.customerId || saving" (click)="saveAccount()">
               <span *ngIf="saving" class="spinner-border spinner-border-sm"></span>
               <span class="material-symbols-rounded" *ngIf="!saving">check_circle</span>
               {{ saving ? 'Procesando...' : 'Confirmar Apertura' }}
            </button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); }
    .card { background: white; border: 1px solid var(--border-color); border-radius: 8px; }
    .font-mono { font-family: 'Roboto Mono', monospace; font-size: 0.8rem; }
    .badge { padding: 4px 8px; border-radius: 4px; font-size: 0.65rem; font-weight: bold; }
    .badge-credit { background: #fee2e2; color: #991b1b; }
    .badge-debit { background: #dcfce7; color: #166534; }

    .status-indicator { display: inline-block; width: 10px; height: 10px; border-radius: 50%; margin-right: 5px; }
    .status-active { background: #22c55e; }
    .status-blocked { background: #ef4444; }

    .table th { font-size: 0.75rem; text-transform: uppercase; color: var(--text-muted); padding: 1rem; }
    .table td { padding: 1rem; border-bottom: 1px solid var(--border-color); }
    .font-weight-600 { font-weight: 600; }

    /* ── Modal overlay ── */
    .modal-overlay {
      position: fixed; inset: 0;
      background: rgba(15, 23, 42, 0.55);
      backdrop-filter: blur(6px);
      display: none; align-items: center; justify-content: center;
      z-index: 1000;
    }
    .modal-overlay.show { display: flex; animation: fadeIn 0.2s ease; }

    @keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }
    @keyframes slideUp {
      from { opacity: 0; transform: translateY(20px) scale(0.97); }
      to   { opacity: 1; transform: translateY(0)   scale(1); }
    }

    /* ── Modal card ── */
    .modal-card {
      width: 100%; max-width: 480px;
      background: #fff;
      border-radius: 16px;
      box-shadow: 0 24px 48px -8px rgba(0,0,0,0.28), 0 8px 16px -4px rgba(0,0,0,0.12);
      overflow: hidden;
      animation: slideUp 0.25s cubic-bezier(0.34, 1.56, 0.64, 1);
    }

    /* ── Modal header ── */
    .modal-header {
      display: flex; justify-content: space-between; align-items: center;
      padding: 1.25rem 1.5rem;
      background: linear-gradient(135deg, var(--primary-dark) 0%, var(--primary) 100%);
    }
    .modal-title-group {
      display: flex; align-items: center; gap: 0.6rem;
    }
    .modal-title-icon {
      font-size: 22px;
      color: rgba(255,255,255,0.85);
    }
    .modal-header h3 {
      margin: 0; font-size: 1rem; font-weight: 600;
      color: #fff; letter-spacing: 0.01em;
    }
    .icon-btn {
      display: inline-flex; align-items: center; justify-content: center;
      width: 32px; height: 32px;
      background: rgba(255,255,255,0.15);
      border: none; border-radius: 50%;
      color: #fff; cursor: pointer;
      transition: background 0.2s;
    }
    .icon-btn:hover { background: rgba(255,255,255,0.28); }
    .icon-btn .material-symbols-rounded { font-size: 18px; }

    /* ── Modal body ── */
    .modal-body { padding: 1.5rem; display: flex; flex-direction: column; gap: 1.1rem; }

    .field-group { display: flex; flex-direction: column; gap: 0.4rem; }
    .field-label {
      font-size: 0.7rem; font-weight: 700;
      text-transform: uppercase; letter-spacing: 0.08em;
      color: var(--text-muted);
    }

    .two-col { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; }

    /* ── Modal footer ── */
    .modal-footer {
      display: flex; justify-content: space-between; align-items: center;
      padding: 1rem 1.5rem;
      border-top: 1px solid var(--border-color);
      background: #fafafa;
    }

    .btn-primary:disabled { opacity: 0.55; cursor: not-allowed; }

    /* ── Confirm modal ── */
    .confirm-modal {
      background: var(--bg-paper);
      max-width: 380px; padding: 2rem 1.5rem;
      text-align: center; border-radius: 16px;
      box-shadow: 0 10px 30px rgba(0,0,0,0.15);
      animation: slideUp 0.25s cubic-bezier(0.34, 1.56, 0.64, 1);
    }
    .confirm-icon {
      width: 56px; height: 56px; border-radius: 50%;
      display: inline-flex; align-items: center; justify-content: center;
      margin-bottom: 1rem;
    }
    .confirm-icon.danger { background: #fee2e2; }
    .confirm-icon.danger .material-symbols-rounded { color: #dc2626; font-size: 28px; }
    .confirm-icon.success { background: #dcfce7; }
    .confirm-icon.success .material-symbols-rounded { color: #16a34a; font-size: 28px; }
    .confirm-title { font-size: 1.05rem; font-weight: 700; margin-bottom: 0.5rem; }
    .confirm-message { font-size: 0.85rem; color: var(--text-muted); margin-bottom: 1.5rem; }
    .confirm-actions { display: flex; gap: 0.75rem; justify-content: center; }
    .btn-danger { background: #dc2626; color: #fff; }
    .btn-danger:hover { background: #b91c1c; }
    .btn-success { background: var(--success); color: #fff; }
    .btn-success:hover { background: #059669; }
  `]
})
export class AccountListComponent implements OnInit {
  private notifications = inject(NotificationService);

  accounts: any[] = [];
  customers: any[] = [];
  availableProducts: CatalogProduct[] = [];

  searchAccount = '';
  filterType = '';
  selectedAccount: any = null;

  // Customer combobox state
  customerSearch = '';
  customerDropdownOpen = false;
  selectedCustomerLabel = '';

  accountTypeOptions = [
    { id: 1, name: 'Débito' },
    { id: 2, name: 'Crédito' }
  ];

  // Confirm modal state
  confirmModal = {
    open: false,
    type: 'danger' as 'danger' | 'success',
    title: '',
    message: '',
    confirmLabel: 'Confirmar',
    onConfirm: () => {}
  };

  isCreateModalOpen = false;
  saving = false;
  createPayload = {
    customerId: '',
    accountType: 1,
    productCode: 'VISA_CLAS',
    creditLimit: 2500
  };

  constructor(
    private customerService: CustomerService,
    private catalogService: CatalogService
  ) {}

  ngOnInit() {
    this.loadAccounts();
    this.customerService.getCustomers().subscribe((data: any[]) => this.customers = data);
    this.catalogService.getProducts().pipe(catchError(() => {
      this.notifications.error('No se pudieron cargar los productos del catálogo.');
      return of([] as CatalogProduct[]);
    })).subscribe((data: CatalogProduct[]) => {
      this.availableProducts = data;
      if (data.length > 0) this.createPayload.productCode = data[0].code;
    });
  }

  loadAccounts() {
    this.customerService.getAccounts(this.searchAccount).subscribe((accounts: any[]) => {
      this.accounts = accounts;
    });
  }

  filteredAccounts() {
    return this.accounts.filter(a => {
      const search = this.searchAccount.toLowerCase();
      const matchesSearch = !search || 
                           (a.accountNumber?.toLowerCase().includes(search)) || 
                           (a.customerName?.toLowerCase().includes(search));
      const matchesType = this.filterType === '' || a.accountType === Number(this.filterType);
      return matchesSearch && matchesType;
    });
  }

  getStatusName(status: number): string {
    return status === 1 ? 'Activa' : (status === 2 ? 'Boqueada' : 'Cerrada');
  }

  getStatusClass(status: number): string {
    return status === 1 ? 'status-active' : 'status-blocked';
  }

  toggleLock(acc: any) {
    const isBlocking = acc.status === 1;
    this.confirmModal = {
      open: true,
      type: isBlocking ? 'danger' : 'success',
      title: isBlocking ? 'Bloquear Cuenta' : 'Desbloquear Cuenta',
      message: `¿Confirma que desea ${isBlocking ? 'BLOQUEAR' : 'DESBLOQUEAR'} la cuenta ${acc.accountNumber || acc.id}?`,
      confirmLabel: isBlocking ? 'Sí, Bloquear' : 'Sí, Desbloquear',
      onConfirm: () => { acc.status = isBlocking ? 2 : 1; }
    };
  }

  openLimits(acc: any) {
    this.selectedAccount = acc;
  }

  openCreateModal() {
    // Reset para evitar estado sucio de aperturas previas
    this.createPayload = {
      customerId: '',
      accountType: 1,
      productCode: this.availableProducts.length > 0 ? this.availableProducts[0].code : '',
      creditLimit: 2500
    };
    this.customerSearch = '';
    this.selectedCustomerLabel = '';
    this.customerDropdownOpen = false;
    this.isCreateModalOpen = true;
  }

  filteredCustomers(): any[] {
    const q = this.customerSearch.toLowerCase().trim();
    if (!q) return this.customers.slice(0, 20); // primeros 20 si no hay búsqueda
    return this.customers.filter(c =>
      c.fullName?.toLowerCase().includes(q) ||
      c.documentId?.toLowerCase().includes(q)
    ).slice(0, 15);
  }

  selectCustomerById(id: string) {
    const c = this.customers.find(x => x.id === id);
    if (c) {
      this.selectedCustomerLabel = `${c.fullName} • ${c.documentId}`;
    } else {
      this.selectedCustomerLabel = '';
    }
  }

  clearCustomer() {
    this.createPayload.customerId = '';
    this.selectedCustomerLabel = '';
    this.customerSearch = '';
    this.customerDropdownOpen = false;
  }

  closeDropdownDelayed() {
    // Delay para que mousedown en una opción se procese antes del blur
    setTimeout(() => { this.customerDropdownOpen = false; }, 150);
  }

  highlight(text: string): string {
    const q = this.customerSearch.trim();
    if (!q) return text;
    const re = new RegExp(`(${q.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')})`, 'gi');
    return text.replace(re, '<mark>$1</mark>');
  }

  saveAccount() {
    this.saving = true;
    // Asegurar tipos correctos antes de enviar
    const payload = {
      ...this.createPayload,
      accountType: Number(this.createPayload.accountType),
      creditLimit: Number(this.createPayload.creditLimit)
    };
    this.customerService.createAccount(payload).subscribe({
      next: () => {
        this.saving = false;
        this.isCreateModalOpen = false;
        this.notifications.success('Cuenta aperturada exitosamente.');
        this.loadAccounts();
      },
      error: (err: any) => {
        this.saving = false;
        const detail = err?.error?.detail ?? err?.error?.title ?? err?.error ?? err?.message ?? 'Error desconocido';
        this.notifications.error(`Error al aperturar cuenta: ${detail}`);
        console.error('[CreateAccount] error:', err);
      }
    });
  }
}
