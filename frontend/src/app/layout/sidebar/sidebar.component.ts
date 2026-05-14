import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../core/auth.service';

interface MenuItem {
  icon: string;
  label: string;
  route: string;
  roles: string[];
}

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive],
  template: `
    <aside class="sidebar" [class.collapsed]="collapsed">
      <div class="logo-container">
        <div class="logo-icon">
          <span class="material-symbols-rounded">credit_card</span>
        </div>
        <div class="logo-text" *ngIf="!collapsed">Zitron</div>
        <button class="toggle-btn" (click)="toggleSidebar()">
          <span class="material-symbols-rounded">menu</span>
        </button>
      </div>

      <div class="user-role" *ngIf="!collapsed && user()">
        <span class="badge">{{ user()?.role }}</span>
      </div>

      <nav class="nav-menu">
        <ng-container *ngFor="let group of menuGroups">
          <div class="nav-group-title d-flex justify-content-between align-items-center" 
               *ngIf="!collapsed && hasVisibleItems(group.items)"
               (click)="toggleGroup(group)">
            <span>{{ group.title }}</span>
            <span class="material-symbols-rounded" style="font-size: 16px; transition: transform 0.2s;" 
                  [style.transform]="group.expanded ? 'rotate(180deg)' : 'rotate(0deg)'">expand_more</span>
          </div>
          
          <div class="nav-group-content" [class.expanded]="group.expanded || collapsed">
            <ul class="nav-list" *ngIf="group.expanded || collapsed">
              <ng-container *ngFor="let item of group.items">
                <li class="nav-item" *ngIf="canAccess(item)">
                  <a [routerLink]="item.route" routerLinkActive="active" class="nav-link">
                    <span class="material-symbols-rounded nav-icon">{{ item.icon }}</span>
                    <span class="nav-label" *ngIf="!collapsed">{{ item.label }}</span>
                  </a>
                </li>
              </ng-container>
            </ul>
          </div>
        </ng-container>
      </nav>

      <div class="sidebar-footer" *ngIf="!collapsed">
        <small class="text-muted">Zitron Platform Inc.</small>
      </div>
    </aside>
  `,
  styles: [`
    .sidebar {
      width: 260px;
      height: 100vh;
      background-color: var(--bg-sidebar);
      color: #9899ac;
      display: flex;
      flex-direction: column;
      transition: width 0.3s ease;
      position: relative;
    }

    .sidebar.collapsed {
      width: 80px;
    }

    .logo-container {
      height: 70px;
      display: flex;
      align-items: center;
      padding: 0 1.5rem;
      border-bottom: 1px solid rgba(255, 255, 255, 0.05);
      background-color: rgba(0, 0, 0, 0.2);
      color: white;
    }

    .logo-icon {
      background-color: var(--primary);
      width: 32px;
      height: 32px;
      border-radius: var(--radius-sm);
      display: flex;
      align-items: center;
      justify-content: center;
      margin-right: 1rem;
    }

    .logo-text {
      font-size: 1.25rem;
      font-weight: 700;
      flex: 1;
    }

    .toggle-btn {
      background: none;
      border: none;
      color: #9899ac;
      cursor: pointer;
      display: flex;
    }

    .toggle-btn:hover {
      color: white;
    }

    .user-role {
      padding: 1rem 1.5rem 0;
    }

    .badge {
      background-color: var(--primary);
      color: white;
      padding: 0.2rem 0.5rem;
      border-radius: var(--radius-sm);
      font-size: 0.75rem;
      font-weight: 600;
      text-transform: uppercase;
    }

    .nav-menu {
      flex: 1;
      overflow-y: auto;
      padding: 1rem 0;
    }

    .nav-menu::-webkit-scrollbar {
      width: 6px;
    }

    .nav-menu::-webkit-scrollbar-track {
      background: transparent;
    }

    .nav-menu::-webkit-scrollbar-thumb {
      background-color: rgba(255, 255, 255, 0.1);
      border-radius: 10px;
    }

    .nav-menu::-webkit-scrollbar-thumb:hover {
      background-color: rgba(255, 255, 255, 0.2);
    }

    .nav-group-title {
      font-size: 0.75rem;
      text-transform: uppercase;
      letter-spacing: 1px;
      padding: 1rem 1.5rem 0.5rem;
      font-weight: 600;
      color: #6c6d80;
      cursor: pointer;
      user-select: none;
      transition: color 0.2s;
    }

    .nav-group-title:hover {
      color: #fff;
    }

    .nav-group-content {
      overflow: hidden;
    }

    .nav-list {
      list-style: none;
      padding: 0;
      margin: 0;
    }

    .nav-item {
      margin-bottom: 0.25rem;
      padding: 0 0.75rem;
    }

    .nav-link {
      display: flex;
      align-items: center;
      padding: 0.75rem 1rem;
      color: #9899ac;
      text-decoration: none;
      border-radius: var(--radius-md);
      transition: all 0.2s;
      font-size: 0.85rem;
    }

    .nav-label {
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .nav-link:hover {
      background-color: var(--bg-sidebar-hover);
      color: white;
    }

    .nav-link.active {
      background-color: var(--bg-sidebar-active);
      color: white;
    }

    .nav-icon {
      font-size: 1.25rem;
      width: 24px;
      text-align: center;
      margin-right: 1rem;
    }

    .sidebar.collapsed .nav-link {
      justify-content: center;
    }

    .sidebar.collapsed .nav-icon {
      margin-right: 0;
    }

    .sidebar-footer {
      padding: 1.5rem;
      border-top: 1px solid rgba(255, 255, 255, 0.05);
      font-size: 0.75rem;
    }
  `]
})
export class SidebarComponent {
  authService = inject(AuthService);
  user = this.authService.currentUser;
  collapsed = false;

