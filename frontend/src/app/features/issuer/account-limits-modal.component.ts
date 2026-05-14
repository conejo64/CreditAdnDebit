import { Component, EventEmitter, Input, Output, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CustomerService, Account, AccountLimit } from './customers/customer.service';
import { NotificationService } from '../../core/notification.service';

@Component({
  selector: 'app-account-limits-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="modal-backdrop fade show"></div>
    <div class="modal fade show d-block" tabindex="-1">
      <div class="modal-dialog modal-dialog-centered">
        <div class="modal-content border-0 shadow-lg">
          <div class="modal-header bg-light border-bottom">
            <h5 class="modal-title d-flex align-items-center gap-2">
              <span class="material-symbols-rounded text-primary">speed</span>
              Gestión de Límites: {{ account.accountNumber }}
            </h5>
            <button type="button" class="btn-close" (click)="close.emit()"></button>
          </div>
          <div class="modal-body p-4">
            <div *ngIf="loading" class="text-center py-4">
               <div class="spinner-border text-primary" role="status"></div>
               <p class="mt-2 text-muted">Cargando parámetros de riesgo...</p>
            </div>

            <div *ngIf="!loading && limits">
              <div class="alert alert-info border-0 shadow-sm mb-4" style="background: #eff6ff">
                <div class="d-flex gap-2">
                   <span class="material-symbols-rounded text-info">info</span>
                   <small class="text-info font-weight-600">
                     Los límites se reinician automáticamente cada 24 horas. 
                     Último reinicio: {{ limits.lastResetDate | date:'shortDate' }}
                   </small>
                </div>
              </div>

              <div class="limit-group mb-4">
                 <div class="d-flex justify-content-between mb-2">
                    <label class="form-label mb-0">Límite Diario ATM (Cajeros)</label>
                    <span class="badge badge-soft-primary">Máx. $2,000</span>
                 </div>
                 <div class="input-group">
                    <span class="input-group-text">$</span>
                    <input type="number" class="form-control" [(ngModel)]="limits.dailyAtmLimit">
                 </div>
                 <div class="progress mt-2" style="height: 6px;">
                    <div class="progress-bar bg-info" [style.width.%]="calcPercent(limits.dailyAtmAuculated, limits.dailyAtmLimit)"></div>
                 </div>
                 <small class="text-muted d-block mt-1">Consumido hoy: {{ (limits.dailyAtmAuculated || 0) | currency }}</small>
              </div>

              <div class="limit-group mb-4">
                 <div class="d-flex justify-content-between mb-2">
                    <label class="form-label mb-0">Límite Diario POS (Compras)</label>
                    <span class="badge badge-soft-primary">Máx. $10,000</span>
                 </div>
                 <div class="input-group">
                    <span class="input-group-text">$</span>
                    <input type="number" class="form-control" [(ngModel)]="limits.dailyPosLimit">
                 </div>
                 <div class="progress mt-2" style="height: 6px;">
                    <div class="progress-bar bg-success" [style.width.%]="calcPercent(limits.dailyPosAccumulated, limits.dailyPosLimit)"></div>
                 </div>
                 <small class="text-muted d-block mt-1">Consumido hoy: {{ (limits.dailyPosAccumulated || 0) | currency }}</small>
              </div>

              <div class="limit-group mb-0">
                 <div class="d-flex justify-content-between mb-2">
                    <label class="form-label mb-0">Límite Diario E-commerce</label>
                    <span class="badge badge-soft-primary">Máx. $5,000</span>
                 </div>
                 <div class="input-group">
                    <span class="input-group-text">$</span>
                    <input type="number" class="form-control" [(ngModel)]="limits.dailyEcommerceLimit">
                 </div>
                 <div class="progress mt-2" style="height: 6px;">
                    <div class="progress-bar bg-warning" [style.width.%]="calcPercent(limits.dailyEcommerceAccumulated, limits.dailyEcommerceLimit)"></div>
                 </div>
                 <small class="text-muted d-block mt-1">Consumido hoy: {{ (limits.dailyEcommerceAccumulated || 0) | currency }}</small>
              </div>
            </div>
          </div>
          <div class="modal-footer bg-light border-top">
            <button type="button" class="btn btn-secondary" (click)="close.emit()">Cancelar</button>
            <button type="button" class="btn btn-primary px-4 d-flex align-items-center gap-2" [disabled]="saving || loading" (click)="saveLimits()">
              <span *ngIf="saving" class="spinner-border spinner-border-sm"></span>
              {{ saving ? 'Guardando...' : 'Aplicar Cambios' }}
            </button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .modal-backdrop { background: rgba(0,0,0,0.5); backdrop-filter: blur(2px); }
    .modal-content { border-radius: 12px; }
    .badge-soft-primary { background: #e0e7ff; color: #4338ca; font-size: 10px; padding: 4px 8px; }
    .form-label { font-size: 0.85rem; font-weight: 600; color: #374151; }
    .font-weight-600 { font-weight: 600; }
  `]
})
export class AccountLimitsModalComponent implements OnInit {
  @Input({ required: true }) account!: Account;
  @Output() close = new EventEmitter<void>();
  @Output() saved = new EventEmitter<void>();

  private customerService = inject(CustomerService);
  private notifications = inject(NotificationService);
  
  limits: AccountLimit | null = null;
  loading = true;
  saving = false;

  ngOnInit() {
    this.loadLimits();
  }

  loadLimits() {
    this.customerService.getAccountLimits(this.account.id).subscribe({
      next: (res) => {
        this.limits = res;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      }
    });
  }

  calcPercent(accumulated: number | undefined, limit: number): number {
    if (!limit || limit === 0) return 0;
    const val = ((accumulated || 0) / limit) * 100;
    return Math.min(val, 100);
  }

  saveLimits() {
    if (!this.limits) return;
    this.saving = true;
    this.customerService.updateAccountLimits(this.account.id, this.limits).subscribe({
      next: () => {
        this.saving = false;
        this.notifications.success('Límites actualizados correctamente.');
        this.saved.emit();
        this.close.emit();
      },
      error: () => {
        this.saving = false;
        this.notifications.error('Error al actualizar límites.');
      }
    });
  }
}
