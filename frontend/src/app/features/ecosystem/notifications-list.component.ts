import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { NotificationsService, CustomerNotification } from './notifications.service';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';
import { NotificationService } from '../../core/notification.service';

const NOTIFICATION_TYPES = ['', 'PaymentDue', 'TransactionAlert', 'FraudAlert', 'LimitChange', 'StatementReady', 'CardBlocked', 'SystemAlert'];

@Component({
  selector: 'app-notifications-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-container">
      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">notifications</span>
            Centro de Notificaciones
          </h1>
          <p class="text-muted mt-1">Historial de comunicaciones push, SMS y email enviadas a clientes.</p>
        </div>
        <button class="btn btn-outline d-flex align-items-center gap-2" (click)="load()">
          <span class="material-symbols-rounded">refresh</span> Recargar
        </button>
      </div>

      <!-- Filters -->
      <div class="filters-bar card mb-4">
        <div class="filters-grid">
          <div class="form-group">
            <label class="form-label">ID de Cliente</label>
            <input class="form-input" [(ngModel)]="filters.customerId" placeholder="UUID del cliente" />
          </div>
          <div class="form-group">
            <label class="form-label">ID de Cuenta</label>
            <input class="form-input" [(ngModel)]="filters.accountId" placeholder="UUID de la cuenta" />
          </div>
          <div class="form-group">
            <label class="form-label">Tipo</label>
            <select class="form-input" [(ngModel)]="filters.type">
              <option value="">Todos</option>
              <option *ngFor="let t of notificationTypes.slice(1)" [value]="t">{{ t }}</option>
            </select>
          </div>
          <div class="form-group">
            <label class="form-label">Registros</label>
            <select class="form-input" [(ngModel)]="filters.take">
              <option [value]="25">25</option>
              <option [value]="50">50</option>
              <option [value]="100">100</option>
            </select>
          </div>
        </div>
        <div class="d-flex justify-content-end mt-3">
          <button class="btn btn-primary d-flex align-items-center gap-2" (click)="load()">
            <span class="material-symbols-rounded">filter_list</span> Aplicar filtros
          </button>
        </div>
      </div>

      <!-- Stats pills -->
      <div class="stats-bar mb-4" *ngIf="notifications.length">
        <div class="stat-pill" *ngFor="let stat of computeStats()">
          <span class="stat-dot" [style.background]="stat.color"></span>
          <span class="stat-label">{{ stat.type }}</span>
          <span class="stat-count">{{ stat.count }}</span>
        </div>
      </div>

      <!-- Table -->
      <div class="card p-0">
        <div class="table-responsive">
          <table class="table">
            <thead>
              <tr>
                <th>TIPO</th>
                <th>CANAL</th>
                <th>ASUNTO</th>
                <th>CLIENTE</th>
                <th>ESTADO</th>
                <th>ENVIADO</th>
                <th class="text-center">VER</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let notif of notifications" (click)="selectedNotif = notif" class="clickable-row">
                <td>
                  <span class="type-badge" [ngClass]="getTypeClass(notif.type)">{{ notif.type }}</span>
                </td>
                <td>
                  <div class="d-flex align-items-center gap-1">
                    <span class="material-symbols-rounded channel-icon">{{ getChannelIcon(notif.channel) }}</span>
                    {{ notif.channel }}
                  </div>
                </td>
                <td class="subject-cell">{{ notif.subject }}</td>
                <td class="font-mono text-xs">{{ notif.customerId ? (notif.customerId | slice:0:8) + '...' : '—' }}</td>
                <td>
                  <span class="status-badge" [class.sent]="notif.status === 'Sent'" [class.failed]="notif.status === 'Failed'" [class.pending]="notif.status === 'Pending'">
                    {{ notif.status }}
                  </span>
                </td>
                <td class="text-muted text-xs">{{ notif.sentAt | date:'short' }}</td>
                <td class="text-center">
                  <button class="btn btn-icon" (click)="selectedNotif = notif; $event.stopPropagation()">
                    <span class="material-symbols-rounded">visibility</span>
                  </button>
                </td>
              </tr>
              <tr *ngIf="notifications.length === 0 && !loading">
                <td colspan="7" class="empty-state">
                  <span class="material-symbols-rounded" style="font-size: 48px; opacity: 0.3">notifications_off</span>
                  <p class="mt-2">No hay notificaciones para los filtros seleccionados.</p>
                </td>
              </tr>
              <tr *ngIf="loading">
                <td colspan="7" class="empty-state">
                  <span class="material-symbols-rounded spin">sync</span>
                  <p class="mt-2 text-muted">Cargando...</p>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <!-- Detail Modal -->
    <div class="modal-backdrop" *ngIf="selectedNotif">
      <div class="modal-card" style="max-width: 560px; width: 90%">
        <div class="modal-header d-flex justify-content-between align-items-start">
          <div>
            <div class="d-flex align-items-center gap-2 mb-1">
              <span class="type-badge" [ngClass]="getTypeClass(selectedNotif.type)">{{ selectedNotif.type }}</span>
              <span class="status-badge" [class.sent]="selectedNotif.status === 'Sent'" [class.failed]="selectedNotif.status === 'Failed'">{{ selectedNotif.status }}</span>
            </div>
            <h3 class="m-0">{{ selectedNotif.subject }}</h3>
          </div>
          <button class="btn-close" (click)="selectedNotif = null">×</button>
        </div>
        <div class="modal-body">
          <div class="detail-meta mb-3">
            <div class="detail-row" *ngIf="selectedNotif.customerId">
              <span class="detail-key">Cliente</span><span class="font-mono text-xs">{{ selectedNotif.customerId }}</span>
            </div>
            <div class="detail-row" *ngIf="selectedNotif.accountId">
              <span class="detail-key">Cuenta</span><span class="font-mono text-xs">{{ selectedNotif.accountId }}</span>
            </div>
            <div class="detail-row">
              <span class="detail-key">Canal</span>
              <span class="d-flex align-items-center gap-1">
                <span class="material-symbols-rounded channel-icon">{{ getChannelIcon(selectedNotif.channel) }}</span>
                {{ selectedNotif.channel }}
              </span>
            </div>
            <div class="detail-row">
              <span class="detail-key">Enviado</span><span>{{ selectedNotif.sentAt | date:'long' }}</span>
            </div>
          </div>
          <div class="notif-body">
            <p class="body-label">Cuerpo del mensaje</p>
            <p class="body-text">{{ selectedNotif.body }}</p>
          </div>
        </div>
        <div class="modal-footer d-flex justify-content-end p-3">
          <button class="btn btn-outline" (click)="selectedNotif = null">Cerrar</button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .text-muted { color: var(--text-muted); }
    .font-mono { font-family: 'Roboto Mono', monospace; }
    .text-xs { font-size: 0.75rem; }

    .filters-bar { padding: 1.25rem; }
    .filters-grid { display: grid; grid-template-columns: 1fr 1fr 1fr 120px; gap: 1rem; }
    @media (max-width: 800px) { .filters-grid { grid-template-columns: 1fr 1fr; } }

    .stats-bar { display: flex; flex-wrap: wrap; gap: 0.5rem; }
    .stat-pill { display: flex; align-items: center; gap: 0.4rem; padding: 0.3rem 0.8rem; background: var(--bg-card); border: 1px solid var(--border-color); border-radius: 20px; font-size: 0.8rem; }
    .stat-dot { width: 8px; height: 8px; border-radius: 50%; }
    .stat-count { font-weight: 700; color: var(--primary); }

    .table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
    .table th, .table td { padding: 0.875rem 1.25rem; text-align: left; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
    .table th { color: var(--text-muted); font-weight: 600; font-size: 0.75rem; text-transform: uppercase; }
    .text-center { text-align: center; }
    .clickable-row { cursor: pointer; transition: background 0.15s; }
    .clickable-row:hover { background: var(--bg-hover, #f9fafb); }
    .subject-cell { max-width: 200px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .empty-state { text-align: center; padding: 3rem; color: var(--text-muted); }

    .type-badge { padding: 0.2rem 0.5rem; border-radius: 4px; font-size: 0.7rem; font-weight: 700; text-transform: uppercase; }
    .type-fraud { background: #fee2e2; color: #b91c1c; }
    .type-payment { background: #d1fae5; color: #047857; }
    .type-statement { background: #ede9fe; color: #7c3aed; }
    .type-limit { background: #fef3c7; color: #92400e; }
    .type-default { background: #f3f4f6; color: #6b7280; }

    .channel-icon { font-size: 1rem; color: var(--text-muted); }

    .status-badge { padding: 0.2rem 0.5rem; border-radius: 4px; font-size: 0.7rem; font-weight: 700; }
    .status-badge.sent { background: #d1fae5; color: #047857; }
    .status-badge.failed { background: #fee2e2; color: #b91c1c; }
    .status-badge.pending { background: #fef3c7; color: #92400e; }

    @keyframes spin { to { transform: rotate(360deg); } }
    .spin { animation: spin 1s linear infinite; font-size: 2rem; }

    .modal-backdrop { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.5); display: flex; align-items: center; justify-content: center; z-index: 1000; }
    .modal-card { background: white; border-radius: 12px; box-shadow: 0 20px 40px rgba(0,0,0,0.2); overflow: hidden; }
    .modal-header { padding: 1.25rem 1.5rem; border-bottom: 1px solid var(--border-color); }
    .modal-body { padding: 1.5rem; }
    .btn-close { border: none; background: none; font-size: 1.5rem; cursor: pointer; color: var(--text-muted); }

    .detail-meta { display: flex; flex-direction: column; gap: 0; }
    .detail-row { display: flex; justify-content: space-between; padding: 0.5rem 0; border-bottom: 1px solid var(--border-color); font-size: 0.875rem; }
    .detail-key { color: var(--text-muted); font-weight: 500; }

    .notif-body { background: var(--bg-card-alt, #f9fafb); border-radius: 8px; padding: 1rem; }
    .body-label { font-size: 0.7rem; text-transform: uppercase; font-weight: 700; color: var(--text-muted); margin: 0 0 0.5rem; }
    .body-text { margin: 0; font-size: 0.875rem; line-height: 1.6; }

    .form-group { display: flex; flex-direction: column; gap: 0.4rem; }
    .form-label { font-size: 0.8rem; font-weight: 600; color: var(--text-muted); }
    .form-input { padding: 0.625rem 0.875rem; border: 1px solid var(--border-color); border-radius: var(--radius-sm); font-size: 0.875rem; background: white; width: 100%; }
    .form-input:focus { outline: none; border-color: var(--primary); }
  `]
})
export class NotificationsListComponent implements OnInit {
  private notificationsService = inject(NotificationsService);
  private uiNotifications = inject(NotificationService);

  notifications: CustomerNotification[] = [];
  selectedNotif: CustomerNotification | null = null;
  loading = false;
  notificationTypes = NOTIFICATION_TYPES;

  filters = { customerId: '', accountId: '', type: '', take: 50 };

  ngOnInit() {
    this.load();
  }

  load() {
    this.loading = true;
    this.notificationsService.list({
      customerId: this.filters.customerId || undefined,
      accountId: this.filters.accountId || undefined,
      type: this.filters.type || undefined,
      take: this.filters.take
    }).pipe(catchError(() => {
        this.uiNotifications.warning('Error al cargar el historial de notificaciones');
        return of([] as CustomerNotification[]);
      })
    ).subscribe(data => { this.notifications = data; this.loading = false; });
  }

  getTypeClass(type: string): string {
    if (type?.toLowerCase().includes('fraud')) return 'type-fraud';
    if (type?.toLowerCase().includes('payment')) return 'type-payment';
    if (type?.toLowerCase().includes('statement')) return 'type-statement';
    if (type?.toLowerCase().includes('limit')) return 'type-limit';
    return 'type-default';
  }

  getChannelIcon(channel: string): string {
    switch (channel?.toLowerCase()) {
      case 'push': return 'phone_android';
      case 'sms': return 'sms';
      case 'email': return 'email';
      default: return 'notifications';
    }
  }

  computeStats(): { type: string; count: number; color: string }[] {
    const typeColors: Record<string, string> = {
      FraudAlert: '#b91c1c', PaymentDue: '#047857', StatementReady: '#7c3aed',
      LimitChange: '#d97706', CardBlocked: '#dc2626', TransactionAlert: '#2563eb', SystemAlert: '#6b7280'
    };
    const counts: Record<string, number> = {};
    this.notifications.forEach(n => { counts[n.type] = (counts[n.type] || 0) + 1; });
    return Object.entries(counts).slice(0, 6).map(([type, count]) => ({ type, count, color: typeColors[type] || '#9ca3af' }));
  }
}
