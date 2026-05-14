import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { CardService, Card, CardStatus } from './card.service';
import { CustomerService, Customer, Account } from '../customers/customer.service';
import { CatalogService, CatalogBin } from '../../switch/catalog.service';
import { NotificationService } from '../../../core/notification.service';
import { catchError, debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { of, forkJoin, Subject } from 'rxjs';

import { SearchSelectComponent } from '../../../shared/components/search-select.component';

@Component({
  selector: 'app-card-list',
  standalone: true,
  imports: [CommonModule, FormsModule, SearchSelectComponent],
  template: `
    <div class="page-container">
      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title">Gestión de Tarjetas</h1>
          <p class="text-muted mt-1">Busque y administre el ciclo de vida de los plásticos de sus clientes.</p>
        </div>
        <div class="header-actions">
          <button class="btn btn-outline" (click)="loadCards()">
            <span class="material-symbols-rounded">refresh</span>
            Refrescar
          </button>
          <button class="btn btn-primary" (click)="openIssueModal()">
            <span class="material-symbols-rounded">credit_score</span>
            Emitir Nueva
          </button>
        </div>
      </div>

      <!-- Filters & Search -->
      <div class="filters-card card mb-4">
        <div class="search-box">
          <span class="material-symbols-rounded search-icon">search</span>
          <input type="text" class="search-input" placeholder="Buscar por Terminan (Ej. 4123) o PAN Token..." (input)="onSearch($event)">
        </div>
        <div class="filters-group d-flex gap-3">
          <select class="form-control filter-select">
            <option value="">Estado</option>
            <option value="5">Activas</option>
            <option value="6">Bloqueada</option>
            <option value="1">Creadas/En Tránsito</option>
          </select>
        </div>
      </div>

      <!-- Cards Table -->
      <div class="card p-0">
        <div class="table-responsive">
          <table class="table">
            <thead>
              <tr>
                <th>CLIENTE</th>
                <th>TARJETA (PAN ENMASCARADO)</th>
                <th>BIN RED</th>
                <th>EXPIRACIÓN</th>
                <th>ESTADO</th>
                <th class="text-right">ACCIONES</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let c of filteredCards">
                <td>
                  <div class="d-flex align-items-center gap-2">
                    <span class="material-symbols-rounded text-muted" style="font-size: 20px;">person</span>
                    <span class="font-weight-500">{{ c.customerName }}</span>
                  </div>
                </td>
                <td>
                  <div class="d-flex flex-column">
                    <strong class="text-main" style="letter-spacing: 1px; font-family: monospace; font-size: 1rem;">
                      {{c.maskedPan}}
                    </strong>
                    <span class="text-muted text-sm d-flex align-items-center gap-1 mt-1">
                      <span class="material-symbols-rounded" style="font-size: 14px;">key</span> {{c.panToken | slice:0:16}}...
                    </span>
                  </div>
                </td>
                <td class="font-weight-500">{{c.bin}}</td>
                <td class="text-muted">{{ c.expiryYyMm.substring(2,4) }}/{{ c.expiryYyMm.substring(0,2) }}</td>
                <td>
                  <span class="status-badge" [ngClass]="getStatusClass(c.status)">
                    {{getStatusName(c.status)}}
                  </span>
                </td>
                <td class="text-right">
                  <div class="action-buttons gap-2">
                    <button class="btn btn-icon text-primary" title="Cambiar PIN" (click)="openPinModal(c)">
                       <span class="material-symbols-rounded">pin</span>
                    </button>
                    <button class="btn btn-icon" [ngClass]="c.status === 5 ? 'text-danger' : 'text-success'" 
                            [title]="c.status === 5 ? 'Bloquear' : 'Activar'" (click)="toggleStatus(c)">
                       <span class="material-symbols-rounded">{{ c.status === 5 ? 'block' : 'check_circle' }}</span>
                    </button>
                    <button class="btn btn-outline btn-sm" (click)="viewCard(c.id)">
                      <span class="material-symbols-rounded" style="font-size: 16px;">admin_panel_settings</span>
                    </button>
                  </div>
                </td>
              </tr>
              <tr *ngIf="filteredCards.length === 0">
                <td colspan="6" class="text-center py-5 text-muted">
                  <span class="material-symbols-rounded" style="font-size: 48px; opacity: 0.5;">credit_card_off</span>
                  <p class="mt-2">No se encontraron tarjetas.</p>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Issue Modal -->
      <div class="modal-overlay" [class.show]="isModalOpen">
        <div class="modal-card card">
          <div class="modal-header">
            <h3>Emisión de Tarjeta (In-App)</h3>
            <button class="icon-btn" (click)="closeIssueModal()">
              <span class="material-symbols-rounded">close</span>
            </button>
          </div>
          <div class="modal-body">
            <div class="alert alert-warning mb-3 d-flex gap-2">
              <span class="material-symbols-rounded">gpp_maybe</span>
              <div>
                Esta acción genera un **PAN Real** en Bóveda que será inyectado tokenizado. El backend contactará con el HSM para la generación de CVV2/Track2 si aplica.
              </div>
            </div>
            
            <div class="input-group mb-3">
              <label class="field-label-sm">Cliente</label>
              <app-search-select 
                [options]="customers"
                [(value)]="selectedCustomerId"
                (valueChange)="onCustomerChange($event)"
                labelKey="fullName"
                subLabelKey="documentId"
                placeholder="Buscar por nombre o documento..."
              ></app-search-select>
            </div>
            
            <div class="input-group mb-3" *ngIf="selectedCustomerId">
              <label>Cuenta Destino</label>
              <app-search-select 
                [options]="accountOptions"
                [(value)]="selectedAccountId"
                labelKey="label"
                subLabelKey="subLabel"
                placeholder="Seleccione cuenta (Obligatorio)..."
              ></app-search-select>
              <small class="text-danger mt-1 d-block" *ngIf="selectedCustomerAccounts.length === 0">
                 Este cliente no tiene cuentas abiertas. Operación no permitida.
              </small>
            </div>
            <div class="input-group">
              <label>Ruta BIN Transaccional (Vault Config)</label>
              <app-search-select 
                [options]="binOptions"
                [(value)]="selectedBin"
                valueKey="binStart"
                labelKey="label"
                subLabelKey="subLabel"
                placeholder="Buscar BIN o Producto..."
              ></app-search-select>
            </div>
          </div>
          <div class="modal-footer d-flex justify-content-between">
            <button class="btn btn-outline" (click)="closeIssueModal()">Cancelar</button>
            <button class="btn btn-primary" [disabled]="!selectedAccountId || !selectedBin" (click)="issueCard(selectedAccountId, selectedBin)">Emitir y Tokenizar PAN</button>
          </div>
        </div>
      </div>

      <!-- PIN Modal -->
      <div class="modal-overlay" [class.show]="isPinModalOpen">
        <div class="modal-card card shadow-lg" style="max-width: 400px;">
          <div class="modal-header">
            <h3>Actualizar PIN de Seguridad</h3>
            <button class="icon-btn" (click)="isPinModalOpen = false">
              <span class="material-symbols-rounded">close</span>
            </button>
          </div>
          <div class="modal-body p-4 text-center">
            <p class="text-muted mb-4">Ingrese el nuevo PIN de 4 dígitos para la tarjeta terminada en <strong>{{selectedCard?.last4}}</strong></p>
            <div class="d-flex justify-content-center">
              <input type="password" class="form-control text-center font-bold" 
                     maxlength="4" style="font-size: 2rem; width: 150px; letter-spacing: 1rem;" 
                     [(ngModel)]="newPin">
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-outline" (click)="isPinModalOpen = false">Cancelar</button>
            <button class="btn btn-primary w-100" [disabled]="newPin.length !== 4 || savingPin" (click)="savePin()">
              {{ savingPin ? 'Guardando...' : 'Cambiar PIN' }}
            </button>
          </div>
        </div>
      </div>

    </div>

    <!-- Confirm Modal (bloquear / activar tarjeta) -->
    <div class=\"modal-overlay\" [class.show]=\"confirmModal.open\" (click)=\"confirmModal.open = false\">
      <div class=\"modal-card confirm-card\" (click)=\"$event.stopPropagation()\">
        <div class=\"confirm-icon-wrap\" [ngClass]=\"confirmModal.type\">
          <span class=\"material-symbols-rounded\">{{ confirmModal.icon }}</span>
        </div>
        <h4 class=\"confirm-title\">{{ confirmModal.title }}</h4>
        <p class=\"confirm-message\">{{ confirmModal.message }}</p>
        <div class=\"confirm-actions\">
          <button class=\"btn btn-outline\" (click)=\"confirmModal.open = false\">Cancelar</button>
          <button class=\"btn\" [ngClass]=\"confirmModal.btnClass\" (click)=\"confirmModal.onConfirm(); confirmModal.open = false\">
            {{ confirmModal.confirmLabel }}
          </button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .filters-card { display: flex; flex-direction: row; justify-content: space-between; align-items: center; padding: 1rem 1.5rem; }
    .search-box { display: flex; align-items: center; background-color: var(--bg-main); border-radius: var(--radius-md); padding: 0.75rem 1rem; width: 400px;}
    .search-icon { color: var(--text-muted); }
    .search-input { border: none; background: transparent; outline: none; width: 100%; margin-left: 0.5rem; font-family: inherit;}
    .filter-select { width: 150px; }
    
    .table-responsive { overflow-x: auto; }
    .table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
    .table th, .table td { padding: 1rem 1.5rem; text-align: left; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
    .table th { color: var(--text-muted); font-weight: 600; font-size: 0.75rem; text-transform: uppercase; }
    
    .status-badge { padding: 0.25rem 0.6rem; border-radius: 1rem; font-size: 0.7rem; font-weight: 600; display: inline-flex; align-items: center; gap: 0.25rem; }
    .status-badge::before { content: ''; width: 6px; height: 6px; border-radius: 50%; }
    .status-active { background: #ecfdf5; color: #047857; }
    .status-active::before { background: #10b981; }
    .status-blocked { background: #fef2f2; color: #b91c1c; }
    .status-blocked::before { background: #ef4444; }
    .status-created { background: #eff6ff; color: #1e3a8a; }
    .status-created::before { background: #3b82f6; }

    .action-buttons { display: flex; justify-content: flex-end; }
    .btn-sm { padding: 0.35rem 0.75rem; font-size: 0.75rem; display: inline-flex; align-items: center; gap: 0.25rem;}
    
    .text-sm { font-size: 0.75rem; }
    
    .alert { padding: 1rem 1.25rem; border-radius: var(--radius-md); font-size: 0.875rem;}
    .alert-warning { background-color: #fffbeb; color: #92400e; border: 1px solid #fcd34d; }

    /* Modal */
    .modal-overlay { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.5); display: none; align-items: center; justify-content: center; z-index: 1000; backdrop-filter: blur(2px);}
    .modal-overlay.show { display: flex; }
    .modal-card { width: 100%; max-width: 500px; padding: 0; animation: scaleUp 0.3s ease; }
    .modal-header { display: flex; justify-content: space-between; align-items: center; padding: 1.5rem; border-bottom: 1px solid var(--border-color); }
    .modal-header h3 { margin: 0; font-size: 1.25rem; }
    .modal-body { padding: 1.5rem; }
    .modal-footer { padding: 1rem 1.5rem; border-top: 1px solid var(--border-color); background: var(--bg-main); border-radius: 0 0 var(--radius-lg) var(--radius-lg); }
    .icon-btn { background: none; border: none; cursor: pointer; color: var(--text-muted); padding: 0.25rem; }
    .icon-btn:hover { color: var(--danger); }
    @keyframes scaleUp { from { transform: scale(0.95); opacity: 0; } to { transform: scale(1); opacity: 1; } }

    .field-label-sm { font-size: 0.7rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.07em; color: var(--text-muted); display: block; margin-bottom: 0.4rem; }

    /* Confirm modal */
    .confirm-card { 
      background: var(--bg-paper);
      max-width: 360px; padding: 2rem 1.5rem; text-align: center; 
      border-radius: 16px; box-shadow: 0 10px 30px rgba(0,0,0,0.15);
      animation: scaleUp 0.25s ease; 
    }
    .confirm-icon-wrap { width: 52px; height: 52px; border-radius: 50%; display: inline-flex; align-items: center; justify-content: center; margin-bottom: 1rem; }
    .confirm-icon-wrap.danger { background: #fee2e2; }
    .confirm-icon-wrap.danger .material-symbols-rounded { color: #dc2626; font-size: 26px; }
    .confirm-icon-wrap.success { background: #dcfce7; }
    .confirm-icon-wrap.success .material-symbols-rounded { color: #16a34a; font-size: 26px; }
    .confirm-title { font-size: 1rem; font-weight: 700; margin-bottom: 0.4rem; }
    .confirm-message { font-size: 0.82rem; color: var(--text-muted); margin-bottom: 1.4rem; }
    .confirm-actions { display: flex; gap: 0.75rem; justify-content: center; }
    .btn-danger { background: #dc2626; color: #fff; }
    .btn-danger:hover { background: #b91c1c; }
    .btn-success-c { background: #16a34a; color: #fff; }
    .btn-success-c:hover { background: #15803d; }
  `]
})
export class CardListComponent {
  private cardService = inject(CardService);
  private router = inject(Router);
  private customerService = inject(CustomerService);
  private catalogService = inject(CatalogService);
  private notifications = inject(NotificationService);

