import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { InstallmentService, InstallmentPlan, InstallmentPlanStatus, InstallmentStatus } from './installment.service';
import { CustomerService, Customer, Account } from '../issuer/customers/customer.service';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';

import { SearchSelectComponent } from '../../shared/components/search-select.component';

@Component({
  selector: 'app-installment-list',
  standalone: true,
  imports: [CommonModule, FormsModule, SearchSelectComponent],
  template: `
    <div class="page-container">
      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">account_tree</span>
            Planes de Diferidos y Cuotas (v66)
          </h1>
          <p class="text-muted mt-1">Gestione los consumos financiados y monitoree sus tablas de amortización.</p>
        </div>
        
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
           <button class="btn btn-primary btn-sm ms-2" [disabled]="!selectedAccountId" (click)="loadPlans(selectedAccountId)">Listar Planes</button>
        </div>
      </div>

      <div *ngIf="loadedAccountId">
        <div class="plans-grid">
           <div class="card p-0 mb-4" *ngFor="let plan of plans">
              <div class="card-header border-bottom p-3 d-flex justify-content-between align-items-center bg-light">
                 <div>
                   <h3 class="m-0 text-main">{{plan.description}}</h3>
                   <small class="text-muted">Iniciado el {{plan.createdOn | date}} • ID: {{plan.id.substring(0,8)}}</small>
                 </div>
                 <span class="status-badge" [ngClass]="getPlanStatusClass(plan.status)">{{getPlanStatusName(plan.status)}}</span>
              </div>
              <div class="p-3 d-flex gap-4">
                 <div class="plan-stats flex-shrink-0" style="width: 280px; border-right: 1px solid #f1f5f9;">
                    <div class="stat-item mb-3">
                       <label>Monto Original</label>
                       <div class="h4 m-0">{{plan.totalAmount | currency}}</div>
                    </div>
                    <div class="stat-item mb-3">
                       <label>Tasa Aplicada (APR)</label>
                       <div class="h4 m-0 text-primary">{{plan.interestApr | percent}}</div>
                    </div>
                    <div class="stat-item mb-3">
                       <label>Cuotas</label>
                       <div class="h4 m-0">{{plan.totalInstallments - plan.remainingInstallments}} / {{plan.totalInstallments}} Pagadas</div>
                    </div>
                    <div class="progress-container mt-3">
                       <div class="progress-label d-flex justify-content-between mb-1">
                          <span>Progreso de Pago</span>
                          <span>{{ (1 - (plan.remainingInstallments / plan.totalInstallments)) | percent }}</span>
                       </div>
                       <div class="progress">
                          <div class="progress-bar" [style.width]="(1 - (plan.remainingInstallments / plan.totalInstallments)) * 100 + '%'"></div>
                       </div>
                    </div>
                 </div>

                 <div class="plan-schedule flex-grow-1">
                    <label class="mb-3 d-block font-weight-600">Tabla de Amortización</label>
                    <div class="table-responsive" style="max-height: 250px;">
                       <table class="table table-sm">
                          <thead>
                             <tr>
                                <th>#</th>
                                <th>VENCIMIENTO</th>
                                <th class="text-right">CAPITAL</th>
                                <th class="text-right">INTERÉS</th>
                                <th class="text-right">TOTAL</th>
                                <th>ESTADO</th>
                             </tr>
                          </thead>
                          <tbody>
                             <tr *ngFor="let inst of plan.amortizationSchedule">
                                <td>{{inst.installmentNumber}}</td>
                                <td class="text-muted">{{inst.dueDate | date:'mediumDate'}}</td>
                                <td class="text-right">{{inst.principalAmount | currency}}</td>
                                <td class="text-right">{{inst.interestAmount | currency}}</td>
                                <td class="text-right font-weight-600 text-main">{{inst.totalInstallmentAmount | currency}}</td>
                                <td>
                                   <span class="dot" [ngClass]="getInstStatusClass(inst.status)"></span>
                                   {{getInstStatusName(inst.status)}}
                                </td>
                             </tr>
                          </tbody>
                       </table>
                    </div>
                 </div>
              </div>
           </div>

           <div class="empty-state py-5" *ngIf="plans.length === 0">
              <span class="material-symbols-rounded text-muted" style="font-size: 64px; opacity: 0.3;">account_tree</span>
              <p class="mt-2 text-muted">No existen planes de diferidos activos para esta cuenta.</p>
           </div>
        </div>
      </div>

      <div class="empty-state" *ngIf="!loadedAccountId">
         <span class="material-symbols-rounded text-muted" style="font-size: 64px; opacity: 0.3">account_tree</span>
         <h3 class="text-muted mt-3">Seleccione una cuenta para gestionar sus diferidos</h3>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .bg-light { background-color: #f8fafc; }
    .h4 { font-size: 1.1rem; font-weight: 700; color: #1e293b; }
    label { font-size: 0.75rem; text-transform: uppercase; color: var(--text-muted); font-weight: 600; }
    
    .status-badge { padding: 0.2rem 0.6rem; border-radius: 1rem; font-size: 0.7rem; font-weight: 600; }
    .status-active { background: #ecfdf5; color: #047857; }
    .status-completed { background: #eff6ff; color: #1e40af; }
    
    .progress { height: 8px; background-color: #f1f5f9; border-radius: 4px; overflow: hidden; }
    .progress-bar { height: 100%; background-color: var(--primary); transition: width 0.3s ease; }
    
    .table-sm th, .table-sm td { padding: 0.5rem; font-size: 0.8rem; }
    .dot { width: 8px; height: 8px; border-radius: 50%; display: inline-block; margin-right: 4px; }
    .dot-pending { background-color: #94a3b8; }
    .dot-invoiced { background-color: #3b82f6; }
    .dot-paid { background-color: #10b981; }
    
    .empty-state { display: flex; flex-direction: column; align-items: center; justify-content: center; padding: 5rem 0; text-align: center; }
  `]
})
export class InstallmentListComponent implements OnInit {
  private instService = inject(InstallmentService);
  private customerService = inject(CustomerService);