  menuGroups = [
    {
      title: 'Principal',
      expanded: true,
      items: [
        { icon: 'dashboard', label: 'Dashboard', route: '/app/dashboard', roles: ['Admin', 'Operator', 'Auditor'] }
      ]
    },
    {
      title: 'Emision y Negocio',
      expanded: true,
      items: [
        { icon: 'group', label: 'Clientes', route: '/app/issuer/customers', roles: ['Admin', 'Operator'] },
        { icon: 'account_balance_wallet', label: 'Cuentas', route: '/app/issuer/accounts', roles: ['Admin', 'Operator'] },
        { icon: 'credit_card', label: 'Tarjetas', route: '/app/issuer/cards', roles: ['Admin', 'Operator'] }
      ]
    },
    {
      title: 'Finanzas',
      expanded: false,
      items: [
        { icon: 'receipt_long', label: 'Movimientos', route: '/app/finance/ledger', roles: ['Admin', 'Operator', 'Auditor'] },
        { icon: 'request_quote', label: 'Estados de Cuenta', route: '/app/finance/billing', roles: ['Admin', 'Operator', 'Auditor'] },
        { icon: 'account_balance', label: 'Compensacion', route: '/app/finance/settlements', roles: ['Admin', 'Auditor'] },
        { icon: 'account_tree', label: 'Diferidos y Cuotas', route: '/app/finance/installments', roles: ['Admin', 'Operator'] },
        { icon: 'calculate', label: 'Contabilidad', route: '/app/finance/accounting', roles: ['Admin', 'Auditor'] },
        { icon: 'tune', label: 'Cupos de Crédito', route: '/app/finance/credit-limits', roles: ['Admin', 'Operator'] }
      ]
    },
    {
      title: 'Inteligencia de Negocio',
      expanded: false,
      items: [
        { icon: 'bar_chart', label: 'Analytics & BI', route: '/app/finance/analytics', roles: ['Admin', 'Auditor'] }
      ]
    },
    {
      title: 'Ecosistema Digital',
      expanded: false,
      items: [
        { icon: 'loyalty', label: 'Fidelización', route: '/app/ecosystem/loyalty', roles: ['Admin', 'Operator'] },
        { icon: 'wallet', label: 'Billeteras Digitales', route: '/app/ecosystem/wallets', roles: ['Admin', 'Operator'] },
        { icon: 'notifications', label: 'Notificaciones', route: '/app/ecosystem/notifications', roles: ['Admin', 'Auditor'] },
        { icon: 'api', label: 'Open Banking', route: '/app/ecosystem/open-banking', roles: ['Admin'] }
      ]
    },
    {
      title: 'Switch y Operaciones',
      expanded: false,
      items: [
        { icon: 'router', label: 'Ruteo BIN', route: '/app/switch/routing', roles: ['Admin'] },
        { icon: 'list_alt', label: 'Catalogos Globales', route: '/app/switch/catalogs', roles: ['Admin'] },
        { icon: 'joystick', label: 'Simulador ISO', route: '/app/switch/simulator', roles: ['Admin', 'Operator'] }
      ]
    },
    {
      title: 'Monitoreo y Seguridad',
      expanded: false,
      items: [
        { icon: 'admin_panel_settings', label: 'Boveda de Tokens', route: '/app/security/vault', roles: ['Admin'] },
        { icon: 'history', label: 'Auditoria', route: '/app/switch/audit', roles: ['Admin', 'Auditor'] },
        { icon: 'policy', label: 'Motor Antifraude', route: '/app/security/rules', roles: ['Admin', 'Operator'] },
        { icon: 'shopping_cart_checkout', label: 'Seguridad E-commerce', route: '/app/security/ecommerce', roles: ['Admin', 'Operator', 'Auditor'] },
        { icon: 'gavel', label: 'Disputas y Fraude', route: '/app/security/disputes', roles: ['Admin', 'Operator', 'Auditor'] }
      ]
    },
    {
      title: 'Administracion',
      expanded: false,
      items: [
        { icon: 'people', label: 'Gestion de Usuarios', route: '/app/admin/users', roles: ['Admin'] },
        { icon: 'lock_person', label: 'Roles y Permisos', route: '/app/admin/roles', roles: ['Admin'] }
      ]
    }
  ];

  toggleGroup(group: any) {
    group.expanded = !group.expanded;
  }

  toggleSidebar() {
    this.collapsed = !this.collapsed;
  }

  canAccess(item: MenuItem): boolean {
    const user = this.user();
    if (!user) {
      return false;
    }

    return item.roles.some((role) => user.roles.includes(role));
  }

  hasVisibleItems(items: MenuItem[]): boolean {
    return items.some((item) => this.canAccess(item));
  }
}
