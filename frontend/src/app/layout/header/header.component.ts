import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [CommonModule],
  template: `
    <header class="header">
      <div class="search-bar">
        <span class="material-symbols-rounded search-icon">search</span>
        <input type="text" placeholder="Buscar..." class="search-input">
      </div>
      
      <div class="header-actions">
        <div class="firm-badge">
          <span class="material-symbols-rounded">business</span>
          Firma Principal
        </div>
        
        <div class="role-badge" *ngIf="user()">
          {{user()?.role}}
        </div>
        
        <button class="icon-btn">
          <span class="material-symbols-rounded">notifications</span>
          <span class="notification-badge"></span>
        </button>
        
        <div class="user-profile">
          <div class="avatar">{{user()?.name?.charAt(0) | uppercase}}</div>
          <div class="user-info">
            <span class="user-name">{{user()?.name}}</span>
          </div>
          <button class="logout-btn" (click)="logout()" title="Cerrar sesión">
            <span class="material-symbols-rounded">logout</span>
          </button>
        </div>
      </div>
    </header>
  `,
  styles: [`
    .header {
      height: 70px;
      background-color: var(--bg-paper);
      border-bottom: 1px solid var(--border-color);
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 0 1.5rem;
      box-shadow: 0 1px 2px rgba(0,0,0,0.02);
      z-index: 10;
    }
    
    .search-bar {
      display: flex;
      align-items: center;
      background-color: var(--bg-main);
      border-radius: var(--radius-md);
      padding: 0.5rem 1rem;
      width: 400px;
    }
    
    .search-icon {
      color: var(--text-muted);
      margin-right: 0.5rem;
      font-size: 1.25rem;
    }
    
    .search-input {
      border: none;
      background: transparent;
      outline: none;
      width: 100%;
      font-family: inherit;
      color: var(--text-main);
    }
    
    .header-actions {
      display: flex;
      align-items: center;
      gap: 1.5rem;
    }
    
    .firm-badge {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      background-color: var(--primary-light);
      color: var(--primary-dark);
      padding: 0.4rem 0.8rem;
      border-radius: var(--radius-md);
      font-weight: 600;
      font-size: 0.875rem;
    }
    
    .role-badge {
      background-color: #fff3cd;
      color: #856404;
      padding: 0.2rem 0.6rem;
      border-radius: var(--radius-xl);
      font-size: 0.75rem;
      font-weight: 700;
    }
    
    .icon-btn {
      background: none;
      border: none;
      color: var(--text-muted);
      cursor: pointer;
      position: relative;
      display: flex;
      align-items: center;
      justify-content: center;
      width: 36px;
      height: 36px;
      border-radius: 50%;
      transition: background-color 0.2s;
    }
    
    .icon-btn:hover {
      background-color: var(--bg-main);
      color: var(--primary);
    }
    
    .notification-badge {
      position: absolute;
      top: 6px;
      right: 8px;
      width: 8px;
      height: 8px;
      background-color: var(--danger);
      border-radius: 50%;
      border: 2px solid var(--bg-paper);
    }
    
    .user-profile {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      padding-left: 1.5rem;
      border-left: 1px solid var(--border-color);
    }
    
    .avatar {
      width: 36px;
      height: 36px;
      background-color: var(--primary);
      color: white;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 700;
    }
    
    .user-info {
      display: flex;
      flex-direction: column;
    }
    
    .user-name {
      font-weight: 600;
      font-size: 0.875rem;
    }

    .logout-btn {
      background: none;
      border: none;
      color: var(--text-muted);
      cursor: pointer;
      margin-left: 0.5rem;
    }
    .logout-btn:hover {
      color: var(--danger);
    }
  `]
})
export class HeaderComponent {
  authService = inject(AuthService);
  user = this.authService.currentUser;

  logout() {
    this.authService.logout();
  }
}
