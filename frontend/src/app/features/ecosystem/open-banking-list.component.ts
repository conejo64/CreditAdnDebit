import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { OpenBankingService, OpenBankingClient, OpenBankingBalance, OpenBankingTransaction } from './open-banking.service';
import { NotificationService } from '../../core/notification.service';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';

type Tab = 'clients' | 'monitor';

@Component({
  selector: 'app-open-banking-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-container">
      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">api</span>
            Open Banking
          </h1>
          <p class="text-muted mt-1">Gestión de clientes OAuth y monitoreo de acceso a cuentas autorizadas.</p>
        </div>
      </div>

      <div class="tab-bar mb-4">
        <button class="tab-btn" [class.active]="activeTab === 'clients'" (click)="setTab('clients')">
          <span class="material-symbols-rounded">apps</span> Clientes Registrados
        </button>
        <button class="tab-btn" [class.active]="activeTab === 'monitor'" (click)="setTab('monitor')">
          <span class="material-symbols-rounded">monitor</span> Monitor de Acceso
        </button>
      </div>

      <!-- Clients Tab -->
      <div *ngIf="activeTab === 'clients'">
        <div class="d-flex justify-content-end mb-3">
          <button class="btn btn-primary d-flex align-items-center gap-2" (click)="openClientForm()">
            <span class="material-symbols-rounded">add</span> Nuevo Cliente
          </button>
        </div>

        <div class="clients-grid" *ngIf="clients.length">
          <div class="client-card" *ngFor="let client of clients">
            <div class="client-header">
              <div class="client-avatar">{{ client.name[0] | uppercase }}</div>
              <div class="client-title">
                <h4 class="client-name">{{ client.name }}</h4>
                <span class="client-id font-mono text-xs">{{ client.clientId }}</span>
              </div>
              <span class="active-dot" [class.active]="client.isActive" [title]="client.isActive ? 'Activo' : 'Inactivo'"></span>
            </div>
            <div class="scopes-row">
              <span class="scope-badge" *ngFor="let scope of client.scopes">{{ scope }}</span>
            </div>
            <div class="client-meta">
              <div class="meta-item">
                <span class="material-symbols-rounded">link</span>
                <span class="text-xs text-muted">{{ client.redirectUri }}</span>
              </div>
              <div class="meta-item">
                <span class="material-symbols-rounded">account_balance_wallet</span>
                <span class="text-xs text-muted">{{ client.allowedAccountIds?.length || 0 }} cuentas autorizadas</span>
              </div>
            </div>
            <button class="btn btn-outline btn-sm w-full mt-3" (click)="openGrantForm(client)">
              <span class="material-symbols-rounded" style="font-size: 1rem">add_link</span>
              Otorgar acceso a cuenta
            </button>
          </div>
        </div>

        <div class="card mt-2" *ngIf="clients.length === 0 && !loading">
          <div class="empty-state">
            <span class="material-symbols-rounded" style="font-size: 48px; opacity: 0.3">api</span>
            <p class="mt-2">No hay clientes OAuth registrados en Open Banking.</p>
          </div>
        </div>
      </div>

      <!-- Monitor Tab -->
      <div *ngIf="activeTab === 'monitor'">
        <div class="monitor-panel">
          <div class="card monitor-form">
            <h3 class="mb-3">Consultar Cuenta vía API</h3>
            <p class="text-muted mb-4" style="font-size: 0.875rem">
              Vista del operador sobre los datos que los clientes Open Banking pueden acceder de una cuenta autorizada.
            </p>
            <div class="form-group mb-4">
              <label class="form-label">ID de Cuenta</label>
              <input class="form-input" [(ngModel)]="monitorAccountId" placeholder="UUID de la cuenta" />
            </div>
            <button class="btn btn-primary w-full" (click)="monitorAccount()" [disabled]="!monitorAccountId || monitoring">
              {{ monitoring ? 'Consultando...' : 'Consultar' }}
            </button>
          </div>

          <div class="monitor-results" *ngIf="balance || transactions.length">
            <div class="balance-card mb-4" *ngIf="balance">
              <div class="d-flex align-items-center gap-3">
                <div class="bal-icon"><span class="material-symbols-rounded">account_balance</span></div>
                <div>
                  <div class="bal-label">Saldo disponible</div>
                  <div class="bal-amount">{{ balance.availableBalance | currency:balance.currency }}</div>
                  <div class="bal-current text-muted text-xs">Saldo actual: {{ balance.currentBalance | currency:balance.currency }} · Al: {{ balance.asOf | date:'short' }}</div>
                </div>
              </div>
            </div>

            <div class="card p-0" *ngIf="transactions.length">
              <div class="table-responsive">
                <table class="table table-sm">
                  <thead>
                    <tr>
                      <th>DESCRIPCIÓN</th>
                      <th>TIPO</th>
                      <th class="text-right">MONTO</th>
                      <th>FECHA</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr *ngFor="let tx of transactions">
                      <td>{{ tx.description }}</td>
                      <td><span class="tx-type-badge">{{ tx.type }}</span></td>
                      <td class="text-right font-weight-600" [class.text-success]="tx.amount > 0" [class.text-danger]="tx.amount < 0">
                        {{ tx.amount > 0 ? '+' : '' }}{{ tx.amount | currency:tx.currency }}
                      </td>
                      <td class="text-muted text-xs">{{ tx.postedAt | date:'short' }}</td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>
          </div>

          <div class="card monitor-placeholder" *ngIf="!balance && !transactions.length && !monitoring">
            <div class="empty-state">
              <span class="material-symbols-rounded" style="font-size: 64px; opacity: 0.2">monitor</span>
              <p class="mt-2">Ingresá el ID de cuenta para simular el acceso Open Banking.</p>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Create Client Modal -->
    <div class="modal-backdrop" *ngIf="showClientForm">
      <div class="modal-card" style="max-width: 480px; width: 90%">
        <div class="modal-header d-flex justify-content-between">
          <h3 class="m-0">Nuevo Cliente Open Banking</h3>
          <button class="btn-close" (click)="showClientForm = false">×</button>
        </div>
        <div class="modal-body">
          <div class="form-group mb-3">
            <label class="form-label">Nombre de la aplicación</label>
            <input class="form-input" [(ngModel)]="clientForm.name" placeholder="Ej: FinTech App S.A." />
          </div>
          <div class="form-group mb-3">
            <label class="form-label">Redirect URI</label>
            <input class="form-input" [(ngModel)]="clientForm.redirectUri" placeholder="https://..." />
          </div>
          <div class="form-group mb-3">
            <label class="form-label">Scopes (separados por coma)</label>
            <input class="form-input" [(ngModel)]="clientForm.scopesRaw" placeholder="balance,transactions" />
            <small class="text-muted">Valores válidos: balance, transactions, identity</small>
          </div>
        </div>
        <div class="modal-footer d-flex justify-content-end gap-2 p-3">
          <button class="btn btn-outline" (click)="showClientForm = false">Cancelar</button>
          <button class="btn btn-primary" (click)="saveClient()" [disabled]="saving">{{ saving ? 'Guardando...' : 'Crear' }}</button>
        </div>
      </div>
    </div>

    <!-- Grant Account Access Modal -->
    <div class="modal-backdrop" *ngIf="showGrantForm && selectedClient">
      <div class="modal-card" style="max-width: 420px; width: 90%">
        <div class="modal-header d-flex justify-content-between">
          <h3 class="m-0">Otorgar Acceso</h3>
          <button class="btn-close" (click)="showGrantForm = false">×</button>
        </div>
        <div class="modal-body">
          <p class="text-muted mb-3" style="font-size: 0.875rem">Otorgar a <strong>{{ selectedClient.name }}</strong> acceso a la siguiente cuenta:</p>
          <div class="form-group mb-3">
            <label class="form-label">ID de Cuenta</label>
            <input class="form-input" [(ngModel)]="grantAccountId" placeholder="UUID de la cuenta" />
          </div>
        </div>
        <div class="modal-footer d-flex justify-content-end gap-2 p-3">
          <button class="btn btn-outline" (click)="showGrantForm = false">Cancelar</button>
          <button class="btn btn-primary" (click)="grantAccess()" [disabled]="saving">{{ saving ? 'Otorgando...' : 'Otorgar' }}</button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .text-muted { color: var(--text-muted); }
    .font-mono { font-family: 'Roboto Mono', monospace; }
    .text-xs { font-size: 0.75rem; }
    .font-weight-600 { font-weight: 600; }
    .text-success { color: #047857; }
    .text-danger { color: #b91c1c; }
    .w-full { width: 100%; }

    .tab-bar { display: flex; gap: 0.5rem; border-bottom: 2px solid var(--border-color); }
    .tab-btn { display: flex; align-items: center; gap: 0.5rem; padding: 0.75rem 1.25rem; border: none; background: none; color: var(--text-muted); font-size: 0.875rem; font-weight: 500; cursor: pointer; border-bottom: 2px solid transparent; margin-bottom: -2px; transition: all 0.2s; }
    .tab-btn.active { color: var(--primary); border-bottom-color: var(--primary); }
    .tab-btn:hover:not(.active) { color: var(--text-main); }

    .clients-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 1rem; }
    .client-card { background: var(--bg-card); border: 1px solid var(--border-color); border-radius: var(--radius-md); padding: 1.25rem; transition: box-shadow 0.2s; }
    .client-card:hover { box-shadow: 0 4px 16px rgba(0,0,0,0.08); }
    .client-header { display: flex; align-items: flex-start; gap: 0.75rem; margin-bottom: 1rem; }
    .client-avatar { width: 40px; height: 40px; background: var(--primary); color: white; border-radius: 10px; display: flex; align-items: center; justify-content: center; font-weight: 800; font-size: 1.1rem; flex-shrink: 0; }
    .client-title { flex: 1; }
    .client-name { margin: 0 0 0.2rem; font-size: 1rem; font-weight: 700; }
    .client-id { color: var(--text-muted); }
    .active-dot { width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0; margin-top: 4px; }
    .active-dot.active { background: #10b981; box-shadow: 0 0 0 3px #d1fae5; }
    .active-dot:not(.active) { background: #9ca3af; }

    .scopes-row { display: flex; flex-wrap: wrap; gap: 0.375rem; margin-bottom: 0.875rem; }
    .scope-badge { padding: 0.2rem 0.5rem; border-radius: 4px; font-size: 0.7rem; font-weight: 600; background: #e0e7ff; color: #4338ca; text-transform: uppercase; }

    .client-meta { display: flex; flex-direction: column; gap: 0.375rem; }
    .meta-item { display: flex; align-items: center; gap: 0.4rem; font-size: 0.8rem; color: var(--text-muted); }
    .meta-item .material-symbols-rounded { font-size: 1rem; }
    .btn-sm { padding: 0.4rem 0.75rem; font-size: 0.8rem; display: flex; align-items: center; gap: 0.4rem; justify-content: center; }

    .monitor-panel { display: grid; grid-template-columns: 320px 1fr; gap: 1.5rem; align-items: start; }
    @media (max-width: 900px) { .monitor-panel { grid-template-columns: 1fr; } }
    .monitor-form { padding: 1.75rem; }
    .monitor-results { display: flex; flex-direction: column; }
    .monitor-placeholder { min-height: 280px; display: flex; align-items: center; justify-content: center; }

    .balance-card { background: linear-gradient(135deg, #1e40af, #7c3aed); border-radius: var(--radius-md); padding: 1.5rem; color: white; }
    .bal-icon { width: 48px; height: 48px; background: rgba(255,255,255,0.2); border-radius: 50%; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .bal-label { font-size: 0.75rem; opacity: 0.7; text-transform: uppercase; font-weight: 600; }
    .bal-amount { font-size: 2rem; font-weight: 800; line-height: 1.1; }
    .bal-current { margin-top: 0.25rem; }

    .tx-type-badge { padding: 0.15rem 0.4rem; border-radius: 4px; font-size: 0.7rem; font-weight: 700; background: #f3f4f6; color: #6b7280; }

    .table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
    .table th, .table td { padding: 0.75rem 1.25rem; text-align: left; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
    .table th { color: var(--text-muted); font-weight: 600; font-size: 0.75rem; text-transform: uppercase; }
    .text-right { text-align: right; }
    .empty-state { text-align: center; padding: 3rem; color: var(--text-muted); }

    .modal-backdrop { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.5); display: flex; align-items: center; justify-content: center; z-index: 1000; }
    .modal-card { background: white; border-radius: 12px; box-shadow: 0 20px 40px rgba(0,0,0,0.2); overflow: hidden; }
    .modal-header { padding: 1.25rem 1.5rem; border-bottom: 1px solid var(--border-color); }
    .modal-body { padding: 1.5rem; }
    .btn-close { border: none; background: none; font-size: 1.5rem; cursor: pointer; color: var(--text-muted); }

    .form-group { display: flex; flex-direction: column; gap: 0.4rem; }
    .form-label { font-size: 0.8rem; font-weight: 600; color: var(--text-muted); }
    .form-input { padding: 0.625rem 0.875rem; border: 1px solid var(--border-color); border-radius: var(--radius-sm); font-size: 0.875rem; background: white; width: 100%; }
    .form-input:focus { outline: none; border-color: var(--primary); }
  `]
})
export class OpenBankingListComponent implements OnInit {
  private openBankingService = inject(OpenBankingService);
  private notifications = inject(NotificationService);

  activeTab: Tab = 'clients';
  clients: OpenBankingClient[] = [];
  balance: OpenBankingBalance | null = null;
  transactions: OpenBankingTransaction[] = [];
  loading = false;
  monitoring = false;
  saving = false;
  monitorAccountId = '';

  showClientForm = false;
  clientForm = { name: '', redirectUri: '', scopesRaw: 'balance,transactions' };

  showGrantForm = false;
  selectedClient: OpenBankingClient | null = null;
  grantAccountId = '';

  ngOnInit() {
    this.loadClients();
  }

  setTab(tab: Tab) { this.activeTab = tab; }

  loadClients() {
    this.loading = true;
    this.openBankingService.getClients().pipe(
      catchError(() => {
        this.notifications.error('No se pudieron cargar los clientes Open Banking.');
        return of([] as OpenBankingClient[]);
      })
    ).subscribe(data => { this.clients = data; this.loading = false; });
  }

  monitorAccount() {
    if (!this.monitorAccountId.trim()) return;
    this.monitoring = true;
    this.balance = null;
    this.transactions = [];
    const id = this.monitorAccountId.trim();
    this.openBankingService.getBalance(id).pipe(catchError(() => of(null))).subscribe(b => { this.balance = b; this.monitoring = false; });
    this.openBankingService.getTransactions(id, 20).pipe(
      catchError(() => {
        this.notifications.error('No se pudieron cargar las transacciones de la cuenta.');
        return of([]);
      })
    ).subscribe(t => this.transactions = t);
  }

  openClientForm() {
    this.clientForm = { name: '', redirectUri: '', scopesRaw: 'balance,transactions' };
    this.showClientForm = true;
  }

  saveClient() {
    if (!this.clientForm.name || !this.clientForm.redirectUri) return;
    this.saving = true;
    const scopes = this.clientForm.scopesRaw.split(',').map(s => s.trim()).filter(Boolean);
    this.openBankingService.createClient({ name: this.clientForm.name, redirectUri: this.clientForm.redirectUri, scopes }).subscribe({
      next: () => { this.saving = false; this.showClientForm = false; this.loadClients(); },
      error: () => { this.saving = false; this.notifications.error('Error al crear el cliente.'); }
    });
  }

  openGrantForm(client: OpenBankingClient) {
    this.selectedClient = client;
    this.grantAccountId = '';
    this.showGrantForm = true;
  }

  grantAccess() {
    if (!this.selectedClient || !this.grantAccountId.trim()) return;
    this.saving = true;
    this.openBankingService.grantAccountAccess(this.selectedClient.clientId, this.grantAccountId.trim()).subscribe({
      next: updated => {
        this.saving = false;
        this.showGrantForm = false;
        const idx = this.clients.findIndex(c => c.clientId === updated.clientId);
        if (idx >= 0) this.clients[idx] = updated;
      },
      error: () => { this.saving = false; this.notifications.error('Error al otorgar acceso. Verificá el ID de cuenta.'); }
    });
  }
}
