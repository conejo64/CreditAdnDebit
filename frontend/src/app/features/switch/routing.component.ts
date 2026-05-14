import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';

interface RoutingRule {
    id: string;
    binPattern: string;
    sourceNetwork: string;
    destinationNetwork: string;
    priority: number;
    isActive: boolean;
    action: 'Forward' | 'Decline' | 'LocalAuth';
}

@Component({
    selector: 'app-routing',
    standalone: true,
    imports: [CommonModule],
    template: `
    <div class="page-container">
      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">router</span>
            Reglas de Ruteo BIN / Tráfico
          </h1>
          <p class="text-muted mt-1">Configuración del motor de decisiones y enrutamiento (IsoSwitch Network Rules).</p>
        </div>
        <button class="btn btn-primary">
          <span class="material-symbols-rounded">add</span> Nueva Regla
        </button>
      </div>
      
      <div class="card p-0">
        <div class="table-responsive">
          <table class="table">
            <thead>
              <tr>
                <th>PATRÓN BIN</th>
                <th>RED ORIGEN</th>
                <th>TIPO DE ACCIÓN</th>
                <th>CONECTOR DESTINO</th>
                <th>PRIORIDAD</th>
                <th>ESTADO</th>
                <th class="text-right">OPCIONES</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let rule of rules">
                <td class="font-monospace text-primary-dark font-weight-600">{{rule.binPattern}}</td>
                <td>
                  <span class="badge" [ngClass]="getNetworkClass(rule.sourceNetwork)">{{rule.sourceNetwork}}</span>
                </td>
                <td>
                   <div class="d-flex align-items-center gap-1">
                      <span class="material-symbols-rounded text-sm" 
                            [ngClass]="{'text-success': rule.action === 'Forward', 'text-warning': rule.action === 'LocalAuth', 'text-danger': rule.action === 'Decline'}">
                         {{ rule.action === 'Forward' ? 'fast_forward' : (rule.action === 'LocalAuth' ? 'dns' : 'block') }}
                      </span>
                      {{rule.action}}
                   </div>
                </td>
                <td class="text-muted font-weight-500">{{rule.destinationNetwork}}</td>
                <td class="text-center">{{rule.priority}}</td>
                <td>
                  <span class="badge" [ngClass]="rule.isActive ? 'badge-success' : 'badge-danger'">{{rule.isActive ? 'ACTIVO' : 'INACTIVO'}}</span>
                </td>
                <td class="text-right">
                  <button class="btn btn-outline btn-sm me-2"><span class="material-symbols-rounded" style="font-size: 16px;">edit</span></button>
                  <button class="btn btn-outline btn-sm text-danger"><span class="material-symbols-rounded" style="font-size: 16px;">delete</span></button>
                </td>
              </tr>
              <tr *ngIf="rules.length === 0">
                <td colspan="7" class="text-center py-5 text-muted">No configuration rules currently active.</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
      
      <div class="mt-4 row">
         <div class="col-6">
            <div class="alert alert-info d-flex gap-3 align-items-center">
               <span class="material-symbols-rounded text-primary">info</span>
               <div>
                  <strong>Acerca del Ruteo V2</strong><br>
                  <small>Las reglas se evalúan en orden de Prioridad (Menor número es mayor prioridad). Una acción <b>LocalAuth</b> dirige la transacción a tu Vault nativo en lugar de reenviarla.</small>
               </div>
            </div>
         </div>
         <div class="col-6">
            <div class="card bg-dark text-white p-3 d-flex flex-row align-items-center gap-3">
               <div class="icon-bg-dark rounded-circle p-2 bg-success text-white d-flex align-items-center justify-content-center">
                  <span class="material-symbols-rounded">check_circle</span>
               </div>
               <div>
                  <h4 class="m-0 font-weight-600">Routing Cache Synchronized</h4>
                  <small class="text-muted text-light">Última actualización: Hace un momento</small>
               </div>
               <button class="btn btn-primary btn-sm ms-auto">Forzar Sincronización</button>
            </div>
         </div>
      </div>
    </div>
  `,
    styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    
    .table-responsive { overflow-x: auto; }
    .table { width: 100%; border-collapse: collapse; font-size: 0.85rem; }
    .table th, .table td { padding: 1rem 1.25rem; text-align: left; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
    .table th { color: var(--text-muted); font-weight: 600; font-size: 0.70rem; text-transform: uppercase; background-color: #f8fafc;}
    .table tbody tr:hover { background-color: #fafbfc; }
    
    .font-monospace { font-family: 'Courier New', Courier, monospace; }
    .font-weight-500 { font-weight: 500; }
    .font-weight-600 { font-weight: 600; }
    .text-sm { font-size: 14px; }
    .text-primary-dark { color: #1e3a8a; }
    .text-light { opacity: 0.8; }
    
    .badge { padding: 0.25rem 0.5rem; border-radius: var(--radius-sm); font-size: 0.70rem; font-weight: 600; }
    .badge-success { background-color: #ecfdf5; color: #047857; }
    .badge-danger { background-color: #fef2f2; color: #b91c1c; }
    
    .net-visa { background: #e0e7ff; color: #1d4ed8; }
    .net-mc { background: #ffedd5; color: #c2410c; }
    .net-any { background: #e2e8f0; color: #0f172a; }
    
    .alert { padding: 1rem; border-radius: var(--radius-md); font-size: 0.875rem;}
    .alert-info { background-color: #eff6ff; color: #1e3a8a; border: 1px solid #bfdbfe; }
    
    .bg-dark { background-color: #1a1b26 !important; border: none; }
    .text-white { color: white !important; }
  `]
})
export class RoutingComponent implements OnInit {
    rules: RoutingRule[] = [];

    ngOnInit() {
        // TODO: Create RoutingService connected to IsoSwitch routing rules API
        // this.routingService.getRules().subscribe(data => this.rules = data);
        this.rules = [];
    }

    getNetworkClass(net: string): string {
        if (net.includes('VISA')) return 'net-visa';
        if (net.includes('MC')) return 'net-mc';
        return 'net-any';
    }
}
