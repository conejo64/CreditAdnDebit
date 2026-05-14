import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CreditLimitService, CreditLimitProposal, CreditLimitEvaluation } from './credit-limit.service';
import { NotificationService } from '../../core/notification.service';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';

type Tab = 'proposals' | 'evaluate';

@Component({
  selector: 'app-credit-limit-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-container">
      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">tune</span>
            Gestión de Cupos de Crédito
          </h1>
          <p class="text-muted mt-1">Propuestas de ajuste de límite y evaluación de cuentas.</p>
        </div>
        <div class="d-flex gap-2">
          <select class="form-input" style="width: 160px" [(ngModel)]="statusFilter" (ngModelChange)="loadProposals()">
            <option value="">Todos los estados</option>
            <option value="Pending">Pendientes</option>
            <option value="Applied">Aplicados</option>
            <option value="Rejected">Rechazados</option>
          </select>
          <button class="btn btn-outline d-flex align-items-center gap-2" (click)="loadProposals()">
            <span class="material-symbols-rounded">refresh</span>
          </button>
        </div>
      </div>

      <!-- Tabs -->
      <div class="tab-bar mb-4">
        <button class="tab-btn" [class.active]="activeTab === 'proposals'" (click)="setTab('proposals')">
          <span class="material-symbols-rounded">list_alt</span> Propuestas de Cupo
        </button>
        <button class="tab-btn" [class.active]="activeTab === 'evaluate'" (click)="setTab('evaluate')">
          <span class="material-symbols-rounded">model_training</span> Evaluar Cuenta
        </button>
      </div>

      <!-- Proposals Tab -->
      <div *ngIf="activeTab === 'proposals'">
        <div class="card p-0">
          <div class="table-responsive">
            <table class="table">
              <thead>
                <tr>
                  <th>CUENTA</th>
                  <th class="text-right">LÍMITE ACTUAL</th>
                  <th class="text-right">LÍMITE PROPUESTO</th>
                  <th>VARIACIÓN</th>
                  <th>ESTADO</th>
                  <th>GENERADO</th>
                  <th class="text-center">ACCIÓN</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let proposal of proposals">
                  <td class="font-mono text-xs">{{ proposal.accountId }}</td>
                  <td class="text-right">{{ proposal.currentLimit | currency }}</td>
                  <td class="text-right font-weight-600">{{ proposal.proposedLimit | currency }}</td>
                  <td>
                    <span class="change-badge" [class.increase]="proposal.proposedLimit > proposal.currentLimit" [class.decrease]="proposal.proposedLimit < proposal.currentLimit">
                      <span class="material-symbols-rounded change-icon">
                        {{ proposal.proposedLimit > proposal.currentLimit ? 'arrow_upward' : 'arrow_downward' }}
                      </span>
                      {{ getDeltaPercent(proposal) | number:'1.1-1' }}%
                    </span>
                  </td>
                  <td>
                    <span class="status-badge" [ngClass]="getProposalStatusClass(proposal.status)">
                      {{ proposal.status }}
                    </span>
                  </td>
                  <td class="text-muted text-xs">{{ proposal.createdAt | date:'short' }}</td>
                  <td class="text-center">
                    <button
                      class="btn btn-sm btn-primary d-flex align-items-center gap-1 mx-auto"
                      *ngIf="proposal.status === 'Pending'"
                      (click)="applyProposal(proposal)"
                      [disabled]="applyingId === proposal.id"
                    >
                      <span class="material-symbols-rounded" style="font-size: 1rem">check_circle</span>
                      {{ applyingId === proposal.id ? 'Aplicando...' : 'Aplicar' }}
                    </button>
                    <span *ngIf="proposal.status !== 'Pending'" class="text-muted text-xs">
                      {{ proposal.appliedAt ? (proposal.appliedAt | date:'short') : '—' }}
                    </span>
                  </td>
                </tr>
                <tr *ngIf="proposals.length === 0 && !loading">
                  <td colspan="7" class="empty-state">
                    <span class="material-symbols-rounded" style="font-size: 48px; opacity: 0.4">tune</span>
                    <p class="mt-2">No hay propuestas de ajuste de cupo para el filtro seleccionado.</p>
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

      <!-- Evaluate Tab -->
      <div *ngIf="activeTab === 'evaluate'">
        <div class="eval-panel">
          <div class="card eval-form-card">
            <h3 class="mb-3">Evaluar Cuenta</h3>
            <p class="text-muted mb-4" style="font-size: 0.875rem">
              El motor de crédito analiza el comportamiento de pago, utilización y riesgo de la cuenta y genera una propuesta de ajuste de cupo.
            </p>
            <div class="form-group mb-4">
              <label class="form-label">ID de Cuenta (UUID)</label>
              <input
                class="form-input"
                [(ngModel)]="evaluateAccountId"
                placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                [disabled]="evaluating"
              />
            </div>
            <button
              class="btn btn-primary d-flex align-items-center gap-2 w-full"
              (click)="runEvaluation()"
              [disabled]="!evaluateAccountId || evaluating"
            >
              <span class="material-symbols-rounded">model_training</span>
              {{ evaluating ? 'Evaluando...' : 'Ejecutar Evaluación' }}
            </button>
          </div>

          <!-- Evaluation Result -->
          <div class="card eval-result-card" *ngIf="evaluation">
            <div class="d-flex align-items-center gap-2 mb-4">
              <span class="material-symbols-rounded text-primary" style="font-size: 1.5rem">insights</span>
              <h3 class="m-0">Resultado de Evaluación</h3>
            </div>

            <div class="eval-score-row mb-4">
              <div class="score-circle" [class.high]="evaluation.score >= 70" [class.mid]="evaluation.score >= 40 && evaluation.score < 70" [class.low]="evaluation.score < 40">
                <span class="score-value">{{ evaluation.score }}</span>
                <span class="score-label">Score</span>
              </div>
              <div class="score-info">
                <div class="eval-row">
                  <span class="eval-key">Límite actual</span>
                  <span class="eval-val">{{ evaluation.currentLimit | currency }}</span>
                </div>
                <div class="eval-row">
                  <span class="eval-key">Límite recomendado</span>
                  <span class="eval-val font-weight-600 text-primary">{{ evaluation.recommendedLimit | currency }}</span>
                </div>
                <div class="eval-row" *ngIf="evaluation.proposalId">
                  <span class="eval-key">Propuesta generada</span>
                  <span class="eval-val font-mono text-xs">{{ evaluation.proposalId }}</span>
                </div>
              </div>
            </div>

            <div class="rationale-box">
              <p class="rationale-label">Justificación del Motor</p>
              <p class="rationale-text">{{ evaluation.rationale }}</p>
            </div>

            <button class="btn btn-outline mt-3 w-full" (click)="evaluation = null; setTab('proposals'); loadProposals()">
              Ver en Propuestas
            </button>
          </div>

          <div class="card eval-result-card placeholder-card" *ngIf="!evaluation && !evaluating">
            <div class="empty-state">
              <span class="material-symbols-rounded" style="font-size: 64px; opacity: 0.2">model_training</span>
              <p class="mt-2">Ingresá el ID de cuenta y ejecutá la evaluación para ver los resultados aquí.</p>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .font-weight-600 { font-weight: 600; }
    .font-mono { font-family: 'Roboto Mono', monospace; }
    .text-xs { font-size: 0.75rem; }
    .text-muted { color: var(--text-muted); }
    .text-primary { color: var(--primary); }
    .mx-auto { margin-left: auto; margin-right: auto; }
    .w-full { width: 100%; }

    .tab-bar { display: flex; gap: 0.5rem; border-bottom: 2px solid var(--border-color); }
    .tab-btn { display: flex; align-items: center; gap: 0.5rem; padding: 0.75rem 1.25rem; border: none; background: none; color: var(--text-muted); font-size: 0.875rem; font-weight: 500; cursor: pointer; border-bottom: 2px solid transparent; margin-bottom: -2px; transition: all 0.2s; }
    .tab-btn.active { color: var(--primary); border-bottom-color: var(--primary); }
    .tab-btn:hover:not(.active) { color: var(--text-main); }

    .table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
    .table th, .table td { padding: 1rem 1.5rem; text-align: left; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
    .table th { color: var(--text-muted); font-weight: 600; font-size: 0.75rem; text-transform: uppercase; }
    .text-right { text-align: right; }
    .text-center { text-align: center; }
    .empty-state { text-align: center; padding: 3rem; color: var(--text-muted); }

    .change-badge { display: inline-flex; align-items: center; gap: 0.25rem; padding: 0.2rem 0.5rem; border-radius: 4px; font-size: 0.75rem; font-weight: 700; }
    .change-badge.increase { background: #d1fae5; color: #047857; }
    .change-badge.decrease { background: #fee2e2; color: #b91c1c; }
    .change-icon { font-size: 0.9rem; }

    .status-badge { padding: 0.25rem 0.6rem; border-radius: 4px; font-size: 0.7rem; font-weight: 700; text-transform: uppercase; }
    .status-pending { background: #fef3c7; color: #92400e; }
    .status-applied { background: #d1fae5; color: #047857; }
    .status-rejected { background: #fee2e2; color: #b91c1c; }

    .btn-sm { padding: 0.4rem 0.75rem; font-size: 0.8rem; }

    @keyframes spin { to { transform: rotate(360deg); } }
    .spin { animation: spin 1s linear infinite; font-size: 2rem; }

    /* Evaluation panel */
    .eval-panel { display: grid; grid-template-columns: 380px 1fr; gap: 1.5rem; align-items: start; }
    @media (max-width: 900px) { .eval-panel { grid-template-columns: 1fr; } }

    .eval-form-card, .eval-result-card { padding: 1.75rem; }
    .placeholder-card { min-height: 300px; display: flex; align-items: center; justify-content: center; }

    .eval-score-row { display: flex; align-items: center; gap: 2rem; }
    .score-circle { width: 96px; height: 96px; border-radius: 50%; display: flex; flex-direction: column; align-items: center; justify-content: center; border: 4px solid; flex-shrink: 0; }
    .score-circle.high { border-color: #10b981; background: #d1fae5; }
    .score-circle.mid { border-color: #f59e0b; background: #fef3c7; }
    .score-circle.low { border-color: #ef4444; background: #fee2e2; }
    .score-value { font-size: 1.75rem; font-weight: 800; line-height: 1; }
    .score-label { font-size: 0.65rem; text-transform: uppercase; font-weight: 600; color: var(--text-muted); }

    .score-info { flex: 1; }
    .eval-row { display: flex; justify-content: space-between; padding: 0.4rem 0; border-bottom: 1px solid var(--border-color); font-size: 0.875rem; }
    .eval-key { color: var(--text-muted); }
    .eval-val { font-weight: 500; }

    .rationale-box { background: var(--bg-card-alt, #f9fafb); border-left: 3px solid var(--primary); border-radius: 4px; padding: 1rem; }
    .rationale-label { font-size: 0.7rem; text-transform: uppercase; font-weight: 700; color: var(--text-muted); margin: 0 0 0.4rem; }
    .rationale-text { font-size: 0.875rem; color: var(--text-main); margin: 0; line-height: 1.6; }

    .form-group { display: flex; flex-direction: column; gap: 0.4rem; }
    .form-label { font-size: 0.8rem; font-weight: 600; color: var(--text-muted); }
    .form-input { padding: 0.625rem 0.875rem; border: 1px solid var(--border-color); border-radius: var(--radius-sm); font-size: 0.875rem; background: white; width: 100%; transition: border-color 0.2s; }
    .form-input:focus { outline: none; border-color: var(--primary); }
    .form-input:disabled { background: var(--bg-card-alt, #f3f4f6); cursor: not-allowed; }
  `]
})
export class CreditLimitListComponent implements OnInit {
  private creditLimitService = inject(CreditLimitService);
  private notifications = inject(NotificationService);

  activeTab: Tab = 'proposals';
  proposals: CreditLimitProposal[] = [];
  evaluation: CreditLimitEvaluation | null = null;
  loading = false;
  evaluating = false;
  applyingId: string | null = null;
  statusFilter = 'Pending';
  evaluateAccountId = '';

  ngOnInit() {
    this.loadProposals();
  }

  setTab(tab: Tab) {
    this.activeTab = tab;
  }

  loadProposals() {
    this.loading = true;
    this.creditLimitService.getProposals(this.statusFilter || undefined, 50).pipe(
      catchError(() => {
        this.notifications.error('No se pudieron cargar las propuestas de cupo.');
        return of([] as CreditLimitProposal[]);
      })
    ).subscribe(data => { this.proposals = data; this.loading = false; });
  }

  applyProposal(proposal: CreditLimitProposal) {
    if (!confirm(`¿Aplicar el ajuste de ${proposal.currentLimit | 0} → ${proposal.proposedLimit | 0} para esta cuenta?`)) return;
    this.applyingId = proposal.id;
    this.creditLimitService.applyProposal(proposal.id).subscribe({
      next: updated => {
        const idx = this.proposals.findIndex(p => p.id === proposal.id);
        if (idx >= 0) this.proposals[idx] = updated;
        this.applyingId = null;
        this.notifications.success('Propuesta aplicada correctamente.');
      },
      error: () => {
        this.applyingId = null;
        this.notifications.error('Error al aplicar la propuesta. Verificá los permisos y que el backend esté disponible.');
      }
    });
  }

  runEvaluation() {
    if (!this.evaluateAccountId.trim()) return;
    this.evaluating = true;
    this.evaluation = null;
    this.creditLimitService.evaluate(this.evaluateAccountId.trim()).subscribe({
      next: result => { this.evaluation = result; this.evaluating = false; },
      error: () => {
        this.evaluating = false;
        this.notifications.error('Error al evaluar la cuenta. Verificá el ID y que el backend esté disponible.');
      }
    });
  }

  getDeltaPercent(proposal: CreditLimitProposal): number {
    if (!proposal.currentLimit) return 0;
    return Math.abs((proposal.proposedLimit - proposal.currentLimit) / proposal.currentLimit * 100);
  }

  getProposalStatusClass(status: string): string {
    switch (status) {
      case 'Pending': return 'status-pending';
      case 'Applied': return 'status-applied';
      case 'Rejected': return 'status-rejected';
      default: return '';
    }
  }
}
