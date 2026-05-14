import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AnalyticsService, AnalyticsDashboard, ConsumptionAnalytics, FraudAnalytics } from './analytics.service';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';

type Tab = 'dashboard' | 'consumption' | 'fraud';

@Component({
  selector: 'app-analytics-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-container">
      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">bar_chart</span>
            Analytics & Business Intelligence
          </h1>
          <p class="text-muted mt-1">Tableros de portafolio, consumo y análisis de riesgo-fraude.</p>
        </div>
        <div class="d-flex gap-2 align-items-center">
          <select class="form-input" style="width: 140px" [(ngModel)]="selectedDays" (ngModelChange)="reload()">
            <option [value]="7">Últimos 7 días</option>
            <option [value]="30">Últimos 30 días</option>
            <option [value]="90">Últimos 90 días</option>
          </select>
          <button class="btn btn-outline d-flex align-items-center gap-2" (click)="reload()">
            <span class="material-symbols-rounded">refresh</span>
          </button>
        </div>
      </div>

      <!-- Tabs -->
      <div class="tab-bar mb-4">
        <button class="tab-btn" [class.active]="activeTab === 'dashboard'" (click)="setTab('dashboard')">
          <span class="material-symbols-rounded">dashboard</span> Portafolio
        </button>
        <button class="tab-btn" [class.active]="activeTab === 'consumption'" (click)="setTab('consumption')">
          <span class="material-symbols-rounded">shopping_cart</span> Consumo
        </button>
        <button class="tab-btn" [class.active]="activeTab === 'fraud'" (click)="setTab('fraud')">
          <span class="material-symbols-rounded">security</span> Fraude & Riesgo
        </button>
      </div>

      <!-- Loading -->
      <div *ngIf="loading" class="loading-state">
        <span class="material-symbols-rounded spin">sync</span>
        <p class="text-muted mt-2">Cargando datos...</p>
      </div>

      <!-- Dashboard Tab -->
      <div *ngIf="activeTab === 'dashboard' && !loading && dashboard">
        <div class="kpi-grid mb-4">
          <div class="kpi-card">
            <div class="kpi-icon blue"><span class="material-symbols-rounded">account_balance_wallet</span></div>
            <div class="kpi-body">
              <div class="kpi-value">{{ dashboard.portfolio.accounts | number }}</div>
              <div class="kpi-label">Total Cuentas</div>
            </div>
          </div>
          <div class="kpi-card">
            <div class="kpi-icon green"><span class="material-symbols-rounded">credit_card</span></div>
            <div class="kpi-body">
              <div class="kpi-value">{{ dashboard.portfolio.activeCards | number }}</div>
              <div class="kpi-label">Tarjetas Activas</div>
            </div>
          </div>
          <div class="kpi-card">
            <div class="kpi-icon purple"><span class="material-symbols-rounded">payments</span></div>
            <div class="kpi-body">
              <div class="kpi-value">{{ dashboard.portfolio.outstandingBalance | currency }}</div>
              <div class="kpi-label">Saldo Pendiente Total</div>
            </div>
          </div>
          <div class="kpi-card">
            <div class="kpi-icon orange"><span class="material-symbols-rounded">gavel</span></div>
            <div class="kpi-body">
              <div class="kpi-value">{{ dashboard.portfolio.openDisputeCount | number }}</div>
              <div class="kpi-label">Disputas Abiertas</div>
            </div>
          </div>
        </div>

        <div class="card" *ngIf="portfolioMetrics.length">
          <div class="card-header mb-3">
            <h3 class="m-0">Métricas de Portafolio</h3>
          </div>
          <div class="metrics-grid">
            <div class="metric-row" *ngFor="let metric of portfolioMetrics">
              <span class="metric-label">{{ metric.label }}</span>
              <div class="metric-bar-wrap">
                <div class="metric-bar" [style.width.%]="getBarWidth(metric.value, portfolioMetrics)"></div>
              </div>
              <span class="metric-value">{{ metric.value | currency }}</span>
              <span class="metric-change">—</span>
            </div>
          </div>
        </div>

        <div class="card mt-4" *ngIf="!portfolioMetrics.length">
          <div class="empty-state">
            <span class="material-symbols-rounded" style="font-size: 48px; opacity: 0.4">bar_chart</span>
            <p class="mt-2">No hay métricas de portafolio disponibles para el período seleccionado.</p>
          </div>
        </div>
      </div>

      <!-- Consumption Tab -->
      <div *ngIf="activeTab === 'consumption' && !loading && consumption">
        <div class="card mb-4">
          <div class="card-header d-flex justify-content-between align-items-center mb-3">
            <h3 class="m-0">Análisis de Consumo — Últimos {{ consumption.days }} días</h3>
          </div>
          <div class="table-responsive">
            <table class="table">
              <thead>
                <tr>
                  <th>CATEGORÍA</th>
                  <th class="text-right">TRANSACCIONES</th>
                  <th class="text-right">MONTO TOTAL</th>
                  <th>PARTICIPACIÓN</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let seg of consumption.categoryBreakdown">
                  <td class="font-weight-600">{{ seg.key }}</td>
                  <td class="text-right">{{ seg.count | number }}</td>
                  <td class="text-right font-weight-600 text-main">{{ seg.amount | currency }}</td>
                  <td>
                    <div class="progress-bar-wrap">
                      <div class="progress-bar-fill" [style.width.%]="seg.sharePercent"></div>
                      <span class="progress-label">{{ seg.sharePercent | number:'1.0-0' }}%</span>
                    </div>
                  </td>
                </tr>
                <tr *ngIf="!consumption.categoryBreakdown?.length">
                  <td colspan="4" class="empty-state">Sin datos de consumo para el período.</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <div class="card" *ngIf="consumption.categoryBreakdown?.length">
          <h3 class="m-0 mb-3">Top Categorías</h3>
          <div class="top-categories">
            <div class="top-cat" *ngFor="let cat of consumption.categoryBreakdown.slice(0,5); let i = index">
              <div class="top-cat-rank">#{{ i + 1 }}</div>
              <div class="top-cat-body">
                <div class="top-cat-name">{{ cat.key }}</div>
                <div class="top-cat-amount">{{ cat.amount | currency }} · {{ cat.count | number }} txs</div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Fraud Tab -->
      <div *ngIf="activeTab === 'fraud' && !loading && fraud">
        <div class="kpi-grid mb-4">
          <div class="kpi-card">
            <div class="kpi-icon red"><span class="material-symbols-rounded">warning</span></div>
            <div class="kpi-body">
              <div class="kpi-value text-danger">{{ fraud.openCases | number }}</div>
              <div class="kpi-label">Casos Abiertos</div>
            </div>
          </div>
          <div class="kpi-card">
            <div class="kpi-icon red"><span class="material-symbols-rounded">money_off</span></div>
            <div class="kpi-body">
              <div class="kpi-value text-danger">{{ fraud.totalExposureAmount | currency }}</div>
              <div class="kpi-label">Exposición Total</div>
            </div>
          </div>
        </div>

        <div class="card">
          <h3 class="m-0 mb-3">Indicadores por Razón — Últimos {{ fraud.days }} días</h3>
          <div class="table-responsive">
            <table class="table">
              <thead>
                <tr>
                  <th>RAZÓN / ESTADO</th>
                  <th class="text-right">CANTIDAD</th>
                  <th class="text-right">MONTO EXPUESTO</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let ind of fraud.reasonCodeBreakdown">
                  <td>
                    <div class="d-flex align-items-center gap-2">
                      <span class="material-symbols-rounded text-danger" style="font-size: 1.1rem">error</span>
                      {{ ind.key }}
                    </div>
                  </td>
                  <td class="text-right font-weight-600">{{ ind.count | number }}</td>
                  <td class="text-right text-danger font-weight-600">{{ ind.amount | currency }}</td>
                </tr>
                <tr *ngIf="!fraud.reasonCodeBreakdown?.length">
                  <td colspan="3" class="empty-state">
                    <span class="material-symbols-rounded" style="font-size: 48px; opacity: 0.4; color: #10b981">verified_user</span>
                    <p class="mt-2 text-success">Sin indicadores de fraude en el período. ¡Buenas noticias!</p>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <!-- No data state -->
      <div *ngIf="!loading && noData" class="card">
        <div class="empty-state">
          <span class="material-symbols-rounded" style="font-size: 48px; opacity: 0.4">bar_chart</span>
          <p class="mt-2">No se pudo cargar la información. Verifique que el backend esté disponible.</p>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .font-weight-600 { font-weight: 600; }
    .text-main { color: var(--text-main); }

    .tab-bar { display: flex; gap: 0.5rem; border-bottom: 2px solid var(--border-color); }
    .tab-btn { display: flex; align-items: center; gap: 0.5rem; padding: 0.75rem 1.25rem; border: none; background: none; color: var(--text-muted); font-size: 0.875rem; font-weight: 500; cursor: pointer; border-bottom: 2px solid transparent; margin-bottom: -2px; transition: all 0.2s; }
    .tab-btn.active { color: var(--primary); border-bottom-color: var(--primary); }
    .tab-btn:hover:not(.active) { color: var(--text-main); }

    .loading-state { text-align: center; padding: 4rem; }
    @keyframes spin { to { transform: rotate(360deg); } }
    .spin { animation: spin 1s linear infinite; font-size: 2.5rem; color: var(--primary); }

    .kpi-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 1rem; }
    .kpi-card { background: var(--bg-card); border: 1px solid var(--border-color); border-radius: var(--radius-md); padding: 1.25rem; display: flex; align-items: center; gap: 1rem; }
    .kpi-icon { width: 48px; height: 48px; border-radius: var(--radius-sm); display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .kpi-icon.blue { background: #dbeafe; color: #1d4ed8; }
    .kpi-icon.green { background: #d1fae5; color: #047857; }
    .kpi-icon.purple { background: #ede9fe; color: #7c3aed; }
    .kpi-icon.orange { background: #fff7ed; color: #c2410c; }
    .kpi-icon.red { background: #fee2e2; color: #b91c1c; }
    .kpi-value { font-size: 1.5rem; font-weight: 700; color: var(--text-main); }
    .kpi-label { font-size: 0.75rem; color: var(--text-muted); font-weight: 500; margin-top: 0.1rem; }
    .text-danger { color: #b91c1c; }
    .text-success { color: #047857; }

    .card-header { display: flex; justify-content: space-between; align-items: center; }
    .metrics-grid { display: flex; flex-direction: column; gap: 0.75rem; }
    .metric-row { display: grid; grid-template-columns: 200px 1fr 140px 70px; align-items: center; gap: 1rem; }
    .metric-label { font-size: 0.875rem; font-weight: 500; }
    .metric-bar-wrap { background: var(--border-color); border-radius: 4px; height: 8px; overflow: hidden; }
    .metric-bar { background: var(--primary); height: 100%; border-radius: 4px; transition: width 0.5s ease; }
    .metric-value { font-size: 0.875rem; font-weight: 600; text-align: right; }
    .metric-change { font-size: 0.75rem; font-weight: 600; text-align: right; }
    .metric-change.positive { color: #047857; }
    .metric-change.negative { color: #b91c1c; }

    .table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
    .table th, .table td { padding: 1rem 1.5rem; text-align: left; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
    .table th { color: var(--text-muted); font-weight: 600; font-size: 0.75rem; text-transform: uppercase; }
    .text-right { text-align: right; }

    .progress-bar-wrap { display: flex; align-items: center; gap: 0.5rem; }
    .progress-bar-fill { height: 6px; background: var(--primary); border-radius: 3px; min-width: 2px; }
    .progress-label { font-size: 0.75rem; color: var(--text-muted); white-space: nowrap; }

    .top-categories { display: grid; grid-template-columns: repeat(auto-fill, minmax(250px, 1fr)); gap: 0.75rem; }
    .top-cat { display: flex; align-items: center; gap: 1rem; padding: 0.875rem 1rem; background: var(--bg-card-alt, #f9fafb); border-radius: var(--radius-sm); border: 1px solid var(--border-color); }
    .top-cat-rank { font-size: 1.25rem; font-weight: 700; color: var(--primary); min-width: 2rem; }
    .top-cat-name { font-weight: 600; font-size: 0.875rem; }
    .top-cat-amount { font-size: 0.75rem; color: var(--text-muted); margin-top: 0.1rem; }

    .empty-state { text-align: center; padding: 3rem; color: var(--text-muted); }

    .form-input { padding: 0.625rem 0.875rem; border: 1px solid var(--border-color); border-radius: var(--radius-sm); font-size: 0.875rem; background: white; }
    .form-input:focus { outline: none; border-color: var(--primary); }
  `]
})
export class AnalyticsDashboardComponent implements OnInit {
  private analyticsService = inject(AnalyticsService);

  activeTab: Tab = 'dashboard';
  selectedDays = 30;
  loading = false;
  noData = false;

  dashboard: AnalyticsDashboard | null = null;
  consumption: ConsumptionAnalytics | null = null;
  fraud: FraudAnalytics | null = null;

  ngOnInit() {
    this.reload();
  }

  setTab(tab: Tab) {
    this.activeTab = tab;
  }

  reload() {
    this.loading = true;
    this.noData = false;

    this.analyticsService.getDashboard(this.selectedDays).pipe(
      catchError(() => of(null))
    ).subscribe(data => { this.dashboard = data; this.loading = false; if (!data) this.noData = true; });

    this.analyticsService.getConsumption(this.selectedDays).pipe(
      catchError(() => of(null))
    ).subscribe(data => { this.consumption = data; });

    this.analyticsService.getFraud(this.selectedDays).pipe(
      catchError(() => of(null))
    ).subscribe(data => { this.fraud = data; });
  }

  getBarWidth(value: number, metrics: { value: number }[]): number {
    const max = Math.max(...metrics.map(m => m.value));
    return max ? (value / max) * 100 : 0;
  }

  get portfolioMetrics(): { label: string; value: number }[] {
    if (!this.dashboard?.portfolio) return [];
    const p = this.dashboard.portfolio;
    return [
      { label: 'Límite Crédito Total', value: p.totalCreditLimit },
      { label: 'Crédito Disponible', value: p.availableCredit },
      { label: 'Saldo Pendiente', value: p.outstandingBalance },
      { label: 'Saldo Estado Abierto', value: p.openStatementBalance },
    ].filter(m => m.value > 0);
  }
}
