import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  template: `
    <div class="login-wrapper">
      <div class="glass-orb orb-1"></div>
      <div class="glass-orb orb-2"></div>

      <div class="login-container">
        <div class="card login-card glass-panel">
          <div class="login-header">
            <div class="logo">
              <span class="material-symbols-rounded">account_balance</span>
            </div>
            <h1>Bienvenido a Zitron</h1>
            <p class="subtitle">La nueva generación bancaria</p>
          </div>

          <form [formGroup]="loginForm" (ngSubmit)="onSubmit()">
            
            <div class="input-group" [class.has-error]="isFieldInvalid('email')">
              <label for="email">Correo Electrónico</label>
              <div class="input-with-icon">
                <span class="material-symbols-rounded icon">mail</span>
                <input 
                  type="email" 
                  id="email"
                  class="form-control" 
                  formControlName="email" 
                  placeholder="admin@demo.com"
                  autocomplete="username"
                >
              </div>
              <div class="field-error fade-in" *ngIf="isFieldInvalid('email')">
                <span class="material-symbols-rounded text-sm">error</span>
                <span *ngIf="f['email'].errors?.['required']">El correo es obligatorio.</span>
                <span *ngIf="f['email'].errors?.['email']">El formato no es válido.</span>
              </div>
            </div>

            <div class="input-group" [class.has-error]="isFieldInvalid('password')">
              <label for="password">Contraseña</label>
              <div class="input-with-icon right-icon">
                <span class="material-symbols-rounded icon">lock</span>
                <input 
                  [type]="showPassword ? 'text' : 'password'" 
                  id="password"
                  class="form-control" 
                  formControlName="password" 
                  placeholder="••••••••"
                  autocomplete="current-password"
                >
                <button type="button" class="icon-action" (click)="togglePassword()" aria-label="Toggle password visibility">
                  <span class="material-symbols-rounded">{{ showPassword ? 'visibility' : 'visibility_off' }}</span>
                </button>
              </div>
              <div class="field-error fade-in" *ngIf="isFieldInvalid('password')">
                <span class="material-symbols-rounded text-sm">error</span>
                <span *ngIf="f['password'].errors?.['required']">La contraseña es obligatoria.</span>
              </div>
            </div>

            <div class="options-group">
              <label class="checkbox-container">
                <input type="checkbox" formControlName="rememberMe"> 
                <span class="checkmark"></span>
                Recordarme
              </label>
              <a routerLink="/auth/forgot-password" class="forgot-password">¿Olvidaste tu contraseña?</a>
            </div>

            <div class="auth-error fade-in" *ngIf="authService.authError() as error">
              <span class="material-symbols-rounded">warning</span>
              <p>{{ error }}</p>
            </div>

            <button 
              type="submit" 
              class="btn-primary login-btn" 
              [disabled]="loginForm.invalid || authService.isSubmitting()"
            >
              <ng-container *ngIf="!authService.isSubmitting()">
                Iniciar Sesión
              </ng-container>
              <ng-container *ngIf="authService.isSubmitting()">
                <span class="loader"></span> Validando...
              </ng-container>
            </button>
          </form>

          <div class="login-footer">
            <span class="text-muted">¿No tienes cuenta?</span> <a class="link-styled" href="#">Regístrate aquí</a>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    :host {
      --primary-gradient: linear-gradient(135deg, #4f46e5 0%, #3b82f6 100%);
      --surface-glass: rgba(255, 255, 255, 0.85);
      --border-glass: rgba(255, 255, 255, 0.4);
      --shadow-glass: 0 8px 32px 0 rgba(31, 38, 135, 0.07);
      --text-main: #1e293b;
      --text-muted: #64748b;
      --error-color: #ef4444;
      --error-bg: #fef2f2;
      --radius-xl: 20px;
      --radius-lg: 12px;
      --transition-core: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
    }

    .login-wrapper {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      background: #f1f5f9;
      position: relative;
      overflow: hidden;
      font-family: 'Inter', system-ui, -apple-system, sans-serif;
    }

    /* Ambient Background Orbs */
    .glass-orb {
      position: absolute;
      border-radius: 50%;
      filter: blur(80px);
      z-index: 0;
      animation: float 10s infinite alternate ease-in-out;
    }

    .orb-1 {
      width: 400px;
      height: 400px;
      background: rgba(79, 70, 229, 0.3);
      top: -100px;
      right: -100px;
    }

    .orb-2 {
      width: 350px;
      height: 350px;
      background: rgba(59, 130, 246, 0.25);
      bottom: -150px;
      left: -50px;
      animation-delay: -5s;
    }

    @keyframes float {
      0% { transform: translate(0, 0) scale(1); }
      100% { transform: translate(30px, 40px) scale(1.1); }
    }

    .login-container {
      width: 100%;
      max-width: 420px;
      padding: 1rem;
      position: relative;
      z-index: 1;
    }

    .glass-panel {
      background: var(--surface-glass);
      backdrop-filter: blur(16px);
      -webkit-backdrop-filter: blur(16px);
      border: 1px solid var(--border-glass);
      border-radius: var(--radius-xl);
      box-shadow: var(--shadow-glass);
      padding: 2.5rem 2rem;
    }

    .login-header {
      text-align: center;
      margin-bottom: 2rem;
    }

    .logo {
      width: 56px;
      height: 56px;
      background: var(--primary-gradient);
      color: white;
      border-radius: var(--radius-lg);
      display: flex;
      align-items: center;
      justify-content: center;
      margin: 0 auto 1.25rem;
      box-shadow: 0 10px 15px -3px rgba(79, 70, 229, 0.3);
    }

    .logo .material-symbols-rounded {
      font-size: 28px;
    }

    .login-header h1 {
      font-size: 1.5rem;
      font-weight: 700;
      margin-bottom: 0.25rem;
      color: var(--text-main);
      letter-spacing: -0.025em;
    }

    .subtitle {
      color: var(--text-muted);
      font-size: 0.9rem;
    }

    .input-group {
      margin-bottom: 1.25rem;
    }

    .input-group label {
      display: block;
      font-size: 0.85rem;
      font-weight: 600;
      color: var(--text-main);
      margin-bottom: 0.5rem;
    }

    .input-with-icon {
      position: relative;
      display: flex;
      align-items: center;
    }

    .input-with-icon .icon {
      position: absolute;
      left: 1rem;
      color: #94a3b8;
      font-size: 1.2rem;
      transition: var(--transition-core);
    }

    .form-control {
      width: 100%;
      background: rgba(255, 255, 255, 0.7);
      border: 1px solid #e2e8f0;
      padding: 0.75rem 1rem 0.75rem 2.75rem;
      border-radius: var(--radius-lg);
      font-size: 0.95rem;
      color: var(--text-main);
      transition: var(--transition-core);
      box-sizing: border-box;
      outline: none;
    }

    .input-with-icon.right-icon .form-control {
      padding-right: 2.75rem;
    }

    .form-control:focus {
      background: #ffffff;
      border-color: #6366f1;
      box-shadow: 0 0 0 4px rgba(99, 102, 241, 0.1);
    }

    .form-control:focus ~ .icon,
    .form-control:not(:placeholder-shown) ~ .icon {
      color: #6366f1;
    }

    /* Error States */
    .has-error .form-control {
      border-color: var(--error-color);
      background: var(--error-bg);
    }
    
    .has-error .form-control:focus {
      box-shadow: 0 0 0 4px rgba(239, 68, 68, 0.1);
    }

    .field-error {
      display: flex;
      align-items: center;
      gap: 0.3rem;
      color: var(--error-color);
      font-size: 0.8rem;
      margin-top: 0.4rem;
      font-weight: 500;
    }
    
    .field-error .text-sm {
      font-size: 1rem;
    }

    .icon-action {
      position: absolute;
      right: 0.75rem;
      background: none;
      border: none;
      color: #94a3b8;
      cursor: pointer;
      display: flex;
      padding: 0.25rem;
      border-radius: 50%;
      transition: var(--transition-core);
    }

    .icon-action:hover {
      color: var(--text-main);
      background: #f1f5f9;
    }

    .options-group {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 2rem;
      font-size: 0.85rem;
    }

    .checkbox-container {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      color: var(--text-muted);
      cursor: pointer;
      font-weight: 500;
      user-select: none;
    }

    .checkbox-container input {
      accent-color: #4f46e5;
      width: 16px;
      height: 16px;
      cursor: pointer;
    }

    .forgot-password, .link-styled {
      color: #4f46e5;
      text-decoration: none;
      font-weight: 600;
      transition: var(--transition-core);
    }

    .forgot-password:hover, .link-styled:hover {
      color: #312e81;
      text-decoration: underline;
    }

    .auth-error {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      margin-bottom: 1.25rem;
      padding: 0.75rem 1rem;
      border-radius: var(--radius-lg);
      background-color: var(--error-bg);
      border: 1px solid #fca5a5;
      color: #991b1b;
      font-size: 0.85rem;
      font-weight: 500;
    }

    .btn-primary {
      width: 100%;
      padding: 0.875rem;
      font-size: 1rem;
      font-weight: 600;
      color: white;
      background: var(--primary-gradient);
      border: none;
      border-radius: var(--radius-lg);
      cursor: pointer;
      transition: var(--transition-core);
      display: flex;
      justify-content: center;
      align-items: center;
      gap: 0.5rem;
      box-shadow: 0 4px 12px rgba(79, 70, 229, 0.25);
    }

    .btn-primary:not(:disabled):hover {
      transform: translateY(-1px);
      box-shadow: 0 6px 16px rgba(79, 70, 229, 0.35);
    }

    .btn-primary:not(:disabled):active {
      transform: translateY(1px);
      box-shadow: 0 2px 8px rgba(79, 70, 229, 0.2);
    }

    .btn-primary:disabled {
      background: #cbd5e1;
      cursor: not-allowed;
      box-shadow: none;
      color: #94a3b8;
    }

    .login-footer {
      text-align: center;
      margin-top: 2rem;
      font-size: 0.85rem;
    }

    /* Small animations */
    .fade-in {
      animation: fadeIn 0.3s ease-out;
    }

    @keyframes fadeIn {
      from { opacity: 0; transform: translateY(-5px); }
      to { opacity: 1; transform: translateY(0); }
    }

    /* Loader */
    .loader {
      width: 18px;
      height: 18px;
      border: 2px solid #ffffff;
      border-bottom-color: transparent;
      border-radius: 50%;
      display: inline-block;
      box-sizing: border-box;
      animation: rotation 1s linear infinite;
    }

    @keyframes rotation {
      0% { transform: rotate(0deg); }
      100% { transform: rotate(360deg); }
    }
  `]
})
export class LoginComponent {
  authService = inject(AuthService);
  fb = inject(FormBuilder);

  showPassword = false;

  loginForm: FormGroup = this.fb.group({
    email: ['admin@demo.com', [Validators.required, Validators.email]],
    password: ['Admin1234!', [Validators.required]],
    rememberMe: [false] // Se integra el checkbox al formulario
  });

  // Getter para acceso fácil a los controls en el template
  get f() { return this.loginForm.controls; }

  togglePassword() {
    this.showPassword = !this.showPassword;
  }

  isFieldInvalid(field: string): boolean {
    const control = this.loginForm.get(field);
    return !!control && control.invalid && (control.dirty || control.touched);
  }

  onSubmit() {
    if (this.loginForm.valid) {
      // Si todo es válido, limpiamos los errores visuales pre-existentes no asociados al HTTP
      const { email, password, rememberMe } = this.loginForm.getRawValue();
      
      // Lógica de "rememberMe" puede inyectarse si el AuthService lo soporta,
      // por ahora se obvia o se prepara para futuro uso de refresh tokens de larga duración.
      this.authService.login(email, password);
    } else {
      // Forzar que los campos muestren estado "inválido" visualmente
      this.loginForm.markAllAsTouched();
    }
  }
}
