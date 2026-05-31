import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  template: `
    <div class="auth-container">
      <div class="auth-card">
        <div class="auth-header">
           <div class="logo">
             <span class="material-symbols-rounded">account_balance</span>
             <span>Zitron<strong>System</strong></span>
           </div>
           <h1>Recuperar Acceso</h1>
           <p class="text-muted">Si olvidó su contraseña, ingrese su correo para recibir instrucciones.</p>
        </div>

        <div *ngIf="!emailSent">
          <div class="form-group mb-4">
            <label>Correo Electrónico</label>
            <div class="input-wrapper">
              <span class="material-symbols-rounded">mail</span>
              <input type="email" [(ngModel)]="email" placeholder="usuario@zitron.com" class="form-control">
            </div>
          </div>

          <div *ngIf="errorMessage" class="alert alert-danger mb-3">{{ errorMessage }}</div>

          <button class="btn btn-primary w-100 py-3 mb-3" [disabled]="!email" (click)="sendResetLink()">
            Enviar Enlace de Recuperación
          </button>
        </div>

        <div *ngIf="emailSent" class="text-center py-4">
           <div class="mb-4">
             <span class="material-symbols-rounded text-success" style="font-size: 64px">check_circle</span>
           </div>
           <h3 class="mb-2">¡Correo Enviado!</h3>
           <p class="text-muted">Hemos enviado un enlace a <strong>{{email}}</strong>. Por favor revise su bandeja de entrada.</p>
        </div>

        <div class="auth-footer">
          <a routerLink="/auth/login" class="text-primary text-decoration-none d-flex align-items-center justify-content-center gap-1">
             <span class="material-symbols-rounded" style="font-size: 18px">arrow_back</span> Regresar al Login
          </a>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .auth-container {
      height: 100vh; width: 100vw; display: flex; align-items: center; justify-content: center;
      background: linear-gradient(135deg, #1e3c72 0%, #2a5298 100%); font-family: 'Outfit', sans-serif;
    }
    .auth-card {
      background: white; width: 100%; max-width: 420px; padding: 2.5rem;
      border-radius: 1.5rem; box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.5);
    }
    .auth-header { text-align: center; margin-bottom: 2rem; }
    .logo { 
      font-size: 1.8rem; color: var(--primary); display: flex; align-items: center; 
      justify-content: center; gap: 0.5rem; margin-bottom: 1.5rem; 
    }
    .logo span:last-child { letter-spacing: -1px; }
    .auth-header h1 { font-size: 1.5rem; color: #1e293b; margin: 0; font-weight: 700; }
    .auth-header p { font-size: 0.95rem; margin-top: 0.5rem; }
    
    .form-group label { display: block; font-size: 0.9rem; font-weight: 600; color: #475569; margin-bottom: 0.5rem; }
    .input-wrapper { position: relative; display: flex; align-items: center; }
    .input-wrapper span { position: absolute; left: 1rem; color: #94a3b8; }
    .input-wrapper input { padding-left: 3rem; width: 100%; }
    
    .auth-footer { margin-top: 2rem; border-top: 1px solid #f1f5f9; padding-top: 1.5rem; text-align: center; }
    .w-100 { width: 100%; }
  `]
})
export class ForgotPasswordComponent {
  email = '';
  emailSent = false;
  loading = false;
  errorMessage = '';
  private router = inject(Router);
  private authService = inject(AuthService);

  sendResetLink() {
    if (!this.email) return;
    this.loading = true;
    this.errorMessage = '';
    this.authService.forgotPassword(this.email).subscribe({
      next: () => {
        this.loading = false;
        this.emailSent = true;
      },
      error: () => {
        this.loading = false;
        this.errorMessage = 'Hubo un problema al enviar el enlace. Por favor intente nuevamente.';
      }
    });
  }
}
