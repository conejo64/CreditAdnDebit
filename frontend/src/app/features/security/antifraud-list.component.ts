import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AntifraudService, AntifraudRule, AntifraudRuleType } from './antifraud.service';

@Component({
  selector: 'app-antifraud-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-container">
      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">policy</span>
            Motor de Reglas Antifraude (v60)
          </h1>
          <p class="text-muted mt-1">Configure las reglas de riesgo, bloqueos geográficos y scoring transaccional.</p>
        </div>
        <button class="btn btn-primary d-flex align-items-center gap-2" (click)="openCreator()">
          <span class="material-symbols-rounded">add</span> Nueva Regla
        </button>
      </div>

      <div class="card p-0">
        <div class="table-responsive">
          <table class="table">
            <thead>
              <tr>
                <th>ESTADO</th>
                <th>TIPO DE REGLA</th>
                <th>VALOR OBJETIVO (ISO/ID)</th>
                <th class="text-right">RISK SCORE</th>
                <th>DESCRIPCIÓN</th>
                <th class="text-right">ACCIONES</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let rule of rules">
                <td>
                   <span class="status-badge" [ngClass]="rule.isEnabled ? 'status-active' : 'status-disabled'">
                     {{ rule.isEnabled ? 'Activa' : 'Inactiva' }}
                   </span>
                </td>
                <td class="font-weight-600">{{ getRuleTypeName(rule.type) }}</td>
                <td><code class="text-primary">{{ rule.targetValue }}</code></td>
                <td class="text-right font-weight-600" [ngClass]="rule.riskScore >= 50 ? 'text-danger' : 'text-main'">
                    {{ rule.riskScore }}
                </td>
                <td class="text-muted">{{ rule.description }}</td>
                <td class="text-right">
                  <div class="d-flex justify-content-end gap-2">
                    <button class="btn btn-outline btn-sm py-1 px-2" (click)="toggleRule(rule)">
                      <span class="material-symbols-rounded" style="font-size: 18px">{{ rule.isEnabled ? 'pause' : 'play_arrow' }}</span>
                    </button>
                    <button class="btn btn-outline btn-sm py-1 px-2 text-danger" (click)="deleteRule(rule.id)">
                      <span class="material-symbols-rounded" style="font-size: 18px">delete</span>
                    </button>
                  </div>
                </td>
              </tr>
              <tr *ngIf="rules.length === 0">
                 <td colspan="6" class="text-center py-5 text-muted">
                    No existen reglas antifraude configuradas. El switch opera con validaciones básicas.
                 </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Simple Rule Editor Overlay (Simulator) -->
      <div class="rule-editor-overlay" *ngIf="isAdding">
         <div class="card p-4" style="max-width: 500px; width: 100%;">
            <h3 class="mb-4">Nueva Regla Antifraude</h3>
            <div class="mb-3">
               <label>Tipo de Regla</label>
               <select class="form-control" [(ngModel)]="newRule.type">
                  <option [value]="1">Bloqueo de País (ISO)</option>
                  <option [value]="2">Monitoreo con Score (País)</option>
                  <option [value]="3">Bloqueo de Comercio (ID)</option>
                  <option [value]="4">Multiplicador por Riesgo</option>
               </select>
            </div>
            <div class="mb-3">
               <label>Valor Objetivo (ISO o ID)</label>
               <input class="form-control" type="text" [(ngModel)]="newRule.targetValue" placeholder="Ejem: KP, CN, 12345678">
            </div>
            <div class="mb-3">
               <label>Impacto en Risk Score (0-100)</label>
               <input class="form-control" type="number" [(ngModel)]="newRule.riskScore">
               <small class="text-muted">Si el score total llega a 70+, la transacción se rechaza.</small>
            </div>
            <div class="mb-4">
               <label>Descripción / Motivo</label>
               <input class="form-control" type="text" [(ngModel)]="newRule.description">
            </div>
            <div class="d-flex justify-content-end gap-2">
               <button class="btn btn-outline" (click)="isAdding = false">Cancelar</button>
               <button class="btn btn-primary" (click)="saveRule()">Guardar Regla</button>
            </div>
         </div>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .status-badge { padding: 0.2rem 0.6rem; border-radius: 1rem; font-size: 0.75rem; font-weight: 600; }
    .status-active { background: #ecfdf5; color: #047857; }
    .status-disabled { background: #f1f5f9; color: #475569; }
    
    .table th, .table td { padding: 1rem 1.5rem; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
    .rule-editor-overlay {
       position: fixed; top: 0; left: 0; right: 0; bottom: 0;
       background: rgba(0,0,0,0.4); backdrop-filter: blur(4px);
       display: flex; align-items: center; justify-content: center; z-index: 1000;
       animation: fadeIn 0.2s ease-out;
    }
    @keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }
    label { font-size: 0.75rem; text-transform: uppercase; color: var(--text-muted); font-weight: 600; margin-bottom: 0.5rem; display: block; }
  `]
})
export class AntifraudListComponent implements OnInit {
  private riskService = inject(AntifraudService);

  rules: AntifraudRule[] = [];
  isAdding = false;
  newRule: Partial<AntifraudRule> = { type: 1, riskScore: 100, isEnabled: true, targetValue: '' };

  ngOnInit() {
    this.loadRules();
  }

  loadRules() {
    this.riskService.getRules().subscribe(data => this.rules = data);
  }

  getRuleTypeName(type: AntifraudRuleType) {
    const map = {
      1: 'País Bloqueado',
      2: 'Monitoreo de País',
      3: 'Comercio Bloqueado',
      4: 'Multiplicador Riesgo',
      5: 'Velocidad por Tarjeta'
    };
    return map[type] || 'Desconocida';
  }

  openCreator() {
    this.isAdding = true;
    this.newRule = { type: 1, riskScore: 100, isEnabled: true, targetValue: '', description: 'Regla preventiva v60' };
  }

  saveRule() {
    this.riskService.upsertRule(this.newRule).subscribe(() => {
      this.isAdding = false;
      this.loadRules();
    });
  }

  toggleRule(rule: AntifraudRule) {
    this.riskService.upsertRule({ ...rule, isEnabled: !rule.isEnabled }).subscribe(() => this.loadRules());
  }

  deleteRule(id: string) {
    if (confirm('¿Desea eliminar esta regla antifraude?')) {
      this.riskService.deleteRule(id).subscribe(() => this.loadRules());
    }
  }
}