  cards: Card[] = [];
  filteredCards: Card[] = [];
  isModalOpen = false;

  customers: Customer[] = [];
  availableBins: CatalogBin[] = [];
  selectedCustomerId: string = '';
  selectedCustomerAccounts: Account[] = [];
  selectedAccountId: string = '';

  // Confirm modal
  confirmModal = {
    open: false,
    type: 'danger' as 'danger' | 'success',
    icon: 'block',
    title: '',
    message: '',
    confirmLabel: 'Confirmar',
    btnClass: 'btn-danger',
    onConfirm: () => {}
  };

  private searchSubject = new Subject<string>();
  isPinModalOpen = false;
  selectedCard: Card | null = null;
  newPin = '';
  savingPin = false;

  selectedBin: string = '';

  get accountOptions() {
    return this.selectedCustomerAccounts.map(a => ({
      id: a.id,
      label: `[${a.accountType === 2 ? 'CRÉDITO' : 'DÉBITO'}] Prod: ${a.productCode}`,
      subLabel: `Lim: $${a.creditLimit.toFixed(2)}`
    }));
  }

  get binOptions() {
    return this.availableBins.map(b => ({
      binStart: b.binStart,
      label: `${b.brand} (${b.binStart})`,
      subLabel: b.product
    }));
  }

