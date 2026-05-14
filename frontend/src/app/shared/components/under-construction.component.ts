import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
    selector: 'app-under-construction',
    standalone: true,
    imports: [CommonModule],
    template: `
    <div class="construction-container">
      <div class="text-center">
        <div class="icon-warning">
          <span class="material-symbols-rounded">construction</span>
        </div>
        <h1 class="mt-4 mb-2">Página en Construcción</h1>
        <p class="text-muted mb-4">
          Este módulo de <strong>Zitron Platform</strong> se encuentra actualmente en desarrollo<br>
          o ha sido planeado para un Sprint futuro.
        </p>
        
        <button class="btn btn-primary" onclick="window.history.back()">
          <span class="material-symbols-rounded me-2">arrow_back</span> Regresar
        </button>
      </div>
    </div>
  `,
    styles: [`
    .construction-container {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 70vh;
      border: 1px dashed var(--border-color);
      border-radius: var(--radius-xl);
      background-color: var(--bg-paper);
      margin: 2rem;
    }
    .icon-warning {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 100px;
      height: 100px;
      background-color: #fef9c3;
      color: #ca8a04;
      border-radius: 50%;
      animation: pulse 2s infinite;
    }
    .icon-warning .material-symbols-rounded {
      font-size: 50px;
    }
    h1 {
      color: var(--text-main);
    }
    .text-center {
      text-align: center;
    }
    
    @keyframes pulse {
      0% { box-shadow: 0 0 0 0 rgba(202, 138, 4, 0.4); }
      70% { box-shadow: 0 0 0 20px rgba(202, 138, 4, 0); }
      100% { box-shadow: 0 0 0 0 rgba(202, 138, 4, 0); }
    }
  `]
})
export class UnderConstructionComponent { }
