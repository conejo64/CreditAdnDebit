import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AccountingService, JournalEntry, LedgerAccount, AccountingMapping } from './accounting.service';
import { NotificationService } from '../../core/notification.service';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';

type Tab = 'journal' | 'accounts' | 'mappings';

@Component({
  selector: 'app-accounting-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-container">
      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">calculate</span>
            Integración Contable
          </h1>
          <p class="text-muted mt-1">Diario contable, cuentas del plan de cuentas y mapeos de eventos.</p>
        </div>
        <div class="d-flex gap-2 align-items-center">
          <input type="number" class="form-input" style="width: 80px" [(ngModel)]="takeCount" min="10" max="200" placeholder="Filas" />
          <button class="btn btn-outline d-flex align-items-center gap-2" (click)="reload()">
            <span class="material-symbols-rounded">refresh</span>
            Recargar
          </button>
        </div>
      </div>

      <!-- Tabs -->
      <div class="tab-bar mb-4">
        <button class="tab-btn" [class.active]="activeTab === 'journal'" (click)="setTab('journal')">
          <span class="material-symbols-rounded">book_2</span> Diario de Asientos
        </button>
        <button class="tab-btn" [class.active]="activeTab === 'accounts'" (click)="setTab('accounts')">
          <span class="material-symbols-rounded">account_tree</span> Plan de Cuentas
        </button>
        <button class="tab-btn" [class.active]="activeTab === 'mappings'" (click)="setTab('mappings')">
          <span class="material-symbols-rounded">swap_horiz</span> Mapeos de Eventos
        </button>
      </div>

      <!-- Journal Entries Tab -->
      <div *ngIf="activeTab === 'journal'">
        <div class="card p-0">
          <div class="table-responsive">
            <table class="table">
              <thead>
                <tr>
                  <th>FECHA NEGOCIO</th>
                  <th>MÓDULO ORIGEN</th>
                  <th>REFERENCIA</th>
                  <th>DESCRIPCIÓN</th>
                  <th>REGISTRADO</th>
                  <th class="text-center">DETALLE</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let entry of journalEntries">
                  <td class="font-weight-600">{{ entry.businessDate | date:'mediumDate' }}</td>
                  <td>
                    <span class="module-badge">{{ entry.sourceModule }}</span>
                  </td>
                  <td class="font-mono text-xs">{{ entry.sourceReference }}</td>
                  <td class="text-muted">{{ entry.description }}</td>
                  <td class="text-muted text-xs">{{ entry.postedAt | date:'short' }}</td>
                  <td class="text-center">
                    <button class="btn btn-icon" (click)="openEntry(entry)" title="Ver asiento">
                      <span class="material-symbols-rounded">visibility</span>
                    </button>
                  </td>
                </tr>
                <tr *ngIf="journalEntries.length === 0 && !loading">
                  <td colspan="6" class="empty-state">
                    <span class="material-symbols-rounded" style="font-size: 48px; opacity: 0.4">calculate</span>
                    <p class="mt-2">No hay asientos contables registrados.</p>
                  </td>
                </tr>
                <tr *ngIf="loading">
                  <td colspan="6" class="empty-state">
                    <span class="material-symbols-rounded spin">sync</span>
                    <p class="mt-2 text-muted">Cargando...</p>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <!-- Ledger Accounts Tab -->
      <div *ngIf="activeTab === 'accounts'">
        <div class="d-flex justify-content-end mb-3">
          <button class="btn btn-primary d-flex align-items-center gap-2" (click)="openAccountForm()">
            <span class="material-symbols-rounded">add</span> Nueva Cuenta
          </button>
        </div>
        <div class="card p-0">
          <div class="table-responsive">
            <table class="table">
              <thead>
                <tr>
                  <th>CÓDIGO</th>
                  <th>NOMBRE</th>
                  <th>TIPO</th>
                  <th>ESTADO</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let account of ledgerAccounts">
                  <td class="font-mono font-weight-600">{{ account.code }}</td>
                  <td>{{ account.name }}</td>
                  <td><span class="type-badge">{{ account.type }}</span></td>
                  <td>
                    <span class="status-dot" [class.active]="account.isActive" [class.inactive]="!account.isActive">
                      {{ account.isActive ? 'Activa' : 'Inactiva' }}
                    </span>
                  </td>
                </tr>
                <tr *ngIf="ledgerAccounts.length === 0 && !loading">
                  <td colspan="4" class="empty-state">
                    <span class="material-symbols-rounded" style="font-size: 48px; opacity: 0.4">account_tree</span>
                    <p class="mt-2">No hay cuentas contables configuradas.</p>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <!-- Mappings Tab -->
      <div *ngIf="activeTab === 'mappings'">
        <div class="d-flex justify-content-end mb-3">
          <button class="btn btn-primary d-flex align-items-center gap-2" (click)="openMappingForm()">
            <span class="material-symbols-rounded">add</span> Nuevo Mapeo
          </button>
        </div>
        <div class="card p-0">
          <div class="table-responsive">
            <table class="table">
              <thead>
                <tr>
                  <th>TIPO DE EVENTO</th>
                  <th>CUENTA DÉBITO</th>
                  <th>CUENTA CRÉDITO</th>
                  <th>ESTADO</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let mapping of mappings">
                  <td>
                    <span class="event-tag">{{ mapping.eventType }}</span>
                  </td>
                  <td class="font-mono">{{ mapping.debitAccountCode }}</td>
                  <td class="font-mono">{{ mapping.creditAccountCode }}</td>
                  <td>
                    <span class="status-dot" [class.active]="mapping.isActive" [class.inactive]="!mapping.isActive">
                      {{ mapping.isActive ? 'Activo' : 'Inactivo' }}
                    </span>
                  </td>
                </tr>
                <tr *ngIf="mappings.length === 0 && !loading">
                  <td colspan="4" class="empty-state">
                    <span class="material-symbols-rounded" style="font-size: 48px; opacity: 0.4">swap_horiz</span>
                    <p class="mt-2">No hay mapeos de eventos configurados.</p>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>

    <!-- Journal Entry Detail Modal -->
    <div class="modal-backdrop" *ngIf="selectedEntry">
      <div class="modal-card" style="max-width: 700px; width: 90%">
        <div class="modal-header d-flex justify-content-between align-items-center">
          <div>
            <h3 class="m-0">Asiento Contable</h3>
            <small class="text-muted">{{ selectedEntry.businessDate | date:'fullDate' }} · {{ selectedEntry.sourceModule }}</small>
          </div>
          <button class="btn-close" (click)="selectedEntry = null">×</button>
        </div>
        <div class="modal-body">
          <div class="info-row mb-3">
            <span class="label">Referencia:</span>
            <span class="font-mono">{{ selectedEntry.sourceReference }}</span>
          </div>
          <div class="info-row mb-3">
            <span class="label">Descripción:</span>
            <span>{{ selectedEntry.description }}</span>
          </div>
          <table class="table table-sm mt-3">
            <thead>
              <tr>
                <th>CUENTA</th>
                <th class="text-right">DÉBITO</th>
                <th class="text-right">CRÉDITO</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let line of selectedEntry.lines">
                <td>
                  <span class="font-mono text-xs">{{ line.accountCode }}</span>
                  <span class="text-muted ms-2">{{ line.accountName }}</span>
                </td>
                <td class="text-right">
                  <span *ngIf="line.debit > 0" class="text-danger font-weight-600">{{ line.debit | currency }}</span>
                  <span *ngIf="line.debit === 0" class="text-muted">—</span>
                </td>
                <td class="text-right">
                  <span *ngIf="line.credit > 0" class="text-success font-weight-600">{{ line.credit | currency }}</span>
                  <span *ngIf="line.credit === 0" class="text-muted">—</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <div class="modal-footer d-flex justify-content-end p-3">
          <button class="btn btn-outline" (click)="selectedEntry = null">Cerrar</button>
        </div>
      </div>
    </div>

    <!-- Account Form Modal -->
    <div class="modal-backdrop" *ngIf="showAccountForm">
      <div class="modal-card" style="max-width: 480px; width: 90%">
        <div class="modal-header d-flex justify-content-between">
          <h3 class="m-0">Nueva Cuenta Contable</h3>
          <button class="btn-close" (click)="showAccountForm = false">×</button>
        </div>
        <div class="modal-body">
          <div class="form-group mb-3">
            <label class="form-label">Código</label>
            <input class="form-input" [(ngModel)]="accountForm.code" placeholder="Ej: 1101" />
          </div>
          <div class="form-group mb-3">
            <label class="form-label">Nombre</label>
            <input class="form-input" [(ngModel)]="accountForm.name" placeholder="Ej: Caja y Bancos" />
          </div>
          <div class="form-group mb-3">
            <label class="form-label">Tipo</label>
            <select class="form-input" [(ngModel)]="accountForm.type">
              <option value="Asset">Activo</option>
              <option value="Liability">Pasivo</option>
              <option value="Equity">Patrimonio</option>
              <option value="Revenue">Ingreso</option>
              <option value="Expense">Gasto</option>
            </select>
          </div>
        </div>
        <div class="modal-footer d-flex justify-content-end gap-2 p-3">
          <button class="btn btn-outline" (click)="showAccountForm = false">Cancelar</button>
          <button class="btn btn-primary" (click)="saveAccount()" [disabled]="saving">
            {{ saving ? 'Guardando...' : 'Guardar' }}
          </button>
        </div>
      </div>
    </div>

    <!-- Mapping Form Modal -->
    <div class="modal-backdrop" *ngIf="showMappingForm">
      <div class="modal-card" style="max-width: 480px; width: 90%">
        <div class="modal-header d-flex justify-content-between">
          <h3 class="m-0">Nuevo Mapeo de Evento</h3>
          <button class="btn-close" (click)="showMappingForm = false">×</button>
        </div>
        <div class="modal-body">
          <div class="form-group mb-3">
            <label class="form-label">Tipo de Evento</label>
            <select class="form-input" [(ngModel)]="mappingForm.eventType">
              <option value="Purchase">Compra (Purchase)</option>
              <option value="Payment">Pago (Payment)</option>
              <option value="Fee">Comisión (Fee)</option>
              <option value="Interest">Interés (Interest)</option>
              <option value="Refund">Reembolso (Refund)</option>
              <option value="Reversal">Reverso (Reversal)</option>
              <option value="Chargeback">Contracargo (Chargeback)</option>
              <option value="Settlement">Liquidación (Settlement)</option>
            </select>
          </div>
          <div class="form-group mb-3">
            <label class="form-label">Cuenta Débito</label>
            <select class="form-input" [(ngModel)]="mappingForm.debitAccountCode">
              <option *ngFor="let acc of ledgerAccounts" [value]="acc.code">{{ acc.code }} — {{ acc.name }}</option>
            </select>
          </div>
          <div class="form-group mb-3">
            <label class="form-label">Cuenta Crédito</label>
            <select class="form-input" [(ngModel)]="mappingForm.creditAccountCode">
              <option *ngFor="let acc of ledgerAccounts" [value]="acc.code">{{ acc.code }} — {{ acc.name }}</option>
            </select>
          </div>
        </div>
        <div class="modal-footer d-flex justify-content-end gap-2 p-3">
          <button class="btn btn-outline" (click)="showMappingForm = false">Cancelar</button>
          <button class="btn btn-primary" (click)="saveMapping()" [disabled]="saving">
            {{ saving ? 'Guardando...' : 'Guardar' }}
          </button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .font-weight-600 { font-weight: 600; }
    .font-mono { font-family: 'Roboto Mono', monospace; font-size: 0.8rem; }
    .text-xs { font-size: 0.75rem; }
    .ms-2 { margin-left: 0.5rem; }

    .tab-bar { display: flex; gap: 0.5rem; border-bottom: 2px solid var(--border-color); }
    .tab-btn {
      display: flex; align-items: center; gap: 0.5rem;
      padding: 0.75rem 1.25rem; border: none; background: none;
      color: var(--text-muted); font-size: 0.875rem; font-weight: 500;
      cursor: pointer; border-bottom: 2px solid transparent; margin-bottom: -2px;
      transition: all 0.2s;
    }
    .tab-btn.active { color: var(--primary); border-bottom-color: var(--primary); }
    .tab-btn:hover:not(.active) { color: var(--text-main); }

    .table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
    .table th, .table td { padding: 1rem 1.5rem; text-align: left; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
    .table th { color: var(--text-muted); font-weight: 600; font-size: 0.75rem; text-transform: uppercase; }
    .table-sm th, .table-sm td { padding: 0.75rem 1rem; }
    .text-right { text-align: right; }

    .empty-state { text-align: center; padding: 3rem; color: var(--text-muted); }

    .module-badge { background: #e0e7ff; color: #4338ca; padding: 0.2rem 0.6rem; border-radius: 4px; font-size: 0.75rem; font-weight: 600; }
    .type-badge { background: var(--bg-card-alt, #f3f4f6); color: var(--text-muted); padding: 0.2rem 0.6rem; border-radius: 4px; font-size: 0.75rem; }
    .event-tag { background: #fef3c7; color: #92400e; padding: 0.2rem 0.6rem; border-radius: 4px; font-size: 0.75rem; font-weight: 600; }

    .status-dot { display: inline-flex; align-items: center; gap: 0.4rem; font-size: 0.8rem; font-weight: 600; }
    .status-dot.active { color: #047857; }
    .status-dot.inactive { color: #9ca3af; }
    .status-dot.active::before { content: ''; width: 8px; height: 8px; border-radius: 50%; background: #10b981; }
    .status-dot.inactive::before { content: ''; width: 8px; height: 8px; border-radius: 50%; background: #9ca3af; }

    .modal-backdrop { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.5); display: flex; align-items: center; justify-content: center; z-index: 1000; }
    .modal-card { background: white; border-radius: 12px; box-shadow: 0 20px 40px rgba(0,0,0,0.2); overflow: hidden; }
    .modal-header { padding: 1.25rem 1.5rem; border-bottom: 1px solid var(--border-color); }
    .modal-body { padding: 1.5rem; }
    .btn-close { border: none; background: none; font-size: 1.5rem; cursor: pointer; color: var(--text-muted); line-height: 1; }

    .info-row { display: flex; gap: 1rem; align-items: center; }
    .info-row .label { font-size: 0.8rem; font-weight: 600; color: var(--text-muted); text-transform: uppercase; min-width: 100px; }

    .form-group { display: flex; flex-direction: column; gap: 0.4rem; }
    .form-label { font-size: 0.8rem; font-weight: 600; color: var(--text-muted); }
    .form-input { padding: 0.625rem 0.875rem; border: 1px solid var(--border-color); border-radius: var(--radius-sm); font-size: 0.875rem; width: 100%; background: white; transition: border-color 0.2s; }
    .form-input:focus { outline: none; border-color: var(--primary); }

    @keyframes spin { to { transform: rotate(360deg); } }
    .spin { animation: spin 1s linear infinite; font-size: 2rem; }
  `]
})
export class AccountingListComponent implements OnInit {
  private accountingService = inject(AccountingService);
  private notifications = inject(NotificationService);

  activeTab: Tab = 'journal';
  journalEntries: JournalEntry[] = [];
  ledgerAccounts: LedgerAccount[] = [];
  mappings: AccountingMapping[] = [];
  selectedEntry: JournalEntry | null = null;
  loading = false;
  saving = false;
  takeCount = 50;

  showAccountForm = false;
  accountForm = { code: '', name: '', type: 'Asset' };

  showMappingForm = false;
  mappingForm = { eventType: 'Purchase', debitAccountCode: '', creditAccountCode: '' };

  ngOnInit() {
    this.loadJournal();
    this.loadAccounts();
    this.loadMappings();
  }

  setTab(tab: Tab) {
    this.activeTab = tab;
  }

  reload() {
    this.loadJournal();
    this.loadAccounts();
    this.loadMappings();
  }

  loadJournal() {
    this.loading = true;
    this.accountingService.getJournalEntries(this.takeCount).pipe(
      catchError(() => {
        this.notifications.error('No se pudieron cargar los asientos contables.');
        return of([] as JournalEntry[]);
      })
    ).subscribe(data => { this.journalEntries = data; this.loading = false; });
  }

  loadAccounts() {
    this.accountingService.getLedgerAccounts().pipe(
      catchError(() => {
        this.notifications.error('No se pudieron cargar las cuentas del libro mayor.');
        return of([] as LedgerAccount[]);
      })
    ).subscribe(data => { this.ledgerAccounts = data; });
  }

  loadMappings() {
    this.accountingService.getMappings().pipe(
      catchError(() => {
        this.notifications.error('No se pudieron cargar los mapeos contables.');
        return of([] as AccountingMapping[]);
      })
    ).subscribe(data => { this.mappings = data; });
  }

  openEntry(entry: JournalEntry) {
    // Load full detail if lines are not embedded
    if (entry.lines?.length) {
      this.selectedEntry = entry;
    } else {
      this.accountingService.getJournalEntry(entry.id).pipe(
        catchError(() => of(entry))
      ).subscribe(full => { this.selectedEntry = full; });
    }
  }

  openAccountForm() {
    this.accountForm = { code: '', name: '', type: 'Asset' };
    this.showAccountForm = true;
  }

  saveAccount() {
    if (!this.accountForm.code || !this.accountForm.name) return;
    this.saving = true;
    this.accountingService.upsertLedgerAccount(this.accountForm).subscribe({
      next: () => {
        this.saving = false;
        this.showAccountForm = false;
        this.loadAccounts();
        this.notifications.success('Cuenta guardada correctamente.');
      },
      error: () => {
        this.saving = false;
        this.notifications.error('Error al guardar la cuenta.');
      }
    });
  }

  openMappingForm() {
    this.mappingForm = { eventType: 'Purchase', debitAccountCode: this.ledgerAccounts[0]?.code ?? '', creditAccountCode: this.ledgerAccounts[1]?.code ?? '' };
    this.showMappingForm = true;
  }

  saveMapping() {
    this.saving = true;
    this.accountingService.upsertMapping(this.mappingForm).subscribe({
      next: () => {
        this.saving = false;
        this.showMappingForm = false;
        this.loadMappings();
        this.notifications.success('Mapeo guardado correctamente.');
      },
      error: () => {
        this.saving = false;
        this.notifications.error('Error al guardar el mapeo.');
      }
    });
  }
}
