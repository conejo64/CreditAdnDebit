import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../core/auth.service';
import { DashboardService, DashboardTransaction } from './dashboard.service';
import { AnalyticsService } from '../finance/analytics.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="dashboard-header d-flex justify-content-between align-items-center mb-4">
      <div>
        <h1 class="page-title">Dashboard General</h1>
        <p class="text-muted mt-1">Resumen de actividad y consumo de Zitron / CardSwitchPlatform.</p>
      </div>
      <button class="btn btn-primary" (click)="refreshData()">
        <span class="material-symbols-rounded">refresh</span> Actualizar
      </button>
    </div>

    <div class="kpi-grid mb-4">
      <div class="kpi-card">
        <div class="kpi-card-inner">
          <div class="kpi-content">
            <span class="kpi-label">CLIENTES ACTIVOS</span>
            <div class="kpi-value">{{ totalCustomers }}</div>
            <div class="kpi-trend text-success">
              <span class="material-symbols-rounded">arrow_upward</span> +12 Esta semana
            </div>
          </div>
          <div class="kpi-icon icon-blue">
            <span class="material-symbols-rounded">group</span>
          </div>
        </div>
      </div>
      
      <div class="kpi-card" *ngIf="hasAnyRole('Admin', 'Operator')">
        <div class="kpi-card-inner">
          <div class="kpi-content">
            <span class="kpi-label">TARJETAS EMITIDAS</span>
            <div class="kpi-value">{{ totalCards }}</div>
            <div class="kpi-trend text-muted">
              Tarjetas activas en portafolio
            </div>
          </div>
          <div class="kpi-icon icon-purple">
            <span class="material-symbols-rounded">credit_card</span>
          </div>
        </div>
      </div>

      <div class="kpi-card" *ngIf="hasAnyRole('Admin', 'Operator', 'Auditor')">
        <div class="kpi-card-inner">
          <div class="kpi-content">
            <span class="kpi-label">ÚLTIMAS TRX ISO</span>
            <div class="kpi-value">{{ totalIsos }}</div>
            <div class="kpi-trend text-muted">Historial Switch API</div>
          </div>
          <div class="kpi-icon icon-green">
            <span class="material-symbols-rounded">swap_horiz</span>
          </div>
        </div>
      </div>

      <div class="kpi-card" *ngIf="hasAnyRole('Admin', 'Auditor')">
        <div class="kpi-card-inner">
          <div class="kpi-content">
            <span class="kpi-label">LIQUIDACIÓN MENS.</span>
            <div class="kpi-value">{{ liquidacion }}</div>
            <div class="kpi-trend text-muted">Consumo últimos 30 días</div>
          </div>
          <div class="kpi-icon icon-indigo">
            <span class="material-symbols-rounded">account_balance</span>
          </div>
        </div>
      </div>
      
      <div class="kpi-card error-card" *ngIf="hasAnyRole('Admin', 'Auditor')">
        <div class="kpi-card-inner">
          <div class="kpi-content">
            <span class="kpi-label text-warning">RECHAZADAS BÓVEDA</span>
            <div class="kpi-value text-warning">{{ rechazadas }}</div>
            <div class="kpi-trend text-muted">Disputas / casos abiertos</div>
          </div>
          <div class="kpi-icon icon-warning">
            <span class="material-symbols-rounded">warning</span>
          </div>
        </div>
      </div>
    </div>

    <!-- Table Section -->
    <div class="card p-0">
      <div class="card-header pb-0 border-bottom">
        <h3 class="card-title text-main d-flex align-items-center gap-2 mb-3">
          <span class="material-symbols-rounded">history</span> Últimas Transacciones (Switch Journal)
        </h3>
      </div>
      <div class="table-responsive">
        <table class="table">
          <thead>
            <tr>
              <th>MTI / TIPO</th>
              <th>ACCOUNT ID</th>
              <th>MONTO</th>
              <th>ESTADO</th>
              <th class="text-right">TIEMPO</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let tx of transactions">
              <td>
                <div class="d-flex align-items-center gap-2 font-weight-500">
                  <span class="material-symbols-rounded" [ngClass]="tx.status === 'APPROVED' ? 'text-primary' : 'text-danger'">point_of_sale</span>
                  {{tx.txType}}
                </div>
              </td>
              <td class="text-muted font-monospace">{{tx.traceId.substring(0,8)}}...</td>
              <td>$ {{ tx.amount12 ? (tx.amount12 | number) : '0.00' }} <small>{{tx.currency}}</small></td>
              <td>
                <span class="badge" [ngClass]="tx.responseCode === '00' ? 'badge-success' : 'badge-danger'">
                  {{ tx.responseCode }} - {{ tx.status }}
                </span>
              </td>
              <td class="text-right text-muted" title="{{ tx.createdOn }}">{{ tx.createdOn | date:'medium' }}</td>
            </tr>
            <tr *ngIf="transactions.length === 0">
              <td colspan="5" class="text-center text-muted p-4">Cargando transacciones en vivo del Switch...</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  `,
  styles: [`
    .page-title {
      font-size: 1.5rem;
      color: var(--primary);
      margin: 0;
    }

    .kpi-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
      gap: 1.5rem;
    }

    .kpi-card {
      background: var(--bg-paper);
      border-radius: var(--radius-lg);
      padding: 1.5rem;
      box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.05);
      border: 1px solid var(--border-color);
      position: relative;
      overflow: hidden;
    }

    .kpi-card::before {
      content: '';
      position: absolute;
      top: 0;
      left: 0;
      width: 4px;
      height: 100%;
      background-color: transparent;
      border-radius: 4px 0 0 4px;
    }
    .kpi-card:nth-child(1)::before { background-color: var(--primary); }
    .kpi-card:nth-child(2)::before { background-color: var(--secondary); }
    .kpi-card:nth-child(3)::before { background-color: var(--success); }
    .kpi-card:nth-child(4)::before { background-color: #3f51b5; }
    
    .error-card {
      border-color: #fdd835;
    }
    .error-card::before { background-color: var(--warning); }

    .kpi-card-inner {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      position: relative;
      z-index: 1;
    }

    .kpi-label {
      font-size: 0.75rem;
      font-weight: 700;
      color: var(--text-muted);
      letter-spacing: 0.5px;
    }

    .kpi-value {
      font-size: 2rem;
      font-weight: 800;
      color: var(--text-main);
      margin: 0.5rem 0;
    }

    .kpi-trend {
      font-size: 0.75rem;
      display: flex;
      align-items: center;
      gap: 0.25rem;
    }
    
    .kpi-trend .material-symbols-rounded { font-size: 14px; }
    
    .text-success { color: var(--success); }
    .text-warning { color: var(--warning); }

    .kpi-icon {
      width: 48px;
      height: 48px;
      border-radius: var(--radius-lg);
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 24px;
    }
    
    .icon-blue { background-color: #e3f2fd; color: #1976d2; }
    .icon-purple { background-color: #f3e5f5; color: #9c27b0; }
    .icon-green { background-color: #e8f5e9; color: #388e3c; }
    .icon-indigo { background-color: #e8eaf6; color: #3f51b5; }
    .icon-warning { background-color: #fff8e1; color: #ffa000; }

    /* Abstract shapes in BG */
    .kpi-card::after {
      content: '';
      position: absolute;
      bottom: -20px;
      right: -20px;
      width: 100px;
      height: 100px;
      border-radius: 50%;
      opacity: 0.05;
      z-index: 0;
    }
    .kpi-card:nth-child(1)::after { background-color: var(--primary); }
    .kpi-card:nth-child(2)::after { background-color: var(--secondary); }
    .kpi-card:nth-child(3)::after { background-color: var(--success); }

    /* Table styles */
    .table-responsive {
      overflow-x: auto;
    }
    .table {
      width: 100%;
      border-collapse: collapse;
      font-size: 0.875rem;
    }
    .table th, .table td {
      padding: 1rem 1.5rem;
      text-align: left;
      border-bottom: 1px solid var(--border-color);
    }
    .table th {
      color: var(--text-muted);
      font-weight: 600;
      font-size: 0.75rem;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }
    .table tbody tr:hover { background-color: #fafbfc; }
    .table tbody tr:last-child td { border-bottom: none; }
    
    .badge {
      padding: 0.25rem 0.5rem;
      border-radius: var(--radius-sm);
      font-size: 0.75rem;
      font-weight: 600;
    }
    .badge-success {
      background-color: #e8f5e9;
      color: #2e7d32;
    }
    .badge-success::before {
      content: '●';
      margin-right: 4px;
      font-size: 10px;
    }

    .font-weight-500 { font-weight: 500; }
    .text-right { text-align: right !important; }
  `]
})
export class DashboardComponent implements OnInit {
  authService = inject(AuthService);
  dashboardService = inject(DashboardService);
  analyticsService = inject(AnalyticsService);

  user = this.authService.currentUser;

  transactions: DashboardTransaction[] = [];
  totalCustomers: string = '...';
  totalIsos: string = '...';
  totalCards: string = '...';
  liquidacion: string = '...';
  rechazadas: string = '...';

  ngOnInit(): void {
    this.refreshData();
  }

  hasAnyRole(...roles: string[]): boolean {
    return this.authService.hasAnyRole(...roles);
  }

  refreshData() {
    this.dashboardService.getLatestTransactions(10).subscribe({
      next: (res) => {
        this.transactions = res.items || [];
        this.totalIsos = (res.count || 0).toString();
      },
      error: (err) => console.error('Error fetching transactions', err)
    });

    this.dashboardService.getCustomersCount().subscribe({
      next: (res) => {
        this.totalCustomers = (res.count !== undefined) ? res.count.toString() : '0';
      },
      error: () => this.totalCustomers = 'N/A'
    });

    this.analyticsService.getDashboard(30).subscribe({
      next: (res) => {
        this.totalCards = (res.portfolio?.activeCards ?? 0).toLocaleString();
        const vol = res.consumption?.grossConsumptionAmount ?? 0;
        this.liquidacion = vol >= 1_000_000
          ? '$' + (vol / 1_000_000).toFixed(1) + 'M'
          : '$' + vol.toLocaleString('es-AR', { maximumFractionDigits: 0 });
        this.rechazadas = (res.portfolio?.openDisputeCount ?? 0).toString();
      },
      error: () => {
        this.totalCards = 'N/A';
        this.liquidacion = 'N/A';
        this.rechazadas = 'N/A';
      }
    });
  }
}
