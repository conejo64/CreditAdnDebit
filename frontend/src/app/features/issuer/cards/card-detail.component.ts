import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { CardService, Card, CardStatus } from './card.service';
import { catchError, map, switchMap } from 'rxjs/operators';
import { of, forkJoin } from 'rxjs';
import { CustomerService } from '../customers/customer.service';
import { NotificationService } from '../../../core/notification.service';

@Component({
  selector: 'app-card-detail',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-container" *ngIf="card">
      <div class="breadcrumb d-flex align-items-center gap-2 mb-3">
        <a href="javascript:void(0)" (click)="goBack()" class="text-muted text-decoration-none d-flex align-items-center gap-1">
          <span class="material-symbols-rounded" style="font-size: 18px">arrow_back</span> Regresar a Tarjetas
        </a>
      </div>

      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">credit_card</span>
            Tarjeta Asignada: {{card.maskedPan}}
          </h1>
          <p class="text-muted mt-1">
            Cliente: <strong class="text-main">{{card.customerName || 'Buscando...'}}</strong> • 
            Vinculada a la cuenta: {{card.accountId}} • 
            Creada el {{card.createdOn | date}}
          </p>
        </div>
        <div class="header-actions">
          <button class="btn" 
            [ngClass]="card.status === enumStatus.Active ? 'btn-outline text-warning' : 'btn-primary'" 
            *ngIf="card.status !== enumStatus.Cancelled"
            (click)="toggleBlock()">
            <span class="material-symbols-rounded">{{card.status === enumStatus.Active ? 'lock' : 'lock_open_right'}}</span>
            {{card.status === enumStatus.Active ? 'Bloquear Temporal' : 'Desbloquear y Activar'}}
          </button>
          <button class="btn btn-outline text-danger" *ngIf="card.status !== enumStatus.Cancelled" (click)="cancelCard()">
            <span class="material-symbols-rounded">cancel</span>
            Cancelación Definitiva
          </button>
        </div>
      </div>

      <div class="d-flex gap-4">
        <!-- left: Plastic visualization -->
        <div class="visual-card-wrapper" [class.blocked]="card.status !== enumStatus.Active">
          <div class="plastic-card">
            <div class="chip"></div>
            <div class="network-logo"></div>
            <div class="pan">{{card.maskedPan}}</div>
            <div class="d-flex justify-content-between mt-4">
              <div class="last4">CVV: ***</div>
              <div class="exp">EXP: {{ card.expiryYyMm.substring(2,4) }}/{{ card.expiryYyMm.substring(0,2) }}</div>
            </div>
          </div>
          <div class="status-overlay" *ngIf="card.status !== enumStatus.Active">
            <span class="material-symbols-rounded warning-icon">lock</span>
            {{getStatusName(card.status)}}
          </div>
        </div>

        <!-- right: Properties -->
        <div class="card flex-grow-1 p-0">
          <div class="card-header border-bottom p-3 d-flex justify-content-between align-items-center">
            <h3 class="m-0 text-main">Propiedades de Bóveda y Ruteo</h3>
            <span class="status-badge" [ngClass]="getStatusClass(card.status)">{{getStatusName(card.status)}}</span>
          </div>
          
          <div class="p-3">
             <div class="alert alert-info mb-4 d-flex gap-2">
                <span class="material-symbols-rounded" style="color: #3b82f6">verified_user</span>
                <div>Tokenizada bajo esquema <strong>CardVault v1</strong>. Este registro es mapeado transparentemente a los sistemas Adquirentes e ISO8583. No se puede decriptar en este componente.</div>
             </div>
             
             <ul class="prop-list">
              <li>
                <label>TokenVault ID (PanToken)</label>
                <div class="d-flex align-items-center gap-2">
                  <span class="code-block">{{card.panToken}}</span>
                  <button class="icon-btn mb-0 mt-0"><span class="material-symbols-rounded" style="font-size:16px">content_copy</span></button>
                </div>
              </li>
              <li>
                <label>BIN o IIN Emisor</label>
                <span>{{card.bin}}</span>
              </li>
              <li>
                <label>Últimos 4 Dígitos (Last4)</label>
                <span>{{card.last4}}</span>
              </li>
              <li>
                <label>Estado Interno (Core)</label>
                <strong>{{getStatusName(card.status)}}</strong>
              </li>
              <li>
                <label>Acciones de Seguridad Avanzada (PCI)</label>
                <div class="d-flex gap-2 mt-2">
                  <button class="btn btn-primary btn-sm" (click)="openPinModal()">
                    <span class="material-symbols-rounded" style="font-size:18px">password</span>
                    Cambiar PIN
                  </button>
                  <button class="btn btn-outline btn-sm" (click)="replaceCard()">Reposición de Plástico</button>
                </div>
              </li>
             </ul>
          </div>
        </div>
      </div>

      <!-- PIN Modal -->
      <div class="modal-overlay" *ngIf="isPinModalOpen">
         <div class="card p-4" style="max-width: 400px; width: 100%;">
            <h3 class="mb-4 d-flex align-items-center gap-2">
               <span class="material-symbols-rounded text-primary">security</span>
               Gestión de PIN (v58)
            </h3>
            <div class="alert alert-warning mb-3 py-2 px-3">
               El PIN será hasheado (SHA256) y almacenado de forma segura siguiendo normas PCI.
            </div>
            <div class="mb-4">
               <label>Nuevo PIN (4 dígitos)</label>
               <input class="form-control text-center text-main" style="font-size: 24px; letter-spacing: 12px;" type="password" maxlength="4" [(ngModel)]="newPin" placeholder="****">
            </div>
            <div class="d-flex justify-content-end gap-2">
               <button class="btn btn-outline" (click)="isPinModalOpen = false">Cancelar</button>
               <button class="btn btn-primary" [disabled]="newPin.length < 4" (click)="savePin()">Establecer PIN</button>
            </div>
         </div>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .breadcrumb a { font-size: 0.875rem; transition: color 0.2s; font-weight: 500; }
    .breadcrumb a:hover { color: var(--primary); text-decoration: none; }
    
    .status-badge { padding: 0.25rem 0.6rem; border-radius: 1rem; font-size: 0.7rem; font-weight: 600; display: inline-flex; align-items: center; gap: 0.25rem; }
    .status-badge::before { content: ''; width: 6px; height: 6px; border-radius: 50%; }
    .status-active { background: #ecfdf5; color: #047857; }
    .status-active::before { background: #10b981; }
    .status-blocked { background: #fef2f2; color: #b91c1c; }
    .status-blocked::before { background: #ef4444; }
    .status-created { background: #eff6ff; color: #1e3a8a; }
    .status-created::before { background: #3b82f6; }

    .header-actions { display: flex; gap: 1rem; }
    .btn-sm { padding: 0.35rem 0.8rem; font-size: 0.75rem; display: inline-flex; align-items: center;}
    
    .alert { padding: 1rem; border-radius: var(--radius-md); font-size: 0.875rem; }
    .alert-info { background-color: #f0fdf4; border: 1px solid #bbf7d0; color: #166534; }
    
    /* the credit card */
    .visual-card-wrapper {
      position: relative; width: 340px; height: 215px; flex-shrink: 0;
      border-radius: var(--radius-lg); box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04);
      transition: filter 0.3s;
    }
    .visual-card-wrapper.blocked { filter: grayscale(100%); }
    .plastic-card {
      width: 100%; height: 100%; border-radius: var(--radius-lg);
      background: linear-gradient(135deg, #1e3c72 0%, #2a5298 100%);
      color: white; padding: 1.5rem; display: flex; flex-direction: column; justify-content: flex-end;
      position: relative; overflow: hidden;
    }
    .plastic-card::before {
       content: ''; position: absolute; top: -50px; right: -50px; 
       width: 200px; height: 200px; background: rgba(255,255,255,0.05); border-radius: 50%;
    }
    .chip {
      width: 45px; height: 35px; background: rgba(255, 215, 0, 0.7); 
      border-radius: 4px; position: absolute; top: 1.5rem; left: 1.5rem;
      border: 1px solid rgba(255, 215, 0, 0.8);
    }
    .network-logo {
      width: 50px; height: 30px; background: #fff; border-radius: 4px; opacity: 0.8;
      position: absolute; bottom: 1.5rem; right: 1.5rem;
    }
    .pan { font-size: 1.35rem; font-family: monospace; letter-spacing: 2px; margin-top: 2rem; position: relative; z-index: 2; text-shadow: 1px 1px 2px rgba(0,0,0,0.5);}
    .last4, .exp { font-family: monospace; font-size: 0.9rem; position: relative; z-index: 2; text-shadow: 1px 1px 2px rgba(0,0,0,0.5);}
    
    .status-overlay {
       position: absolute; top: 0; left: 0; right: 0; bottom: 0; 
       background: rgba(0,0,0,0.4); border-radius: var(--radius-lg); 
       display: flex; flex-direction: column; align-items: center; justify-content: center;
       color: white; font-weight: 700; font-size: 1.25rem; z-index: 10;
       border: 3px solid var(--danger);
    }
    .warning-icon { font-size: 48px; margin-bottom: 0.5rem; text-shadow: 0 0 10px rgba(255,0,0,0.8);}

    .prop-list { list-style: none; padding: 0; margin: 0; }
    .prop-list li { padding: 1rem 0; border-bottom: 1px solid var(--border-color); display: flex; flex-direction: column;}
    .prop-list li:last-child { border-bottom: none; }
    .prop-list label { font-size: 0.75rem; text-transform: uppercase; color: var(--text-muted); font-weight: 600; margin-bottom: 0.25rem; }
    .code-block { background: var(--bg-main); padding: 0.25rem 0.5rem; border-radius: 4px; font-family: monospace; }
    .icon-btn { background: none; border: none; cursor: pointer; color: var(--text-muted); padding: 0; }
    .icon-btn:hover { color: var(--primary); }
  `]
})
export class CardDetailComponent implements OnInit {
  private cardService = inject(CardService);
  private customerService = inject(CustomerService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private notifications = inject(NotificationService);

  card: Card | null = null;
  enumStatus = CardStatus;

  isPinModalOpen = false;
  newPin = '';

  openPinModal() {
    this.isPinModalOpen = true;
    this.newPin = '';
  }

  savePin() {
    if (!this.card || this.newPin.length < 4) return;
    this.cardService.setPin(this.card.id, this.newPin).subscribe({
      next: () => {
        this.isPinModalOpen = false;
        this.notifications.success('PIN establecido correctamente.');
      },
      error: (err) => {
        console.error('Error setting PIN:', err?.status, err?.message);
        this.notifications.error('Error al establecer el PIN.');
      }
    });
  }

  ngOnInit() {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.cardService.getCard(id).pipe(
        catchError(err => {
          console.error('Error loading card:', err?.status, err?.message);
          this.notifications.error('No se pudo cargar la información de la tarjeta');
          this.goBack();
          return of(null);
        }),
        switchMap(card => {
          if (!card) return of(null);
          return this.customerService.getCustomers().pipe(
            catchError(() => {
                this.notifications.warning('No se pudo obtener el nombre del cliente');
                return of([]);
            }),
            map(customers => {
              const customer = customers.find(c => c.accounts && c.accounts.some(a => a.id === card.accountId));
              card.customerName = customer ? customer.fullName : 'Cliente Desconocido';
              return card;
            })
          );
        })
      ).subscribe(c => { if (c) this.card = c; });
    }
  }

  goBack() {
    this.router.navigate(['/app/issuer/cards']);
  }

  getStatusClass(status: CardStatus): string {
    switch (status) {
      case CardStatus.Active: return 'status-active';
      case CardStatus.Blocked: case CardStatus.Cancelled: return 'status-blocked';
      default: return 'status-created';
    }
  }

  getStatusName(status: CardStatus): string {
    return CardStatus[status] || 'Desconocido';
  }

  toggleBlock() {
    if (!this.card) return;
    if (this.card.status === CardStatus.Active) {
      this.cardService.blockCard(this.card.id).subscribe({
        next: () => { 
            this.card!.status = CardStatus.Blocked; 
            this.notifications.success('Tarjeta bloqueada temporalmente');
        },
        error: (err) => { 
            console.error('Error blocking card:', err?.status); 
            this.notifications.error('Error al intentar bloquear la tarjeta');
        }
      });
    } else {
      this.cardService.unblockCard(this.card.id).subscribe({
        next: () => { 
            this.card!.status = CardStatus.Active; 
            this.notifications.success('Tarjeta reactivada correctamente');
        },
        error: (err) => { 
            console.error('Error unblocking card:', err?.status); 
            this.notifications.error('Error al intentar desbloquear la tarjeta');
        }
      });
    }
  }

  cancelCard() {
    if (!this.card) return;
    if (confirm('¿Está seguro de CANCELAR este plástico de forma irreversible?')) {
      this.cardService.cancelCard(this.card.id).subscribe({
        next: () => { 
            this.card!.status = CardStatus.Cancelled; 
            this.notifications.success('Tarjeta cancelada definitivamente');
        },
        error: (err) => { 
            console.error('Error cancelling card:', err?.status); 
            this.notifications.error('Error al intentar cancelar la tarjeta');
        }
      });
    }
  }

  replaceCard() {
    if (!this.card) return;
    if (confirm('¿Está seguro de solicitar la REPOSICIÓN de este plástico? La tarjeta actual quedará cancelada.')) {
      this.cardService.replaceCard(this.card.id).subscribe({
        next: (newCard) => {
          this.notifications.success('Reposición solicitada. Nueva tarjeta emitida.');
          this.router.navigate([`/app/issuer/cards/${newCard.newCardId}`]);
        },
        error: (err) => {
          console.error('Error replacing card:', err?.status);
          this.notifications.error('Error al solicitar la reposición de la tarjeta');
        }
      });
    }
  }
}
