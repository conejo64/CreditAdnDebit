import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SwitchService } from './switch.service';
import { CardService } from '../issuer/cards/card.service';
import { CustomerService } from '../issuer/customers/customer.service';
import { NotificationService } from '../../core/notification.service';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

@Component({
   selector: 'app-simulator',
   standalone: true,
   imports: [CommonModule, FormsModule],
   template: `
    <div class="page-container">
      <div class="page-header mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">rocket_launch</span>
            Simulador de Canales (ATM / POS)
          </h1>
          <p class="text-muted mt-1">Inyecte mensajería ISO8583 virtual hacia el Switch para validar reglas de ruteo y autorizaciones en la bóveda de saldo.</p>
        </div>
      </div>

      <div class="row">
         <div class="col-6">
            <div class="card p-0 h-100">
              <div class="card-header border-bottom p-3 bg-light">
                 <h3 class="m-0 text-main">Construir Transacción</h3>
              </div>
              <div class="p-4">
                 
                  <div class="input-group mb-4">
                    <label class="font-weight-600">Origen de Transacción</label>
                    <div class="d-flex gap-2">
                       <button class="btn flex-grow-1" [ngClass]="source === 'POS' ? 'btn-primary' : 'btn-outline'" (click)="onSourceChange('POS')">
                          <span class="material-symbols-rounded" style="font-size:18px">point_of_sale</span> Red POS (Compra)
                       </button>
                       <button class="btn flex-grow-1" [ngClass]="source === 'ATM' ? 'btn-primary' : 'btn-outline'" (click)="onSourceChange('ATM')">
                          <span class="material-symbols-rounded" style="font-size:18px">atm</span> Cajero ATM (Retiro)
                       </button>
                    </div>
                  </div>

                  <div class="input-group mb-3" *ngIf="source === 'POS'">
                    <label>Seleccionar Tarjeta (Simulada)</label>
                    <select class="form-control" [(ngModel)]="selectedCardId" (ngModelChange)="onCardSelect($event)">
                       <option value="">Entrada Manual / Otra Tarjeta...</option>
                       <option *ngFor="let c of cards" [value]="c.id">{{c.maskedPan}} ({{c.customerName}})</option>
                    </select>
                  </div>

                  <div class="row g-2 mb-3">
                    <div class="col-4">
                       <label>BIN (DE 1-6)</label>
                       <input type="number" class="form-control font-monospace" [(ngModel)]="bin" placeholder="411111">
                    </div>
                    <div class="col-8">
                       <label>PAN Completo o Token</label>
                       <input type="text" class="form-control font-monospace text-primary" [(ngModel)]="panToken" placeholder="4111 11** **** 1111">
                    </div>
                  </div>

                  <div class="input-group mb-3" *ngIf="source === 'ATM'">
                    <label>PIN de Tarjeta (DE 52 - PinBlock)</label>
                    <input type="password" maxlength="4" class="form-control text-center font-monospace" style="letter-spacing: 12px; font-size: 20px;" [(ngModel)]="pin" placeholder="****">
                    <small class="text-muted mt-1">El simulador enviará este valor en el PinBlock ISO8583.</small>
                  </div>

                  <div class="d-flex gap-3">
                    <div class="input-group flex-grow-1">
                       <label>Monto de la Operación ($)</label>
                       <input type="number" class="form-control font-size-lg font-weight-700" [(ngModel)]="amount" placeholder="0.00">
                    </div>
                    <div class="input-group flex-grow-1">
                       <label>Código de Procesamiento (DE 3)</label>
                       <select class="form-control" [(ngModel)]="procCode">
                          <option value="000000">000000 - Compra Bienes</option>
                          <option value="010000">010000 - Retiro Efectivo</option>
                          <option value="200000">200000 - Reversa/Devolución</option>
                          <option value="310000">310000 - Consulta Saldo</option>
                       </select>
                    </div>
                  </div>
                 
                  <div class="input-group">
                     <label>MTI Base (Message Type Indicator)</label>
                     <input type="text" class="form-control bg-light" [value]="source === 'POS' ? '0100 (Authorization)' : '0200 (Financial Request)'" disabled>
                  </div>

                  <button class="btn btn-primary w-100 mt-4 d-flex align-items-center justify-content-center gap-2 p-3 font-size-lg" (click)="sendSimulation()">
                     <span class="material-symbols-rounded">send_time_extension</span> Enviar al Switch
                  </button>
              </div>
            </div>
         </div>

         <!-- Response Terminal -->
         <div class="col-6">
            <div class="card bg-dark text-white p-0 h-100 console-view d-flex flex-column">
              <div class="card-header border-bottom border-secondary p-3 d-flex justify-content-between align-items-center">
                 <h3 class="m-0 text-primary-light d-flex align-items-center gap-2">
                   <span class="material-symbols-rounded">terminal</span> Respuesta (Host)
                 </h3>
                 <span class="badge" [ngClass]="isSending ? 'bg-warning text-dark' : 'bg-success'" *ngIf="terminalLog.length > 0">
                    {{ isSending ? 'ENRUTANDO...' : 'CANAL LIBRE' }}
                 </span>
              </div>
              
              <div class="p-3 flex-grow-1 overflow-auto font-monospace text-sm" id="terminalLogArea" style="max-height: 480px">
                 <div *ngIf="terminalLog.length === 0" class="text-muted d-flex flex-column justify-content-center align-items-center h-100">
                     <span class="material-symbols-rounded mb-2" style="font-size:48px; opacity:0.3">settings_ethernet</span>
                     Esperando inyección de socket virtual...
                 </div>
                 <div *ngFor="let log of terminalLog" class="mb-2 d-flex justify-content-between align-items-center">
                    <span [innerHTML]="log.html"></span>
                    <button *ngIf="log.canReverse" class="btn btn-outline btn-sm py-0 px-2" style="font-size: 10px; color: #f87171; border-color: #444;" (click)="sendReversal(log.data)">
                       REVERSAR
                    </button>
                 </div>
              </div>
              
              <div class="p-2 border-top border-secondary text-right">
                  <button class="btn btn-outline btn-sm text-white border-secondary" (click)="terminalLog = []">Limpiar</button>
              </div>
            </div>
         </div>
      </div>
    </div>
  `,
   styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .bg-light { background-color: #f8fafc; }
    .font-size-lg { font-size: 1.25rem !important; }
    
    .row { display: flex; gap: 1.5rem; }
    .col-6 { flex: 0 0 calc(50% - 0.75rem); }
    
    .font-monospace { font-family: 'Courier New', Courier, monospace; }
    .text-sm { font-size: 0.8rem; }
    
    /* Console Specific styles */
    .console-view { background-color: #1e1e1e !important; color: #d4d4d4 !important; border: 1px solid #333; border-radius: var(--radius-lg);}
    .console-view h3 { color: #d4d4d4; }
    .bg-secondary { background-color: #2d2d2d !important; }
    .border-secondary { border-color: #333 !important;}
    .text-primary-light { color: #569cd6 !important;}
    .text-success-light { color: #4ade80 !important;}
    .text-danger-light { color: #f87171 !important;}
    
    .btn-outline.text-white:hover { background-color: rgba(255,255,255,0.1); }
  `]
})
export class SimulatorComponent {
   private switchService = inject(SwitchService);
   private cardService = inject(CardService);
   private customerService = inject(CustomerService);
   private notifications = inject(NotificationService);

   source: 'POS' | 'ATM' = 'POS';
   panToken: string = '';
   bin: number = 411111;
   pin: string = '';
   amount: number | null = 10.00;
   procCode: string = '000000';

   cards: any[] = [];
   selectedCardId: string = '';

   isSending: boolean = false;
   terminalLog: any[] = [];

   ngOnInit() {
      this.loadInitialData();
   }

   loadInitialData() {
      forkJoin({
         cards: this.cardService.getCards().pipe(
            catchError(() => {
               this.notifications.warning('No se pudieron cargar las tarjetas reales');
               return of([]);
            })
         ),
         customers: this.customerService.getCustomers().pipe(
            catchError(() => {
               this.notifications.error('Error de conexión con el servicio de clientes');
               return of([]);
            })
         )
      }).subscribe(({ cards, customers }) => {
         cards.forEach((card: any) => {
            const customer = customers.find((c: any) => c.accounts && c.accounts.some((a: any) => a.id === card.accountId));
            card.customerName = customer ? customer.fullName : 'Cliente Desconocido';
         });
         this.cards = cards;
      });
   }

   onSourceChange(src: 'POS' | 'ATM') {
      this.source = src;
      this.procCode = src === 'POS' ? '000000' : '010000';
   }

   onCardSelect(cardId: string) {
      const card = this.cards.find(c => c.id === cardId);
      if (card) {
         this.panToken = card.panToken;
         this.bin = parseInt(card.bin);
      }
   }

   appendLog(msg: string, colorClass: string = '', data: any = null) {
      const time = new Date().toLocaleTimeString();
      const wrap = colorClass ? `<span class="${colorClass}">${msg}</span>` : msg;
      const html = `<span class="text-muted">[${time}]</span> ${wrap}`;
      this.terminalLog.push({ html, canReverse: !!data, data });

      setTimeout(() => {
         const t = document.getElementById('terminalLogArea');
         if (t) t.scrollTop = t.scrollHeight;
      }, 50);
   }

   sendReversal(original: any) {
      if (!original) return;
      this.isSending = true;
      this.appendLog(`!!! Iniciando REVERSO (0420) para TraceId: ${original.traceId}`, 'text-danger-light');
      
      const reversalPayload = {
         originalTraceId: original.traceId,
         merchantId: original.merchantId,
         terminalId: original.terminalId,
         currency: original.currency,
         pinBlock: original.pinBlock,
         emvTlv: original.emvTlv
      };

      this.switchService.simulateReversal(reversalPayload).subscribe({
         next: (res: any) => {
            this.isSending = false;
            this.appendLog(`<<< RESP_MTI: 0430 (Reverso Exitoso)`, 'text-success-light');
            this.appendLog(`<<< RC: ${res.reversalResponseCode} | Status: ${res.status}`, 'text-main');
         },
         error: (err: any) => {
            this.isSending = false;
            this.appendLog(`!!! Error en reverso: ${err.message}`, 'text-danger-light');
         }
      });
   }

   sendSimulation() {
      if (!this.panToken || !this.amount || !this.bin) {
        this.notifications.warning('Falta BIN, PAN/Token o Monto');
        return;
      }

      this.isSending = true;
      const mti = this.source === 'POS' ? '0100' : '0200';
      const traceId = Math.random().toString(36).substring(2, 12).toUpperCase();
      const stan = Math.floor(100000 + Math.random() * 900000).toString();

      const payload = {
         traceId: traceId,
         bin: this.bin,
         amount: this.amount,
         currency: '840',
         merchantId: 'MERCHDEMO001',
         terminalId: 'TERM0001',
         stan: stan,
         pan: this.panToken.startsWith('TPAN_') ? null : this.panToken,
         tokenPan: this.panToken.startsWith('TPAN_') ? this.panToken : null,
         expiryYyMm: '2912',
         cardId: this.selectedCardId || null,
         pinBlock: this.pin || null
      };

      this.appendLog(`>>> Inyectando Socket Virtual ISO ${mti} [STAN: ${stan}]`, 'text-primary-light');
      this.appendLog(`>>> Payload: DE2=${this.panToken}, DE4=${this.amount}, DE11=${stan}`, 'text-muted');

      this.switchService.simulateAuthorize(payload).subscribe({
         next: (res: any) => {
            this.isSending = false;
            this.appendLog(`<<< RESP_MTI: ${parseInt(mti) + 10} [TRANS: ${res.traceId}]`);
            if (res.responseCode === '00') {
               this.appendLog(`<<< DE39 Response Code: ${res.responseCode} (${res.decision})`, 'text-success-light', payload);
               this.appendLog(`<<< DE38 Auth Code: ${Math.floor(100000 + Math.random() * 900000)}`);
               this.appendLog(`<<< Enrutado vía: ${res.connectorId || 'SIMULATOR'}`, 'text-main');
            } else {
               this.appendLog(`<<< DE39 Response Code: ${res.responseCode} (${res.decision})`, 'text-danger-light');
            }
            this.appendLog(`==> Transacción cerrada.`, 'text-muted');
         },
         error: (err: any) => {
            this.isSending = false;
            this.appendLog(`!!! Error crítico en socket: ${err.message || 'Timeout/Connection Refused'}`, 'text-danger-light');
            this.appendLog(`==> Verifique que IsoSwitch esté disponible y reinténtelo.`, 'text-muted');
         }
      });
   }
}
