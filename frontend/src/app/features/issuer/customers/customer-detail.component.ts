import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { CustomerService, Customer, Account } from './customer.service';
import { CatalogService, CatalogProduct } from '../../switch/catalog.service';
import { NotificationService } from '../../../core/notification.service';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';

@Component({
  selector: 'app-customer-detail',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-container" *ngIf="customer">
      <!-- Breadcrumb & Header -->
      <div class="breadcrumb d-flex align-items-center gap-2 mb-3">
        <a href="javascript:void(0)" (click)="goBack()" class="text-muted text-decoration-none">
          <span class="material-symbols-rounded">arrow_back</span> Regresar a Lista
        </a>
      </div>

      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">account_circle</span>
            Vista 360: {{customer.fullName}}
          </h1>
          <p class="text-muted mt-1">ID Cliente: {{customer.customerNumber}} • Creado el {{customer.createdOn | date}}</p>
        </div>
        <div class="header-actions">
          <button class="btn btn-outline text-danger">
            <span class="material-symbols-rounded">block</span>
            Bloquear Cliente
          </button>
          <button class="btn btn-primary" (click)="openAccountModal()">
            <span class="material-symbols-rounded">add_card</span>
            Abrir Nueva Cuenta
          </button>
        </div>
      </div>

      <div class="d-flex gap-4">
        <!-- left col: Profiles -->
        <div class="card flex-grow-1" style="max-width: 400px; height: max-content;">
          <h3 class="border-bottom pb-3 mb-3 d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary">badge</span> Datos de Identidad
          </h3>
          <ul class="list-group">
            <li class="list-item">
              <span class="list-label text-muted">DOCUMENTO (DNI/RUC)</span>
              <strong>{{customer.documentId}}</strong>
            </li>
            <li class="list-item">
              <span class="list-label text-muted">CORREO ELECTRÓNICO</span>
              <strong>{{customer.email}}</strong>
            </li>
            <li class="list-item">
              <span class="list-label text-muted">TELÉFONO</span>
              <strong>{{customer.phone}}</strong>
            </li>
            <li class="list-item">
              <span class="list-label text-muted">ESTADO ACTUAL</span>
              <span class="badge badge-success mt-1">✓ Activo y Verificado</span>
            </li>
          </ul>
        </div>

        <!-- right col: Accounts -->
        <div class="card flex-grow-1 p-0">
          <div class="card-header border-bottom p-3 d-flex justify-content-between align-items-center">
            <h3 class="m-0 d-flex align-items-center gap-2 text-primary-dark">
              <span class="material-symbols-rounded">account_balance_wallet</span> Cuentas Asociadas
            </h3>
            <span class="badge badge-subtle">{{accounts.length}} Cuentas Activas</span>
          </div>

          <div class="table-responsive">
            <table class="table">
              <thead>
                <tr>
                  <th>PRODUCTO</th>
                  <th>TIPO</th>
                  <th class="text-right">LIMITE (LINEA CRED.)</th>
                  <th class="text-right">DISPONIBLE</th>
                  <th class="text-center">ESTADO</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let acc of accounts">
                  <td>
                    <div class="d-flex flex-column">
                      <strong class="text-main">{{acc.productCode}}</strong>
                      <span class="text-muted text-sm">ID: {{acc.id | slice:0:8}}...</span>
                    </div>
                  </td>
                  <td>
                    <span class="role-badge" [ngClass]="acc.accountType === 2 ? 'role-issuer' : 'role-admin'">
                      {{acc.accountType === 2 ? 'Crédito' : 'Débito / Ahorro'}}
                    </span>
                  </td>
                  <td class="text-right font-weight-500">
                    {{acc.accountType === 2 ? (acc.creditLimit | currency) : 'N/A' }}
                  </td>
                  <td class="text-right text-success font-weight-500">
                    {{acc.availableLimit | currency}}
                  </td>
                  <td class="text-center">
                    <span class="status-badge status-active">Normal</span>
                  </td>
                </tr>
                <tr *ngIf="accounts.length === 0">
                  <td colspan="5" class="text-center py-5 text-muted">
                    <span class="material-symbols-rounded" style="font-size: 32px; opacity: 0.5;">money_off</span>
                    <p class="mt-2">El cliente no tiene cuentas aperturadas</p>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <!-- Create Account Modal -->
      <div class="modal-overlay" [class.show]="isModalOpen">
        <div class="modal-card card">
          <div class="modal-header">
            <h3>Apertura de Cuenta</h3>
            <button class="icon-btn" (click)="closeAccountModal()">
              <span class="material-symbols-rounded">close</span>
            </button>
          </div>
          <div class="modal-body">
            <div class="alert alert-info d-flex gap-2 align-items-center mb-3">
              <span class="material-symbols-rounded">info</span>
              <span>Esta acción creará un Ledger (Libro contable) listo para procesar transacciones transaccionales en tiempo real.</span>
            </div>
            
            <div class="input-group">
              <label>Tipo de Cuenta</label>
              <select class="form-control" #accType (change)="isCredit = accType.value === '2'">
                <option value="1">Cuenta de Débito (Fondos Prepagados)</option>
                <option value="2">Cuenta de Crédito (Línea Revolvente)</option>
              </select>
            </div>
            <div class="input-group">
              <label>Producto Bancario (CardVault Catalog)</label>
              <select class="form-control" #prodCode>
                <option *ngFor="let p of availableProducts" [value]="p.code">
                  {{p.brand}} {{p.name}} ({{p.code}})
                </option>
                <option *ngIf="availableProducts.length === 0" value="VISA_CLASSIC">Cargando productos...</option>
              </select>
            </div>
            
            <div class="input-group" *ngIf="isCredit">
              <label>Límite de Crédito Aprobado ($ USD)</label>
              <input type="number" class="form-control input-xl" placeholder="5000" id="limitInput" [(ngModel)]="creditLimitInp">
              <small class="text-muted mt-1">Este será el límite máximo de sobregiro transaccional.</small>
            </div>
          </div>
          <div class="modal-footer d-flex justify-content-between">
            <button class="btn btn-outline" (click)="closeAccountModal()">Cancelar</button>
            <button class="btn btn-primary" (click)="saveAccount(accType.value, prodCode.value, creditLimitInp.toString())">Crear Cuenta y Ledger</button>
          </div>
        </div>
      </div>

    </div>
    
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .breadcrumb a { font-size: 0.875rem; transition: color 0.2s; display: flex; align-items: center; gap: 0.25rem; font-weight: 500; }
    .breadcrumb a:hover { color: var(--primary); text-decoration: none; }
    
    .list-group { list-style: none; padding: 0; margin: 0; }
    .list-item { display: flex; flex-direction: column; padding: 0.75rem 0; border-bottom: 1px dashed var(--border-color); }
    .list-item:last-child { border-bottom: none; }
    .list-label { font-size: 0.75rem; letter-spacing: 0.5px; font-weight: 600; margin-bottom: 0.25rem; }
    .list-item strong { color: var(--text-main); font-size: 0.875rem; }

    .table-responsive { overflow-x: auto; }
    .table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
    .table th, .table td { padding: 1rem 1.25rem; text-align: left; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
    .table th { color: var(--text-muted); font-weight: 600; font-size: 0.75rem; text-transform: uppercase; }
    
    .badge-success { background-color: #ecfdf5; color: #047857; padding: 0.25rem 0.5rem; border-radius: var(--radius-sm); font-size: 0.75rem; font-weight: 600; align-self: flex-start;}
    .badge-subtle { background-color: #f1f5f9; color: #64748b; padding: 0.2rem 0.6rem; border-radius: 12px; font-size: 0.75rem; font-weight: 600; }
    
    .role-badge { padding: 0.25rem 0.6rem; border-radius: var(--radius-sm); font-size: 0.75rem; font-weight: 600; }
    .role-admin { background: #e0e7ff; color: #4338ca; } /* blue for debit */
    .role-issuer { background: #fef3c7; color: #b45309; } /* gold for credit */

    .status-badge { padding: 0.25rem 0.6rem; border-radius: 1rem; font-size: 0.75rem; font-weight: 600; display: inline-flex; align-items: center; gap: 0.25rem; }
    .status-active { background: #ecfdf5; color: #047857; }
    
    .alert { padding: 1rem 1.25rem; border-radius: var(--radius-md); font-size: 0.875rem;}
    .alert-info { background-color: #eff6ff; color: #1e3a8a; border: 1px solid #bfdbfe; }
    
    .input-xl { font-size: 1.25rem; font-weight: 600; padding: 1rem; }

    /* Modal */
    .modal-overlay { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.5); display: none; align-items: center; justify-content: center; z-index: 1000; backdrop-filter: blur(2px);}
    .modal-overlay.show { display: flex; }
    .modal-card { width: 100%; max-width: 500px; padding: 0; animation: slideDown 0.3s ease; }
    .modal-header { display: flex; justify-content: space-between; align-items: center; padding: 1.5rem; border-bottom: 1px solid var(--border-color); }
    .modal-header h3 { margin: 0; font-size: 1.25rem; color: var(--text-main); }
    .modal-body { padding: 1.5rem; }
    .modal-footer { padding: 1rem 1.5rem; border-top: 1px solid var(--border-color); background: var(--bg-main); border-radius: 0 0 var(--radius-lg) var(--radius-lg); }
    .icon-btn { background: none; border: none; cursor: pointer; color: var(--text-muted); padding: 0.25rem; }
    .icon-btn:hover { color: var(--danger); }
    
    @keyframes slideDown { from { transform: translateY(-20px); opacity: 0; } to { transform: translateY(0); opacity: 1; } }
  `]
})
export class CustomerDetailComponent implements OnInit {
  private customerService = inject(CustomerService);
  private catalogService = inject(CatalogService);
  private notifications = inject(NotificationService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  customer: Customer | null = null;
  accounts: Account[] = [];
  availableProducts: CatalogProduct[] = [];

  isModalOpen = false;
  isCredit = false;
  creditLimitInp: number = 2500;

  ngOnInit() {
    const custId = this.route.snapshot.paramMap.get('id');
    if (custId) {
      this.customerService.getCustomer(custId).pipe(
        catchError(err => {
          console.error('Error loading customer:', err?.status, err?.message);
          this.notifications.error('No se pudo cargar el cliente. Verificá que el backend esté disponible.');
          this.goBack();
          return of(null);
        })
      ).subscribe(res => {
        if (res) {
          this.customer = res;
          this.accounts = res.accounts || [];
        }
      });
    }

    // Pre-load products for the account opening modal
    this.catalogService.getProducts().pipe(catchError(() => {
      this.notifications.error('No se pudieron cargar los productos del catálogo.');
      return of([]);
    })).subscribe(data => {
      this.availableProducts = data;
    });
  }

  goBack() {
    this.router.navigate(['/app/issuer/customers']);
  }

  openAccountModal() {
    this.isModalOpen = true;
    this.isCredit = false; // default
  }

  closeAccountModal() {
    this.isModalOpen = false;
  }

  saveAccount(type: string, code: string, limit: string) {
    const isCr = type === '2';
    const numLimit = isCr ? parseFloat(limit) : 0;
    const payload = {
      customerId: this.customer?.id,
      accountType: parseInt(type, 10),
      productCode: code,
      creditLimit: numLimit
    };

    this.customerService.createAccount(payload).pipe(
      catchError(err => {
        console.error('Error creating account:', err?.status, err?.message);
        this.notifications.error('No se pudo crear la cuenta. Verificá que el backend esté disponible.');
        return of(null);
      })
    ).subscribe(res => {
      if (res) {
        this.accounts.push(res);
        this.closeAccountModal();
        this.accounts = [...this.accounts];
      }
    });
  }
}