  constructor() {
    this.loadCards();
    this.searchSubject.pipe(
      debounceTime(400),
      distinctUntilChanged()
    ).subscribe(term => this.performSearch(term));
  }

  loadCards() {
    this.cardService.getCards().pipe(
      catchError(err => {
        console.error('Error loading cards:', err?.status, err?.message);
        this.notifications.warning('No se pudo cargar la lista de tarjetas');
        return of([] as Card[]);
      })
    ).subscribe(cards => {
      this.cards = cards;
      this.filteredCards = cards;
    });

    // Populate customers for issuance dropdown only
    this.customerService.getCustomers().pipe(
      catchError(() => {
        this.notifications.error('Error al cargar lista de clientes');
        return of([]);
      })
    ).subscribe(data => this.customers = data);
    
    this.catalogService.getBins().pipe(
      catchError(() => {
        this.notifications.error('Error al cargar BINs disponibles');
        return of([]);
      })
    ).subscribe(data => {
      this.availableBins = data.filter(b => b.enabled);
    });
  }

  getStatusClass(status: CardStatus): string {
    switch (status) {
      case CardStatus.Active: return 'status-active';
      case CardStatus.Blocked: return 'status-blocked';
      default: return 'status-created';
    }
  }

  getStatusName(status: CardStatus): string {
    return CardStatus[status] || 'Desconocido';
  }

