import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LoadingService } from '../../core/loading.service';

@Component({
    selector: 'app-global-loader',
    standalone: true,
    imports: [CommonModule],
    template: `
    <div class="loader-overlay" *ngIf="loadingService.isLoading()">
      <div class="loader-container">
         <div class="spinner-icon">
             <span class="material-symbols-rounded icon-spinning mb-2">autorenew</span>
         </div>
         <h4 class="loader-title">Cargando Información</h4>
         <p class="loader-text text-muted">Procesando solicitud de manera segura...</p>
      </div>
    </div>
  `,
    styles: [`
    .loader-overlay {
      position: fixed;
      top: 0;
      left: 0;
      width: 100vw;
      height: 100vh;
      background: rgba(255, 255, 255, 0.85);
      backdrop-filter: blur(4px);
      display: flex;
      justify-content: center;
      align-items: center;
      z-index: 9999;
      animation: fadeIn 0.3s forwards;
    }
    
    .loader-container {
       background: white;
       padding: 2.5rem;
       border-radius: var(--radius-xl);
       box-shadow: 0 10px 40px -10px rgba(0,0,0,0.1);
       text-align: center;
       border: 1px solid var(--border-color);
       min-width: 320px;
    }

    .icon-spinning {
      font-size: 48px;
      color: var(--primary);
      animation: spin 1.2s linear infinite;
      display: inline-block;
    }

    .loader-title {
      color: var(--text-main);
      margin: 0.5rem 0 0;
      font-weight: 700;
      font-size: 1.25rem;
    }
    
    .loader-text {
      font-size: 0.875rem;
      margin: 0.25rem 0 0;
    }

    @keyframes spin {
      100% { transform: rotate(360deg); }
    }
    
    @keyframes fadeIn {
      0% { opacity: 0; }
      100% { opacity: 1; }
    }
  `]
})
export class GlobalLoaderComponent {
    loadingService = inject(LoadingService);
}
