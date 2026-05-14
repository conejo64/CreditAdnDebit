import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DisputeService, DisputeCase, DisputeEvent } from './dispute.service';
import { CustomerService, Customer, Account } from '../issuer/customers/customer.service';
import { NotificationService } from '../../core/notification.service';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';

import { SearchSelectComponent } from '../../shared/components/search-select.component';

@Component({
  selector: 'app-dispute-list',
  standalone: true,
  imports: [CommonModule, FormsModule, SearchSelectComponent],
  template: `
    <div class="page-container">
      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-danger" style="font-size: 32px">gavel</span>
            Gestión de Disputas y Reclamos
          </h1>
          <p class="text-muted mt-1">Administre las quejas de los clientes y los procesos de contracargo.</p>
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
          <button class="btn btn-primary btn-sm" [disabled]="!selectedAccountId" (click)="loadCases(selectedAccountId)">
            Cargar Casos
          </button>
        </div>
      </div>

      <div class="card p-0" *ngIf="cases.length > 0; else noCases">
        <div class="table-responsive">
          <table class="table">
            <thead>
              <tr>
                <th>RRN/REFERENCIA</th>
                <th>MONTO</th>
                <th>CÓDIGO MOTIVO</th>
                <th>ESTADO</th>
                <th>ABIERTO EL</th>
                <th class="text-center">GESTIÓN</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let c of cases">
                <td class="font-weight-600">{{ c.rrn }}</td>
                <td class="text-danger font-weight-600">{{ c.amount | currency }}</td>
                <td>{{ c.reasonCode }}</td>
                <td>
                  <span class="role-badge" [ngClass]="getStatusClass(c.status)">
                    {{ c.status === 'IN_PROGRESS' ? 'EN PROCESO' : c.status }}
                  </span>
                </td>
                <td class="text-muted">{{ c.openedAt | date:'short' }}</td>
                <td class="text-center">
                   <button class="btn btn-icon" (click)="viewDetails(c)">
                     <span class="material-symbols-rounded">manage_search</span>
                   </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <ng-template #noCases>
        <div class="empty-state">
           <span class="material-symbols-rounded text-muted" style="font-size: 64px; opacity: 0.2">policy</span>
           <h3 class="text-muted mt-3">No hay disputas registradas para esta cuenta</h3>
           <p class="text-muted" *ngIf="selectedAccountId">Todas las transacciones se encuentran en estado de conformidad.</p>
           <p class="text-muted" *ngIf="!selectedAccountId">Seleccione una cuenta para filtrar los reclamos activos.</p>
        </div>
      </ng-template>
    </div>

    <!-- Details/Action Modal -->
    <div class="modal-backdrop" *ngIf="selectedCase">
       <div class="modal-card" style="max-width: 600px; width: 90%;">
         <div class="modal-header d-flex justify-content-between">
           <h3>Gestión de Caso #{{ selectedCase.id.substring(0, 8) }}</h3>
           <button class="btn-close" (click)="selectedCase = null">×</button>
         </div>
         <div class="modal-body">
            <div class="case-meta mb-4">
               <div class="row">
                 <div class="col"><small class="d-block text-muted">Monto Disputado</small><strong>{{ selectedCase.amount | currency }}</strong></div>
                 <div class="col"><small class="d-block text-muted">Red</small><strong>{{ selectedCase.network }}</strong></div>
                 <div class="col"><small class="d-block text-muted">Estado Actual</small><span [ngClass]="getStatusClass(selectedCase.status)">{{ selectedCase.status }}</span></div>
               </div>
            </div>

            <div class="timeline mt-3 mb-4">
              <div class="timeline-item" *ngFor="let ev of caseEvents">
                <div class="timeline-date">{{ ev.createdOn | date:'shortTime' }}</div>
                <div class="timeline-content">
                   <strong>{{ ev.action }}</strong>
                   <p class="m-0 text-sm text-muted">{{ ev.notes || 'Sin observaciones.' }}</p>
                </div>
              </div>
            </div>

            <div class="form-group mb-3">
               <label class="text-sm font-weight-600 mb-1">Nueva Acción / Decisión</label>
               <app-search-select 
                 [options]="actionOptions"
                 [(value)]="nextAction"
                 valueKey="id"
                 labelKey="name"
                 placeholder="Seleccione una acción..."
               ></app-search-select>
            </div>
            <div class="form-group mb-3">
               <textarea class="form-control" placeholder="Escriba notas de seguimiento..." rows="3" [(ngModel)]="actionNotes"></textarea>
            </div>
         </div>
         <div class="modal-footer d-flex justify-content-end p-3 gap-2">
            <button class="btn btn-outline" (click)="selectedCase = null">Cerrar</button>
            <button class="btn btn-primary" [disabled]="!nextAction" (click)="applyAction()">Aplicar</button>
         </div>
       </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); }
    .card { background: white; border: 1px solid var(--border-color); border-radius: 8px; }
    .table th { font-size: 0.75rem; text-transform: uppercase; color: var(--text-muted); padding: 1rem 1.5rem; }
    .table td { padding: 1rem 1.5rem; border-bottom: 1px solid var(--border-color); }
    .font-weight-600 { font-weight: 600; }
    
    .role-badge { padding: 0.25rem 0.5rem; border-radius: 4px; font-size: 0.65rem; font-weight: bold; }
    .status-open { background: #e0f2fe; color: #0369a1; }
    .status-in_progress { background: #fef3c7; color: #92400e; }
    .status-won { background: #d1fae5; color: #065f46; }
    .status-lost { background: #fee2e2; color: #991b1b; }
    
    .empty-state { display: flex; flex-direction: column; align-items: center; justify-content: center; padding: 5rem 0; text-align: center; }
    
    .modal-backdrop { position: fixed; top:0; left:0; width:100%; height:100%; background: rgba(0,0,0,0.5); display: flex; align-items: center; justify-content: center; z-index: 1000;}
    .modal-card { background: white; border-radius: 8px; overflow: hidden; }
    .modal-header { padding: 1rem; border-bottom: 1px solid #eee; }
    .modal-body { padding: 1.5rem; }
    .timeline { border-left: 2px solid #eee; padding-left: 1rem; margin-left: 0.5rem;}
    .timeline-item { position: relative; margin-bottom: 1.5rem; }
    .timeline-item::before { content: ""; position: absolute; left: -1.45rem; top: 5px; width: 12px; height: 12px; background: var(--primary); border-radius: 50%; }
    .timeline-date { font-size: 0.7rem; color: #999; }
    .text-sm { font-size: 0.8rem; }
  `]
})
export class DisputeListComponent implements OnInit {
  private disputeService = inject(DisputeService);
  private customerService = inject(CustomerService);
  private notifications = inject(NotificationService);