  onSearch(event: any) {
    this.searchSubject.next(event.target.value);
  }

  performSearch(term: string) {
    const t = term.toLowerCase();
    this.filteredCards = this.cards.filter(c =>
      c.last4.includes(t) ||
      c.panToken.toLowerCase().includes(t) ||
      c.maskedPan.includes(t) ||
      (c.customerName && c.customerName.toLowerCase().includes(t))
    );
  }

  viewCard(id: string) {
    this.router.navigate(['/app/issuer/cards', id]);
  }

  openIssueModal() {
    this.isModalOpen = true;
    this.selectedCustomerId = '';
    this.selectedAccountId = '';
    this.selectedBin = '';
    this.selectedCustomerAccounts = [];
    
    if (this.customers.length === 0) {
      this.customerService.getCustomers().pipe(
        catchError(() => {
          this.notifications.error('No se pudieron cargar los clientes. Verificá que el backend esté disponible.');
          return of([]);
        })
      ).subscribe(data => {
        this.customers = data;
      });
    }
  }

  closeIssueModal() { this.isModalOpen = false; }

  onCustomerChange(custId: string) {
    this.selectedAccountId = '';
    this.selectedCustomerAccounts = [];
    if (!custId) return;

    this.customerService.getCustomer(custId).pipe(
      catchError(() => {
        this.notifications.error('Error al obtener detalle del cliente');
        return of(null);
      })
    ).subscribe(detail => {
      if (detail && detail.accounts) {
        this.selectedCustomerAccounts = detail.accounts;
      } else {
        this.selectedCustomerAccounts = [];
      }
    });
  }

