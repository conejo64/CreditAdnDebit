import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { WalletsService, WalletToken, WalletEnrollment } from './wallets.service';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';
import { NotificationService } from '../../core/notification.service';

type Tab = 'tokens' | 'enroll' | 'authorize';

@Component({
  selector: 'app-wallets-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-container">
      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">wallet</span>
            Billeteras Digitales
          </h1>
          <p class="text-muted mt-1">Registro de tokens NFC, activación y autorización de pagos digitales.</p>
        </div>
      </div>

      <div class="tab-bar mb-4">
        <button class="tab-btn" [class.active]="activeTab === 'tokens'" (click)="setTab('tokens')">
          <span class="material-symbols-rounded">nfc</span> Consultar Tokens
        </button>
        <button class="tab-btn" [class.active]="activeTab === 'enroll'" (click)="setTab('enroll')">
          <span class="material-symbols-rounded">phonelink_ring</span> Nuevo Enrolamiento
        </button>
        <button class="tab-btn" [class.active]="activeTab === 'authorize'" (click)="setTab('authorize')">
          <span class="material-symbols-rounded">contactless</span> Probar Autorización
        </button>
      </div>

      <!-- Tokens Tab -->
      <div *ngIf="activeTab === 'tokens'">
        <div class="card lookup-card mb-4">
          <div class="d-flex gap-2 align-items-end">
            <div class="form-group flex-1">
              <label class="form-label">ID de Tarjeta (UUID)</label>
              <input class="form-input" [(ngModel)]="lookupCardId" placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" />
            </div>
            <button class="btn btn-primary" (click)="lookupTokens()" [disabled]="!lookupCardId || loadingTokens">
              <span class="material-symbols-rounded">search</span>
            </button>
          </div>
        </div>

        <div class="tokens-grid" *ngIf="tokens.length">
          <div class="token-card" *ngFor="let token of tokens">
            <div class="token-header">
              <div class="wallet-logo" [class]="'wallet-' + token.walletProvider.toLowerCase()">
                {{ token.walletProvider }}
              </div>
              <span class="token-status-badge" [class.active]="token.status === 'Active'" [class.pending]="token.status === 'Pending'" [class.suspended]="token.status === 'Suspended'">
                {{ token.status }}
              </span>
            </div>
            <div class="token-pan">{{ token.maskedPan }}</div>
            <div class="token-meta">
              <span class="text-muted text-xs">ID: {{ token.id | slice:0:8 }}...</span>
              <span class="text-muted text-xs" *ngIf="token.activatedAt">Activado: {{ token.activatedAt | date:'mediumDate' }}</span>
            </div>
          </div>
        </div>

        <div class="card" *ngIf="!tokens.length && !loadingTokens">
          <div class="empty-state">
            <span class="material-symbols-rounded" style="font-size: 48px; opacity: 0.3">nfc</span>
            <p class="mt-2">{{ lookupCardId ? 'No hay tokens wallet para esta tarjeta.' : 'Ingresá el ID de tarjeta y hacé la búsqueda.' }}</p>
          </div>
        </div>
      </div>

      <!-- Enroll Tab -->
      <div *ngIf="activeTab === 'enroll'">
        <div class="enroll-panel">
          <div class="card enroll-form">
            <h3 class="mb-1">Registrar Billetera Digital</h3>
            <p class="text-muted mb-4" style="font-size: 0.875rem">Inicia el proceso de tokenización para un dispositivo NFC o wallet móvil.</p>
            <div class="form-group mb-3">
              <label class="form-label">ID de Tarjeta</label>
              <input class="form-input" [(ngModel)]="enrollForm.cardId" placeholder="UUID de la tarjeta" />
            </div>
            <div class="form-group mb-3">
              <label class="form-label">Proveedor de Billetera</label>
              <select class="form-input" [(ngModel)]="enrollForm.walletProvider">
                <option value="ApplePay">Apple Pay</option>
                <option value="GooglePay">Google Pay</option>
                <option value="SamsungPay">Samsung Pay</option>
                <option value="VisaToken">Visa Token Service</option>
              </select>
            </div>
            <div class="form-group mb-4">
              <label class="form-label">Device ID</label>
              <input class="form-input" [(ngModel)]="enrollForm.deviceId" placeholder="ID único del dispositivo" />
            </div>
            <button class="btn btn-primary w-full" (click)="doEnroll()" [disabled]="!enrollForm.cardId || !enrollForm.deviceId || enrolling">
              {{ enrolling ? 'Registrando...' : 'Iniciar Enrolamiento' }}
            </button>
          </div>

          <div class="card enroll-result" *ngIf="enrollment">
            <div class="result-icon success"><span class="material-symbols-rounded" style="font-size: 2rem">check_circle</span></div>
            <h3 class="mt-3 mb-1">Enrolamiento Iniciado</h3>
            <p class="text-muted" style="font-size: 0.875rem">El siguiente paso es la activación con OTP del usuario.</p>
            <div class="result-details mt-3">
              <div class="result-row"><span>Token ID</span><span class="font-mono text-xs">{{ enrollment.id }}</span></div>
              <div class="result-row"><span>Tarjeta</span><span class="font-mono text-xs">{{ enrollment.cardId }}</span></div>
              <div class="result-row"><span>Proveedor</span><span>{{ enrollment.walletProvider }}</span></div>
              <div class="result-row"><span>Estado</span><span class="font-weight-600">{{ enrollment.status }}</span></div>
            </div>
            <div class="mt-4 p-3" style="background: #fef3c7; border-radius: 8px; border-left: 3px solid #f59e0b;">
              <p style="margin: 0; font-size: 0.8rem; color: #92400e;">
                <strong>Próximo paso:</strong> El cliente debe activar el token con OTP en la solapa "Consultar Tokens" usando el ID: <strong>{{ enrollment.id | slice:0:8 }}...</strong>
              </p>
            </div>
          </div>
          <div class="card enroll-result placeholder-card" *ngIf="!enrollment && !enrolling">
            <div class="empty-state">
              <span class="material-symbols-rounded" style="font-size: 64px; opacity: 0.2">phonelink_ring</span>
              <p class="mt-2">Completá el formulario para iniciar el registro de una billetera digital.</p>
            </div>
          </div>
        </div>
      </div>

      <!-- Authorize Tab -->
      <div *ngIf="activeTab === 'authorize'">
        <div class="enroll-panel">
          <div class="card enroll-form">
            <h3 class="mb-1">Probar Autorización de Pago</h3>
            <p class="text-muted mb-4" style="font-size: 0.875rem">Simula una autorización de pago digital a través de un token de billetera activo.</p>
            <div class="form-group mb-3">
              <label class="form-label">Wallet Token ID</label>
              <input class="form-input" [(ngModel)]="authForm.walletTokenId" placeholder="UUID del token" />
            </div>
            <div class="form-group mb-3">
              <label class="form-label">Monto</label>
              <input class="form-input" type="number" [(ngModel)]="authForm.amount" min="0.01" step="0.01" />
            </div>
            <div class="form-group mb-3">
              <label class="form-label">Moneda</label>
              <select class="form-input" [(ngModel)]="authForm.currency">
                <option value="USD">USD</option>
                <option value="CRC">CRC</option>
                <option value="MXN">MXN</option>
                <option value="EUR">EUR</option>
              </select>
            </div>
            <div class="form-group mb-4">
              <label class="form-label">Merchant ID</label>
              <input class="form-input" [(ngModel)]="authForm.merchantId" placeholder="ID del comercio" />
            </div>
            <button class="btn btn-primary w-full" (click)="doAuthorize()" [disabled]="!authForm.walletTokenId || !authForm.merchantId || authorizing">
              {{ authorizing ? 'Autorizando...' : 'Enviar Autorización' }}
            </button>
          </div>

          <div class="card enroll-result" *ngIf="authorization">
            <div class="result-icon" [class.success]="authorization.status === 'Approved'" [class.danger]="authorization.status !== 'Approved'">
              <span class="material-symbols-rounded" style="font-size: 2rem">{{ authorization.status === 'Approved' ? 'check_circle' : 'cancel' }}</span>
            </div>
            <h3 class="mt-3 mb-1">{{ authorization.status === 'Approved' ? 'Pago Autorizado' : 'Pago Rechazado' }}</h3>
            <div class="result-details mt-3">
              <div class="result-row"><span>ID Autorización</span><span class="font-mono text-xs">{{ authorization.id }}</span></div>
              <div class="result-row"><span>Monto</span><span class="font-weight-600">{{ authorization.amount | currency:authorization.currency }}</span></div>
              <div class="result-row"><span>Estado</span><span class="font-weight-600" [class.text-success]="authorization.status === 'Approved'" [class.text-danger]="authorization.status !== 'Approved'">{{ authorization.status }}</span></div>
              <div class="result-row"><span>Fecha</span><span>{{ authorization.authorizedAt | date:'short' }}</span></div>
            </div>
          </div>
          <div class="card enroll-result placeholder-card" *ngIf="!authorization && !authorizing">
            <div class="empty-state">
              <span class="material-symbols-rounded" style="font-size: 64px; opacity: 0.2">contactless</span>
              <p class="mt-2">Completá el formulario para simular un pago digital.</p>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .text-muted { color: var(--text-muted); }
    .font-weight-600 { font-weight: 600; }
    .font-mono { font-family: 'Roboto Mono', monospace; }
    .text-xs { font-size: 0.75rem; }
    .text-success { color: #047857; }
    .text-danger { color: #b91c1c; }
    .w-full { width: 100%; }
    .flex-1 { flex: 1; }

    .tab-bar { display: flex; gap: 0.5rem; border-bottom: 2px solid var(--border-color); }
    .tab-btn { display: flex; align-items: center; gap: 0.5rem; padding: 0.75rem 1.25rem; border: none; background: none; color: var(--text-muted); font-size: 0.875rem; font-weight: 500; cursor: pointer; border-bottom: 2px solid transparent; margin-bottom: -2px; transition: all 0.2s; }
    .tab-btn.active { color: var(--primary); border-bottom-color: var(--primary); }
    .tab-btn:hover:not(.active) { color: var(--text-main); }

    .lookup-card { padding: 1.25rem; }

    .tokens-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 1rem; }
    .token-card { background: var(--bg-card); border: 1px solid var(--border-color); border-radius: var(--radius-md); padding: 1.25rem; transition: box-shadow 0.2s; }
    .token-card:hover { box-shadow: 0 4px 16px rgba(0,0,0,0.08); }
    .token-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
    .wallet-logo { font-size: 0.75rem; font-weight: 800; padding: 0.3rem 0.6rem; border-radius: 6px; text-transform: uppercase; letter-spacing: 0.5px; }
    .wallet-applepay { background: #000; color: white; }
    .wallet-googlepay { background: #4285f4; color: white; }
    .wallet-samsungpay { background: #1428a0; color: white; }
    .wallet-visatoken { background: #1a1f71; color: white; }
    .token-status-badge { font-size: 0.7rem; font-weight: 700; padding: 0.15rem 0.5rem; border-radius: 4px; }
    .token-status-badge.active { background: #d1fae5; color: #047857; }
    .token-status-badge.pending { background: #fef3c7; color: #92400e; }
    .token-status-badge.suspended { background: #fee2e2; color: #b91c1c; }
    .token-pan { font-family: 'Roboto Mono', monospace; font-size: 1.1rem; font-weight: 600; letter-spacing: 2px; margin-bottom: 0.75rem; }
    .token-meta { display: flex; flex-direction: column; gap: 0.2rem; }

    .enroll-panel { display: grid; grid-template-columns: 380px 1fr; gap: 1.5rem; align-items: start; }
    @media (max-width: 900px) { .enroll-panel { grid-template-columns: 1fr; } }
    .enroll-form { padding: 1.75rem; }
    .enroll-result { padding: 2rem; text-align: center; }
    .placeholder-card { min-height: 300px; display: flex; align-items: center; justify-content: center; }
    .result-icon { width: 64px; height: 64px; border-radius: 50%; display: flex; align-items: center; justify-content: center; margin: 0 auto; }
    .result-icon.success { background: #d1fae5; color: #047857; }
    .result-icon.danger { background: #fee2e2; color: #b91c1c; }
    .result-details { text-align: left; border-top: 1px solid var(--border-color); }
    .result-row { display: flex; justify-content: space-between; padding: 0.625rem 0; border-bottom: 1px solid var(--border-color); font-size: 0.875rem; }
    .result-row span:first-child { color: var(--text-muted); }

    .empty-state { text-align: center; padding: 3rem; color: var(--text-muted); }

    .form-group { display: flex; flex-direction: column; gap: 0.4rem; }
    .form-label { font-size: 0.8rem; font-weight: 600; color: var(--text-muted); }
    .form-input { padding: 0.625rem 0.875rem; border: 1px solid var(--border-color); border-radius: var(--radius-sm); font-size: 0.875rem; width: 100%; background: white; transition: border-color 0.2s; }
    .form-input:focus { outline: none; border-color: var(--primary); }
  `]
})
export class WalletsListComponent implements OnInit {
  private walletsService = inject(WalletsService);
  private notifications = inject(NotificationService);

  activeTab: Tab = 'tokens';
  tokens: WalletToken[] = [];
  enrollment: WalletEnrollment | null = null;
  authorization: any = null;
  loadingTokens = false;
  enrolling = false;
  authorizing = false;
  lookupCardId = '';

  enrollForm = { cardId: '', walletProvider: 'ApplePay', deviceId: '' };
  authForm = { walletTokenId: '', amount: 10, currency: 'USD', merchantId: '' };

  ngOnInit() {}

  setTab(tab: Tab) { this.activeTab = tab; }

  lookupTokens() {
    if (!this.lookupCardId.trim()) return;
    this.loadingTokens = true;
    this.tokens = [];
    this.walletsService.getByCard(this.lookupCardId.trim()).pipe(
      catchError(() => {
        this.notifications.warning('No se encontraron tokens para esta tarjeta');
        return of([] as WalletToken[]);
      })
    ).subscribe(data => { this.tokens = data; this.loadingTokens = false; });
  }

  doEnroll() {
    this.enrolling = true;
    this.enrollment = null;
    this.walletsService.register(this.enrollForm).subscribe({
      next: result => { 
        this.enrollment = result; 
        this.enrolling = false; 
        this.notifications.success('Enrolamiento de billetera iniciado');
      },
      error: err => { 
        this.enrolling = false; 
        console.error(err); 
        this.notifications.error('Error al registrar billetera. Verifique los datos.'); 
      }
    });
  }

  doAuthorize() {
    this.authorizing = true;
    this.authorization = null;
    this.walletsService.authorize(this.authForm).subscribe({
      next: result => { 
        this.authorization = result; 
        this.authorizing = false; 
        if (result.status === 'Approved') {
            this.notifications.success('Pago autorizado correctamente');
        } else {
            this.notifications.warning('El pago fue rechazado por el emisor');
        }
      },
      error: err => { 
        this.authorizing = false; 
        console.error(err); 
        this.notifications.error('Error de red al autorizar el pago.'); 
      }
    });
  }
}
