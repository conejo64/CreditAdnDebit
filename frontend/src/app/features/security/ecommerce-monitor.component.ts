import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SwitchService } from '../switch/switch.service';
import { catchError, of } from 'rxjs';

@Component({
  selector: 'app-ecommerce-monitor',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="page-container">
      <div class="page-header mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">shopping_cart_checkout</span>
            E-commerce Security Monitor (v70)
          </h1>
          <p class="text-muted mt-1">Supervisión de transacciones no presenciales (CNP) y autenticación 3D Secure 2.0.</p>
        </div>
      </div>

      <!-- Overview Cards -->
      <div class="stats-row mb-4">
        <div class="stat-card card">
           <div class="stat-icon bg-soft-info"><span class="material-symbols-rounded">verified_user</span></div>
           <div class="stat-content">
              <label>3DS Authenticated</label>
              <strong>84.2%</strong>
              <small class="text-success">↑ 1.2% vs ayer</small>
           </div>
        </div>
        <div class="stat-card card">
           <div class="stat-icon bg-soft-warning"><span class="material-symbols-rounded">rule</span></div>
           <div class="stat-content">
              <label>Frictionless Flow</label>
              <strong>65.0%</strong>
              <small class="text-muted">Target: >60%</small>
           </div>
        </div>
        <div class="stat-card card">
           <div class="stat-icon bg-soft-danger"><span class="material-symbols-rounded">block</span></div>
           <div class="stat-content">
              <label>Fraud Attempts (CNP)</label>
              <strong>12</strong>
              <small class="text-danger">Detectados hoy</small>
           </div>
        </div>
        <div class="stat-card card">
           <div class="stat-icon bg-soft-primary"><span class="material-symbols-rounded">payments</span></div>
           <div class="stat-content">
              <label>Approval Rate</label>
              <strong>92.5%</strong>
           </div>
        </div>
      </div>

      <div class="row">
         <div class="col-12">
            <div class="card p-0">
               <div class="card-header border-bottom p-3 d-flex justify-content-between align-items-center">
                  <h3 class="m-0 text-main font-size-base">Panel de Monitoreo en Vivo (CNP)</h3>
                  <div class="d-flex gap-2">
                     <span class="badge badge-outline">3DS v2.2</span>
                     <span class="badge badge-outline">SCA Active</span>
                  </div>
               </div>
               <div class="table-responsive">
                  <table class="table mb-0">
                     <thead class="bg-light">
                        <tr>
                           <th>Timestamp</th>
                           <th>Comercio (Merchant)</th>
                           <th>Tarjeta (Masked)</th>
                           <th>Monto</th>
                           <th>3DS Status</th>
                           <th>Decision</th>
                        </tr>
                     </thead>
                     <tbody>
                        <tr *ngFor="let txn of txns">
                           <td>{{ txn.time }}</td>
                           <td>
                              <div class="d-flex flex-column">
                                 <span class="font-weight-600">{{ txn.merchant }}</span>
                                 <small class="text-muted">{{ txn.category }}</small>
                              </div>
                           </td>
                           <td class="font-mono text-sm">{{ txn.card }}</td>
                           <td class="font-weight-600">{{ txn.amount | currency }}</td>
                           <td>
                              <span class="badge" [ngClass]="get3dsClass(txn.threeDS)">
                                 {{ txn.threeDS }}
                              </span>
                           </td>
                           <td>
                              <span class="badge" [ngClass]="txn.approved ? 'badge-success' : 'badge-danger'">
                                 {{ txn.approved ? 'APPROVED' : 'DECLINED' }}
                              </span>
                           </td>
                        </tr>
                     </tbody>
                  </table>
               </div>
            </div>
         </div>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .stats-row { display: grid; grid-template-columns: repeat(4, 1fr); gap: 1rem; }
    .stat-card { display: flex; align-items: center; gap: 1rem; padding: 1.25rem; }
    .stat-icon { width: 48px; height: 48px; border-radius: 12px; display: flex; align-items: center; justify-content: center; font-size: 24px; }
    .stat-content label { font-size: 0.72rem; text-transform: uppercase; color: var(--text-muted); font-weight: 600; display: block; margin-bottom: 2px; }
    .stat-content strong { font-size: 1.2rem; display: block; }
    .stat-content small { font-size: 0.65rem; }

    .bg-soft-primary { background: #eff6ff; color: #1d4ed8; }
    .bg-soft-info { background: #f0f9ff; color: #0369a1; }
    .bg-soft-warning { background: #fffbeb; color: #92400e; }
    .bg-soft-danger { background: #fef2f2; color: #b91c1c; }

    .badge-outline { border: 1px solid var(--border-color); color: var(--text-muted); font-size: 0.7rem; }
    .badge-success { background: #ecfdf5; color: #047857; }
    .badge-danger { background: #fef2f2; color: #b91c1c; }
    .badge-warning { background: #fffbeb; color: #92400e; }
    .badge-info { background: #f0f9ff; color: #0369a1; }

    .font-size-base { font-size: 1rem; }
    .font-mono { font-family: 'Roboto Mono', monospace; }
    .font-weight-600 { font-weight: 600; }
    
    .table th { font-size: 0.72rem; text-transform: uppercase; color: var(--text-muted); padding: 0.75rem 1rem; border-bottom: none; }
    .table td { padding: 0.75rem 1rem; vertical-align: middle; border-bottom: 1px solid #f1f5f9; }
  `]
})
export class EcommerceMonitorComponent {
  txns = [
    { time: '17:35:10', merchant: 'Amazon.com', category: 'E-commerce', card: '4111 11** **** 9012', amount: 142.50, threeDS: 'Frictionless', approved: true },
    { time: '17:34:02', merchant: 'Netflix Services', category: 'Subscriptions', card: '5244 55** **** 0119', amount: 12.99, threeDS: 'Stored Credential', approved: true },
    { time: '17:32:45', merchant: 'AliExpress Global', category: 'General Retail', card: '4111 11** **** 4421', amount: 8.40, threeDS: 'Challenge Required', approved: false },
    { time: '17:30:11', merchant: 'Apple Store', category: 'Technology', card: '4532 99** **** 5501', amount: 1299.00, threeDS: 'Frictionless', approved: true },
    { time: '17:28:55', merchant: 'Unknown Merchant', category: 'Unknown', card: '4111 11** **** 1111', amount: 450.00, threeDS: 'Denied (Risk)', approved: false }
  ];

  get3dsClass(status: string) {
    if (status.includes('Frictionless')) return 'badge-success';
    if (status.includes('Challenge')) return 'badge-warning';
    if (status.includes('Risk') || status.includes('Denied')) return 'badge-danger';
    return 'badge-info';
  }
}