  customers: Customer[] = [];
  selectedCustomerId = '';
  selectedCustomerAccounts: Account[] = [];
  selectedAccountId = '';

  cases: DisputeCase[] = [];
  selectedCase: DisputeCase | null = null;
  caseEvents: DisputeEvent[] = [];
  nextAction = '';
  actionNotes = '';

  actionOptions = [
    { id: 'INVESTIGATING', name: 'Mover a Investigación' },
    { id: 'REQUEST_EVIDENCE', name: 'Solicitar Evidencias' },
    { id: 'WIN', name: 'Ganar Disputa (Revertir cargo)' },
    { id: 'LOSS', name: 'Perder Disputa (Mantener cargo)' }
  ];

  get accountOptions() {
    return this.selectedCustomerAccounts.map(a => ({
      id: a.id,
      label: `Cuenta: ...${a.id.slice(-8)}`
    }));
  }

  ngOnInit() {
    this.customerService.getCustomers().pipe(
      catchError(() => {
        this.notifications.warning('No se pudo cargar la lista de clientes');
        return of([]);
      })
    ).subscribe(data => this.customers = data);
  }

  onCustomerChange(custId: string) {
    this.selectedAccountId = '';
    this.selectedCustomerAccounts = [];
    if (!custId) return;
    this.customerService.getCustomer(custId).pipe(
      catchError(() => {
        this.notifications.error('Error al obtener detalle del cliente');
        return of(null);
      })
    ).subscribe(d => {
      if (d) this.selectedCustomerAccounts = d.accounts || [];
    });
  }

  loadCases(accId: string) {
    this.disputeService.getDisputesByAccount(accId).pipe(
      catchError(err => {
        console.error('Error loading disputes:', err?.status, err?.message);
        this.notifications.warning('No se encontraron disputas para esta cuenta o hubo un error');
        return of([] as DisputeCase[]);
      })
    ).subscribe(data => this.cases = data);
  }

  getStatusClass(status: string): string {
    return 'status-' + status.toLowerCase();
  }

  viewDetails(c: DisputeCase) {
    this.selectedCase = c;
    this.disputeService.getDisputeEvents(c.id).pipe(
      catchError(err => {
        console.error('Error loading dispute events:', err?.status, err?.message);
        return of([] as DisputeEvent[]);
      })
    ).subscribe(evs => this.caseEvents = evs);
  }

  applyAction() {
    if (!this.selectedCase) return;
    this.disputeService.transitionDispute(this.selectedCase.id, this.nextAction, this.actionNotes).subscribe({
      next: () => {
        this.notifications.success('Estado de disputa actualizado');
        this.selectedCase = null;
        this.loadCases(this.selectedAccountId);
      },
      error: () => this.notifications.error('Error al actualizar el estado de la disputa')
    });
  }
}
