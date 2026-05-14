import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  LoyaltyService, RewardProgram, RewardCatalogItem,
  LoyaltyBalance, LoyaltyEntry
} from './loyalty.service';
import { NotificationService } from '../../core/notification.service';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';

type Tab = 'programs' | 'catalog' | 'account';

@Component({
  selector: 'app-loyalty-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-container">
      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">loyalty</span>
            Fidelización y Recompensas
          </h1>
          <p class="text-muted mt-1">Programas de puntos, catálogo de canjeo y saldo de cuentas.</p>
        </div>
      </div>

      <div class="tab-bar mb-4">
        <button class="tab-btn" [class.active]="activeTab === 'programs'" (click)="setTab('programs')">
          <span class="material-symbols-rounded">star</span> Programas
        </button>
        <button class="tab-btn" [class.active]="activeTab === 'catalog'" (click)="setTab('catalog')">
          <span class="material-symbols-rounded">redeem</span> Catálogo de Canje
        </button>
        <button class="tab-btn" [class.active]="activeTab === 'account'" (click)="setTab('account')">
          <span class="material-symbols-rounded">manage_accounts</span> Consulta de Cuenta
        </button>
      </div>

      <!-- Programs Tab -->
      <div *ngIf="activeTab === 'programs'">
        <div class="d-flex justify-content-end mb-3">
          <button class="btn btn-primary d-flex align-items-center gap-2" (click)="openProgramForm()">
            <span class="material-symbols-rounded">add</span> Nuevo Programa
          </button>
        </div>
        <div class="programs-grid">
          <div class="program-card" *ngFor="let prog of programs">
            <div class="program-header">
              <span class="material-symbols-rounded program-icon">loyalty</span>
              <div class="program-status" [class.active]="prog.isActive">{{ prog.isActive ? 'Activo' : 'Inactivo' }}</div>
            </div>
            <h3 class="program-name">{{ prog.name }}</h3>
            <p class="program-desc text-muted">{{ prog.description }}</p>
            <div class="program-metrics">
              <div class="program-metric">
                <span class="metric-val">{{ prog.pointsPerDollar }}</span>
                <span class="metric-key">pts / $</span>
              </div>
              <div class="metric-divider"></div>
              <div class="program-metric">
                <span class="metric-val">{{ prog.cashbackPercent }}%</span>
                <span class="metric-key">cashback</span>
              </div>
            </div>
          </div>
          <div class="program-card add-card" (click)="openProgramForm()">
            <span class="material-symbols-rounded" style="font-size: 2.5rem; opacity: 0.4">add_circle</span>
            <p class="text-muted mt-2">Agregar programa</p>
          </div>
        </div>
        <div class="card mt-4" *ngIf="programs.length === 0 && !loading">
          <div class="empty-state">
            <span class="material-symbols-rounded" style="font-size: 48px; opacity: 0.3">loyalty</span>
            <p class="mt-2">No hay programas de fidelización configurados.</p>
          </div>
        </div>
      </div>

      <!-- Catalog Tab -->
      <div *ngIf="activeTab === 'catalog'">
        <div class="d-flex justify-content-end mb-3">
          <button class="btn btn-primary d-flex align-items-center gap-2" (click)="openCatalogForm()">
            <span class="material-symbols-rounded">add</span> Nuevo Ítem
          </button>
        </div>
        <div class="catalog-grid">
          <div class="catalog-card" *ngFor="let item of catalog">
            <div class="catalog-cat-badge">{{ item.category }}</div>
            <h4 class="catalog-name">{{ item.name }}</h4>
            <p class="catalog-desc text-muted">{{ item.description }}</p>
            <div class="catalog-footer">
              <div class="points-cost">
                <span class="material-symbols-rounded" style="font-size: 1rem; color: var(--primary)">stars</span>
                {{ item.pointsCost | number }} pts
              </div>
              <span class="avail-badge" [class.available]="item.isAvailable" [class.unavailable]="!item.isAvailable">
                {{ item.isAvailable ? 'Disponible' : 'Agotado' }}
              </span>
            </div>
          </div>
        </div>
        <div class="card mt-4" *ngIf="catalog.length === 0 && !loading">
          <div class="empty-state">
            <span class="material-symbols-rounded" style="font-size: 48px; opacity: 0.3">redeem</span>
            <p class="mt-2">No hay ítems en el catálogo de canje.</p>
          </div>
        </div>
      </div>

      <!-- Account Lookup Tab -->
      <div *ngIf="activeTab === 'account'">
        <div class="lookup-panel">
          <div class="card lookup-form">
            <h3 class="mb-3">Consultar Cuenta</h3>
            <div class="form-group mb-4">
              <label class="form-label">ID de Cuenta (UUID)</label>
              <input class="form-input" [(ngModel)]="lookupAccountId" placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" />
            </div>
            <button class="btn btn-primary w-full d-flex align-items-center gap-2 justify-center" (click)="lookupAccount()" [disabled]="!lookupAccountId || lookingUp">
              <span class="material-symbols-rounded">search</span>
              {{ lookingUp ? 'Consultando...' : 'Consultar Saldo' }}
            </button>
          </div>

          <div *ngIf="balance" class="lookup-results">
            <div class="balance-hero">
              <div class="balance-icon"><span class="material-symbols-rounded">stars</span></div>
              <div class="balance-main">
                <div class="balance-points">{{ balance.availablePoints | number }} <span class="pts-label">puntos disponibles</span></div>
                <div class="balance-sub">{{ balance.totalPoints | number }} pts acumulados · {{ balance.redeemedPoints | number }} pts canjeados</div>
              </div>
              <div class="balance-cashback">
                <div class="cashback-val">{{ balance.cashbackAccrued | currency }}</div>
                <div class="cashback-label">Cashback acumulado</div>
              </div>
            </div>
            <div class="card p-0 mt-4" *ngIf="entries.length">
              <div class="table-responsive">
                <table class="table">
                  <thead>
                    <tr>
                      <th>TIPO</th>
                      <th>DESCRIPCIÓN</th>
                      <th class="text-right">PUNTOS</th>
                      <th>FECHA</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr *ngFor="let entry of entries">
                      <td>
                        <span class="entry-badge" [class.earn]="entry.points > 0" [class.spend]="entry.points < 0">{{ entry.type }}</span>
                      </td>
                      <td class="text-muted">{{ entry.description }}</td>
                      <td class="text-right font-weight-600" [class.text-success]="entry.points > 0" [class.text-danger]="entry.points < 0">
                        {{ entry.points > 0 ? '+' : '' }}{{ entry.points | number }}
                      </td>
                      <td class="text-muted text-xs">{{ entry.occurredAt | date:'short' }}</td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>
          </div>
          <div class="card lookup-placeholder" *ngIf="!balance && !lookingUp">
            <div class="empty-state">
              <span class="material-symbols-rounded" style="font-size: 64px; opacity: 0.2">loyalty</span>
              <p class="mt-2">Ingresá el ID de cuenta para ver el saldo y el historial de puntos.</p>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Program Form Modal -->
    <div class="modal-backdrop" *ngIf="showProgramForm">
      <div class="modal-card" style="max-width: 460px; width: 90%">
        <div class="modal-header d-flex justify-content-between">
          <h3 class="m-0">Nuevo Programa de Recompensas</h3>
          <button class="btn-close" (click)="showProgramForm = false">×</button>
        </div>
        <div class="modal-body">
          <div class="form-group mb-3">
            <label class="form-label">Nombre</label>
            <input class="form-input" [(ngModel)]="programForm.name" placeholder="Ej: Programa Gold" />
          </div>
          <div class="form-group mb-3">
            <label class="form-label">Descripción</label>
            <input class="form-input" [(ngModel)]="programForm.description" placeholder="Descripción del programa" />
          </div>
          <div class="form-group mb-3">
            <label class="form-label">Puntos por dólar</label>
            <input class="form-input" type="number" [(ngModel)]="programForm.pointsPerDollar" min="0" />
          </div>
          <div class="form-group mb-3">
            <label class="form-label">Cashback (%)</label>
            <input class="form-input" type="number" [(ngModel)]="programForm.cashbackPercent" min="0" max="100" step="0.1" />
          </div>
        </div>
        <div class="modal-footer d-flex justify-content-end gap-2 p-3">
          <button class="btn btn-outline" (click)="showProgramForm = false">Cancelar</button>
          <button class="btn btn-primary" (click)="saveProgram()" [disabled]="saving">{{ saving ? 'Guardando...' : 'Guardar' }}</button>
        </div>
      </div>
    </div>

    <!-- Catalog Item Form Modal -->
    <div class="modal-backdrop" *ngIf="showCatalogForm">
      <div class="modal-card" style="max-width: 460px; width: 90%">
        <div class="modal-header d-flex justify-content-between">
          <h3 class="m-0">Nuevo Ítem de Catálogo</h3>
          <button class="btn-close" (click)="showCatalogForm = false">×</button>
        </div>
        <div class="modal-body">
          <div class="form-group mb-3">
            <label class="form-label">Nombre</label>
            <input class="form-input" [(ngModel)]="catalogForm.name" placeholder="Ej: Bono de $10" />
          </div>
          <div class="form-group mb-3">
            <label class="form-label">Descripción</label>
            <input class="form-input" [(ngModel)]="catalogForm.description" />
          </div>
          <div class="form-group mb-3">
            <label class="form-label">Categoría</label>
            <select class="form-input" [(ngModel)]="catalogForm.category">
              <option value="Cash">Efectivo/Bono</option>
              <option value="Travel">Viajes</option>
              <option value="Shopping">Compras</option>
              <option value="Entertainment">Entretenimiento</option>
              <option value="Other">Otro</option>
            </select>
          </div>
          <div class="form-group mb-3">
            <label class="form-label">Costo en puntos</label>
            <input class="form-input" type="number" [(ngModel)]="catalogForm.pointsCost" min="1" />
          </div>
        </div>
        <div class="modal-footer d-flex justify-content-end gap-2 p-3">
          <button class="btn btn-outline" (click)="showCatalogForm = false">Cancelar</button>
          <button class="btn btn-primary" (click)="saveCatalogItem()" [disabled]="saving">{{ saving ? 'Guardando...' : 'Guardar' }}</button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .text-muted { color: var(--text-muted); }
    .font-weight-600 { font-weight: 600; }
    .text-xs { font-size: 0.75rem; }
    .text-success { color: #047857; }
    .text-danger { color: #b91c1c; }
    .w-full { width: 100%; }
    .justify-center { justify-content: center; }

    .tab-bar { display: flex; gap: 0.5rem; border-bottom: 2px solid var(--border-color); }
    .tab-btn { display: flex; align-items: center; gap: 0.5rem; padding: 0.75rem 1.25rem; border: none; background: none; color: var(--text-muted); font-size: 0.875rem; font-weight: 500; cursor: pointer; border-bottom: 2px solid transparent; margin-bottom: -2px; transition: all 0.2s; }
    .tab-btn.active { color: var(--primary); border-bottom-color: var(--primary); }
    .tab-btn:hover:not(.active) { color: var(--text-main); }

    /* Programs grid */
    .programs-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 1rem; }
    .program-card { background: var(--bg-card); border: 1px solid var(--border-color); border-radius: var(--radius-md); padding: 1.5rem; transition: box-shadow 0.2s, transform 0.2s; }
    .program-card:hover { box-shadow: 0 4px 20px rgba(0,0,0,0.08); transform: translateY(-2px); }
    .add-card { display: flex; flex-direction: column; align-items: center; justify-content: center; cursor: pointer; border-style: dashed; min-height: 180px; }
    .program-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
    .program-icon { color: var(--primary); font-size: 2rem; }
    .program-status { font-size: 0.7rem; font-weight: 700; text-transform: uppercase; padding: 0.2rem 0.6rem; border-radius: 20px; }
    .program-status.active { background: #d1fae5; color: #047857; }
    .program-status:not(.active) { background: #f3f4f6; color: #9ca3af; }
    .program-name { font-size: 1.1rem; font-weight: 700; margin: 0 0 0.4rem; }
    .program-desc { font-size: 0.8rem; margin: 0 0 1.25rem; }
    .program-metrics { display: flex; align-items: center; gap: 1rem; padding-top: 1rem; border-top: 1px solid var(--border-color); }
    .program-metric { display: flex; flex-direction: column; align-items: center; flex: 1; }
    .metric-val { font-size: 1.5rem; font-weight: 800; color: var(--primary); }
    .metric-key { font-size: 0.7rem; color: var(--text-muted); text-transform: uppercase; }
    .metric-divider { width: 1px; height: 40px; background: var(--border-color); }

    /* Catalog grid */
    .catalog-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 1rem; }
    .catalog-card { background: var(--bg-card); border: 1px solid var(--border-color); border-radius: var(--radius-md); padding: 1.25rem; display: flex; flex-direction: column; gap: 0.5rem; transition: box-shadow 0.2s; }
    .catalog-card:hover { box-shadow: 0 4px 16px rgba(0,0,0,0.08); }
    .catalog-cat-badge { display: inline-block; padding: 0.15rem 0.5rem; border-radius: 4px; font-size: 0.7rem; font-weight: 600; background: #e0e7ff; color: #4338ca; text-transform: uppercase; width: fit-content; }
    .catalog-name { font-weight: 700; margin: 0.25rem 0 0; font-size: 1rem; }
    .catalog-desc { font-size: 0.8rem; flex: 1; }
    .catalog-footer { display: flex; justify-content: space-between; align-items: center; padding-top: 0.75rem; border-top: 1px solid var(--border-color); }
    .points-cost { display: flex; align-items: center; gap: 0.25rem; font-weight: 700; font-size: 0.875rem; }
    .avail-badge { font-size: 0.7rem; font-weight: 700; padding: 0.15rem 0.5rem; border-radius: 4px; }
    .avail-badge.available { background: #d1fae5; color: #047857; }
    .avail-badge.unavailable { background: #fee2e2; color: #b91c1c; }

    /* Account lookup */
    .lookup-panel { display: grid; grid-template-columns: 360px 1fr; gap: 1.5rem; align-items: start; }
    @media (max-width: 900px) { .lookup-panel { grid-template-columns: 1fr; } }
    .lookup-form { padding: 1.75rem; }
    .lookup-placeholder { min-height: 280px; display: flex; align-items: center; justify-content: center; }
    .lookup-results { display: flex; flex-direction: column; }
    .balance-hero { background: linear-gradient(135deg, var(--primary), #7c3aed); border-radius: var(--radius-md); padding: 1.5rem; color: white; display: flex; align-items: center; gap: 1.5rem; }
    .balance-icon { width: 56px; height: 56px; background: rgba(255,255,255,0.2); border-radius: 50%; display: flex; align-items: center; justify-content: center; }
    .balance-icon .material-symbols-rounded { font-size: 1.75rem; }
    .balance-main { flex: 1; }
    .balance-points { font-size: 2rem; font-weight: 800; }
    .pts-label { font-size: 1rem; font-weight: 500; opacity: 0.8; }
    .balance-sub { font-size: 0.8rem; opacity: 0.7; margin-top: 0.25rem; }
    .balance-cashback { text-align: right; }
    .cashback-val { font-size: 1.5rem; font-weight: 800; }
    .cashback-label { font-size: 0.75rem; opacity: 0.7; }

    .entry-badge { padding: 0.2rem 0.5rem; border-radius: 4px; font-size: 0.7rem; font-weight: 700; }
    .entry-badge.earn { background: #d1fae5; color: #047857; }
    .entry-badge.spend { background: #fee2e2; color: #b91c1c; }

    .table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
    .table th, .table td { padding: 0.875rem 1.25rem; text-align: left; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
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
    .form-input { padding: 0.625rem 0.875rem; border: 1px solid var(--border-color); border-radius: var(--radius-sm); font-size: 0.875rem; width: 100%; background: white; transition: border-color 0.2s; }
    .form-input:focus { outline: none; border-color: var(--primary); }
  `]
})
export class LoyaltyListComponent implements OnInit {
  private loyaltyService = inject(LoyaltyService);
  private notifications = inject(NotificationService);

  activeTab: Tab = 'programs';
  programs: RewardProgram[] = [];
  catalog: RewardCatalogItem[] = [];
  balance: LoyaltyBalance | null = null;
  entries: LoyaltyEntry[] = [];
  loading = false;
  lookingUp = false;
  saving = false;
  lookupAccountId = '';

  showProgramForm = false;
  programForm = { name: '', description: '', pointsPerDollar: 1, cashbackPercent: 0 };

  showCatalogForm = false;
  catalogForm = { name: '', description: '', category: 'Cash', pointsCost: 1000 };

  ngOnInit() {
    this.loadPrograms();
    this.loadCatalog();
  }

  setTab(tab: Tab) { this.activeTab = tab; }

  loadPrograms() {
    this.loyaltyService.getPrograms().pipe(
      catchError(() => {
        this.notifications.error('No se pudieron cargar los programas de fidelización.');
        return of([] as RewardProgram[]);
      })
    ).subscribe(d => this.programs = d);
  }

  loadCatalog() {
    this.loyaltyService.getCatalog().pipe(
      catchError(() => {
        this.notifications.error('No se pudo cargar el catálogo de recompensas.');
        return of([] as RewardCatalogItem[]);
      })
    ).subscribe(d => this.catalog = d);
  }

  lookupAccount() {
    if (!this.lookupAccountId.trim()) return;
    this.lookingUp = true;
    this.balance = null;
    this.loyaltyService.getBalance(this.lookupAccountId.trim()).pipe(catchError(() => of(null))).subscribe(b => {
      this.balance = b;
      this.lookingUp = false;
    });
    this.loyaltyService.getEntries(this.lookupAccountId.trim(), 20).pipe(
      catchError(() => {
        this.notifications.error('No se pudieron cargar los movimientos de la cuenta.');
        return of([] as LoyaltyEntry[]);
      })
    ).subscribe(e => this.entries = e);
  }

  openProgramForm() {
    this.programForm = { name: '', description: '', pointsPerDollar: 1, cashbackPercent: 0 };
    this.showProgramForm = true;
  }

  saveProgram() {
    if (!this.programForm.name) return;
    this.saving = true;
    this.loyaltyService.upsertProgram(this.programForm).subscribe({
      next: () => { this.saving = false; this.showProgramForm = false; this.loadPrograms(); this.notifications.success('Programa guardado correctamente.'); },
      error: () => { this.saving = false; this.notifications.error('Error al guardar el programa.'); }
    });
  }

  openCatalogForm() {
    this.catalogForm = { name: '', description: '', category: 'Cash', pointsCost: 1000 };
    this.showCatalogForm = true;
  }

  saveCatalogItem() {
    if (!this.catalogForm.name) return;
    this.saving = true;
    this.loyaltyService.upsertCatalogItem(this.catalogForm).subscribe({
      next: () => { this.saving = false; this.showCatalogForm = false; this.loadCatalog(); this.notifications.success('Ítem guardado correctamente.'); },
      error: () => { this.saving = false; this.notifications.error('Error al guardar el ítem.'); }
    });
  }
}
