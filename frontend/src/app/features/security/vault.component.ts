import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { VaultService } from './vault.service';
import { catchError, of, forkJoin, timer } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import { NotificationService } from '../../core/notification.service';

@Component({
  selector: 'app-vault',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="page-container">
      <div class="page-header mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">admin_panel_settings</span>
            Security Vault Dashboard (PCI-DSS)
          </h1>
          <p class="text-muted mt-1">Gestión avanzada de la Bóveda de Tokens, rotación de llaves AES-256 e inspección de logs de detokenización.</p>
        </div>
        <div class="header-actions">
           <button class="btn btn-outline" (click)="loadData()">
              <span class="material-symbols-rounded">refresh</span> Refrescar
           </button>
           <button class="btn btn-primary" (click)="rotateKey()" [disabled]="isRotating">
              <span class="material-symbols-rounded">key_visualizer</span> Rotate Active Key
           </button>
        </div>
      </div>

      <!-- Stats Grid -->
      <div class="stats-row mb-4">
        <div class="stat-card card">
          <div class="stat-icon bg-soft-primary"><span class="material-symbols-rounded">key</span></div>
          <div class="stat-content">
             <label>Active Key ID</label>
             <div class="d-flex align-items-center gap-2">
                <code class="text-main">{{ activeKeyId || 'HSM_MASTER_v1' }}</code>
                <span class="badge badge-success text-xs">ONLINE</span>
             </div>
          </div>
        </div>
        <div class="stat-card card">
          <div class="stat-icon bg-soft-warning"><span class="material-symbols-rounded">history</span></div>
          <div class="stat-content">
             <label>Audit Level</label>
             <strong>Detokenization (High)</strong>
          </div>
        </div>
        <div class="stat-card card">
          <div class="stat-icon bg-soft-info"><span class="material-symbols-rounded">memory</span></div>
          <div class="stat-content">
             <label>Crypto Provider</label>
             <div class="d-flex flex-column">
                <strong>Software-Based HSM</strong>
                <small class="text-muted">FIPS 140-2 Level 2</small>
             </div>
          </div>
        </div>
        <div class="stat-card card">
          <div class="stat-icon bg-soft-danger"><span class="material-symbols-rounded">shield</span></div>
          <div class="stat-content">
             <label>Vault Status</label>
             <span class="badge badge-primary">STRICT MODE</span>
          </div>
        </div>
      </div>

      <div class="row">
         <!-- Left: Crypto Audit -->
         <div class="col-8">
            <div class="card p-0">
               <div class="card-header border-bottom p-3 d-flex justify-content-between align-items-center">
                  <h3 class="m-0 text-main">Audit Logs (Token Access)</h3>
                  <span class="badge badge-outline">Filtered: detokenize / secret</span>
               </div>
               <div class="table-responsive" style="max-height: 500px">
                  <table class="table mb-0">
                     <thead>
                        <tr>
                           <th>Timestamp</th>
                           <th>Actor / Identity</th>
                           <th>Action</th>
                           <th>Resource</th>
                           <th class="text-right">Result</th>
                        </tr>
                     </thead>
                     <tbody>
                        <tr *ngFor="let audit of auditLogs">
                           <td class="text-muted">{{ audit.occurredOn | date:'HH:mm:ss' }}</td>
                           <td>
                              <div class="d-flex align-items-center gap-2">
                                 <span class="avatar-sm"></span>
                                 <span>{{ audit.actor }}</span>
                              </div>
                           </td>
                           <td class="font-weight-500">{{ audit.eventName }}</td>
                           <td><code class="text-xs">{{ audit.metadata }}</code></td>
                           <td class="text-right">
                              <span class="badge" [ngClass]="audit.eventName.includes('error') ? 'badge-danger' : 'badge-success'">
                                 SUCCESS
                              </span>
                           </td>
                        </tr>
                        <tr *ngIf="auditLogs.length === 0">
                           <td colspan="5" class="text-center py-5 text-muted">
                              <span class="material-symbols-rounded mb-2" style="font-size: 32px; opacity:0.3">vpn_key</span>
                              <p>No se encontraron accesos a la bóveda en las últimas horas.</p>
                           </td>
                        </tr>
                     </tbody>
                  </table>
               </div>
            </div>
         </div>

         <!-- Right: Actions -->
         <div class="col-4">
            <div class="card mb-4 bg-dark text-white border-0 shadow-lg">
               <div class="p-4">
                  <h4 class="d-flex align-items-center gap-2 mb-3">
                     <span class="material-symbols-rounded text-warning">warning</span> Pro-Active Rotation
                  </h4>
                  <p class="text-secondary text-sm mb-4">
                    La rotación de llaves AES-256 es requerida por **PCI-DSS** trimestralmente.
                    Al rotar, los nuevos tokens se generarán con la nueva versión de la llave.
                  </p>
                  <button class="btn btn-warning w-100" [disabled]="isRotating" (click)="rotateKey()">
                      {{ isRotating ? 'Rotating...' : 'Rotate Cripto Vault' }}
                  </button>
               </div>
            </div>

            <div class="card">
               <h4 class="mb-3 d-flex align-items-center gap-2">
                  <span class="material-symbols-rounded">sync</span> Data Re-Encryption
               </h4>
               <p class="text-muted text-sm mb-4">
                  Actualiza todos los PANs cifrados con versiones anteriores de llaves a la llave activa actual. 
                  Operación asíncrona recomendada en horas de bajo tráfico.
               </p>
               <button class="btn btn-outline w-100 mb-3" (click)="reEncrypt()">
                  Batch Re-Encrypt (v1 -> Active)
               </button>
               <div class="text-center">
                  <small class="text-muted">Estimated: 1,420 tokens (v1)</small>
               </div>
            </div>
         </div>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .stats-row { display: grid; grid-template-columns: repeat(4, 1fr); gap: 1rem; }
    .stat-card { display: flex; align-items: center; gap: 1rem; padding: 1.25rem; }
    .stat-icon { width: 48px; height: 48px; border-radius: 12px; display: flex; align-items: center; justify-content: center; font-size: 24px; }
    .stat-content label { font-size: 0.75rem; text-transform: uppercase; color: var(--text-muted); font-weight: 600; display: block; margin-bottom: 2px; }
    
    .bg-soft-primary { background: #e0f2fe; color: #0369a1; }
    .bg-soft-warning { background: #fef3c7; color: #92400e; }
    .bg-soft-info { background: #e0f2fe; color: #0891b2; }
    .bg-soft-danger { background: #fee2e2; color: #b91c1c; }

    .badge-outline { border: 1px solid var(--border-color); color: var(--text-muted); padding: 0.2rem 0.5rem; }
    .badge-success { background: #ecfdf5; color: #047857; }
    .badge-danger { background: #fef2f2; color: #b91c1c; }
    .badge-primary { background: #eff6ff; color: #1d4ed8; }
    
    .avatar-sm { width: 24px; height: 24px; border-radius: 50%; background: #ddd; }
    .text-xs { font-size: 0.7rem; }
    .font-weight-500 { font-weight: 500; }
    
    .row { display: flex; gap: 1.5rem; margin-top: 1.5rem; }
    .col-8 { flex: 0 0 calc(66.6% - 0.75rem); }
    .col-4 { flex: 0 0 calc(33.3% - 0.75rem); }

    .text-sm { font-size: 0.85rem; }
    .text-secondary { color: rgba(255,255,255,0.7); }
    
    .table th { font-size: 0.7rem; text-transform: uppercase; color: var(--text-muted); }
    .table td { vertical-align: middle; }
  `]
})
export class VaultComponent {
  private vaultService = inject(VaultService);
  private notifications = inject(NotificationService);

  activeKeyId: string = '';
  availableKeyIds: string[] = [];
  auditLogs: any[] = [];
  isRotating = false;

  constructor() {
    this.loadData();
  }

  loadData() {
    forkJoin({
      active: this.vaultService.getActiveKey().pipe(catchError(err => {
        console.error('Error loading active key:', err?.status, err?.message);
        this.notifications.warning('No se pudo cargar la llave activa de la bóveda');
        return of({ activeKeyId: 'N/A' });
      })),
      audit: this.vaultService.getAuditLogs().pipe(catchError(err => {
        console.error('Error loading audit logs:', err?.status, err?.message);
        this.notifications.warning('No se pudo cargar los logs de auditoría');
        return of([]);
      }))
    }).subscribe((res: any) => {
      this.activeKeyId = res.active?.activeKeyId || 'N/A';
      this.availableKeyIds = res.active?.availableKeyIds || [];
      this.auditLogs = res.audit.filter((x: any) => 
        x.eventName.toLowerCase().includes('token') || 
        x.eventName.toLowerCase().includes('security') ||
        x.eventName.toLowerCase().includes('authorized')
      );
    });
  }

  rotateKey() {
    const nextKey = this.availableKeyIds.find(k => k !== this.activeKeyId);
    if (!nextKey) {
      this.notifications.warning('No hay otras llaves disponibles para rotar. Configure una segunda llave en Vault:Keys.');
      return;
    }
    if (confirm(`¿Rotar la llave activa de "${this.activeKeyId}" a "${nextKey}"? Las nuevas transacciones usarán la nueva llave AES.`)) {
      this.isRotating = true;
      this.vaultService.rotateKey(nextKey).subscribe({
        next: () => {
          this.isRotating = false;
          this.notifications.success('Rotación de llaves completada exitosamente');
          this.loadData();
        },
        error: (err) => {
          this.isRotating = false;
          console.error('Error rotating key:', err?.status, err?.message);
          this.notifications.error('Error al rotar la llave. Verifique la conexión.');
        }
      });
    }
  }

  reEncrypt() {
    if (confirm('Se iniciará un proceso de re-cifrado por lotes. ¿Desea continuar?')) {
       this.vaultService.reEncrypt().subscribe({
         next: () => this.notifications.success('Proceso de re-cifrado iniciado en background'),
         error: (err) => {
           console.error('Error starting re-encryption:', err?.status, err?.message);
           this.notifications.error('Error al iniciar re-cifrado. Verifique la conexión.');
         }
       });
    }
  }
}