  customers: Customer[] = [];
  selectedCustomerId: string = '';
  selectedCustomerAccounts: Account[] = [];
  selectedAccountId: string = '';
  loadedAccountId: string | null = null;
  plans: InstallmentPlan[] = [];

  get accountOptions() {
    return this.selectedCustomerAccounts.map(a => ({
      id: a.id,
      label: `[${a.accountType === 2 ? 'CRÉDITO' : 'DÉBITO'}] Lim: $${a.creditLimit.toFixed(2)}`
    }));
  }

  ngOnInit() {
    this.customerService.getCustomers().subscribe(data => this.customers = data);
  }

  onCustomerChange(custId: string) {
    this.selectedAccountId = '';
    this.selectedCustomerAccounts = [];
    if (!custId) return;
    this.customerService.getCustomer(custId).subscribe(c => {
      if (c && c.accounts) this.selectedCustomerAccounts = c.accounts;
    });
  }

  loadPlans(id: string) {
    this.loadedAccountId = id;
    this.instService.getPlans(id).pipe(
      catchError(err => {
        console.error('Error loading installment plans:', err?.status, err?.message);
        return of([] as InstallmentPlan[]);
      })
    ).subscribe(data => this.plans = data);
  }

  getPlanStatusName(s: InstallmentPlanStatus) { return InstallmentPlanStatus[s]; }
  getPlanStatusClass(s: InstallmentPlanStatus) { return s === 1 ? 'status-active' : 'status-completed'; }

  getInstStatusName(s: InstallmentStatus) { return InstallmentStatus[s]; }
  getInstStatusClass(s: InstallmentStatus) {
    if (s === 1) return 'dot-pending';
    if (s === 2) return 'dot-invoiced';
    return 'dot-paid';
  }
}