  issueCard(accountId: string, bin: string) {
    if (!accountId) return;

    // Generate a valid-length PAN (16 chars) starting with the selected BIN
    let pan = bin;
    while (pan.length < 15) {
      pan += Math.floor(Math.random() * 10).toString();
    }
    // Simple Luhn-ish suffix or just 16th digit
    pan += Math.floor(Math.random() * 10).toString();

    const expiryYyMm = '2912'; // Dec 2029

    this.cardService.issueCard(accountId, bin, pan, expiryYyMm).pipe(
      catchError(err => {
        console.error('Error issuing card:', err?.status, err?.message);
        this.notifications.error('No se pudo emitir la tarjeta. Verifique la conexión.');
        return of(null);
      })
    ).subscribe(res => {
      if (res) {
        this.notifications.success('Tarjeta emitida y tokenizada');
        this.cards.unshift(res);
        this.filteredCards = [...this.cards];
        this.closeIssueModal();
      }
    });
  }

  closePinModal() {
    this.isPinModalOpen = false;
    this.selectedCard = null;
    this.newPin = '';
  }

  toggleStatus(card: Card) {
    const isBlocking = card.status === CardStatus.Active;
    const actionName = isBlocking ? 'bloquear' : 'activar';
    
    this.confirmModal = {
      open: true,
      type: isBlocking ? 'danger' : 'success',
      icon: isBlocking ? 'block' : 'check_circle',
      title: isBlocking ? 'Bloquear Tarjeta' : 'Activar Tarjeta',
      message: `¿Está seguro de que desea ${actionName} la tarjeta ${card.maskedPan}?`,
      confirmLabel: isBlocking ? 'Sí, Bloquear' : 'Sí, Activar',
      btnClass: isBlocking ? 'btn-danger' : 'btn-success-c',
      onConfirm: () => {
         const obs = isBlocking ? this.cardService.blockCard(card.id) : this.cardService.activateCard(card.id);
         obs.subscribe({
           next: () => {
             this.notifications.success(`Tarjeta ${actionName}da correctamente.`);
             this.loadCards();
           },
           error: () => this.notifications.error(`No se pudo ${actionName} la tarjeta.`)
         });
      }
    };
  }

  openPinModal(card: Card) {
    this.selectedCard = card;
    this.newPin = '';
    this.isPinModalOpen = true;
  }

  savePin() {
    if (!this.selectedCard || this.newPin.length !== 4) return;
    this.savingPin = true;
    this.cardService.setPin(this.selectedCard.id, this.newPin).subscribe({
      next: () => {
        this.savingPin = false;
        this.isPinModalOpen = false;
        this.notifications.success('PIN actualizado correctamente.');
      },
      error: () => {
        this.savingPin = false;
        this.notifications.error('Error al actualizar el PIN.');
      }
    });
  }
}
