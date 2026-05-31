import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule, ActivatedRoute } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-reset-password',
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
          <h1>Nueva Contraseña</h1>
          <p class="text-muted">Ingrese su nueva contraseña para completar la recuperación.</p>
        </div>

        <div *ngIf="invalidLink" class="text-center py-4">
          <div class="mb-4">
            <span class="material-symbols-rounded text-danger" style="font-size: 64px">link_off</span>
          </div>
          <h3 class="mb-2">Enlace Inválido</h3>
          <p class="text-muted">El enlace de recuperación es inválido o ha expirado. Solicite uno nuevo.</p>
        </div>

        <div *ngIf="!submitted && !invalidLink">
          <div class="form-group mb-4">
            <label>Nueva Contraseña</label>
            <div class="input-wrapper">
              <span class="material-symbols-rounded">lock</span>
              <input type="password" [(ngModel)]="newPassword" placeholder="••••••••" class="form-control">
            </div>
          </div>

          <div *ngIf="error" class="alert alert-danger mb-3">{{ error }}</div>

          <button class="btn btn-primary w-100 py-3 mb-3" [disabled]="!newPassword" (click)="resetPassword()">
            Cambiar Contraseña
          </button>
        </div>

        <div *ngIf="submitted" class="text-center py-4">
          <div class="mb-4">
            <span class="material-symbols-rounded text-success" style="font-size: 64px">check_circle</span>
          </div>
          <h3 class="mb-2">¡Contraseña Actualizada!</h3>
          <p class="text-muted">Su contraseña fue cambiada exitosamente. Puede iniciar sesión ahora.</p>
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
    .alert-danger { background: #fef2f2; color: #991b1b; border: 1px solid #fecaca; padding: 0.75rem 1rem; border-radius: 0.5rem; font-size: 0.9rem; }
  `]
})
export class ResetPasswordComponent implements OnInit {
  token = '';
  newPassword = '';
  error = '';
  submitted = false;
  invalidLink = false;

  private authService = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  ngOnInit() {
    this.token = this.route.snapshot.queryParamMap.get('token') ?? '';
    if (!this.token) {
      this.invalidLink = true;
    }
  }

  resetPassword() {
    if (this.invalidLink || !this.token || !this.newPassword) return;
    this.error = '';
    this.authService.resetPassword(this.token, this.newPassword).subscribe({
      next: () => {
        this.submitted = true;
        // Spec IAM-PR-4-S4: show success state — do NOT auto-navigate; let user click the back link
      },
      error: () => {
        this.error = 'El enlace de recuperación es inválido o ha expirado.';
      }
    });
  }
}
