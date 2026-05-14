import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NotificationService } from '../../core/notification.service';

@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="notifications-container">
      @for (n of service.notifications(); track n.id) {
        <div class="notification-toast" [class]="n.type" (click)="service.remove(n.id)">
            <span class="material-symbols-rounded">{{ getIcon(n.type) }}</span>
            <span class="message">{{ n.message }}</span>
            <button class="close-btn">&times;</button>
        </div>
      }
    </div>
  `,
  styles: [`
    .notifications-container {
      position: fixed;
      top: 20px;
      right: 20px;
      z-index: 9999;
      display: flex;
      flex-direction: column;
      gap: 10px;
      max-width: 400px;
    }

    .notification-toast {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 16px;
      border-radius: 8px;
      background: white;
      box-shadow: 0 4px 12px rgba(0,0,0,0.15);
      cursor: pointer;
      animation: slideIn 0.3s ease-out forwards;
      border-left: 4px solid var(--primary);
    }

    .message {
      font-size: 0.875rem;
      font-weight: 500;
      color: #1f2937;
      flex-grow: 1;
    }

    .success { border-left-color: #10b981; }
    .success span { color: #10b981; }
    
    .error { border-left-color: #ef4444; }
    .error span { color: #ef4444; }

    .warning { border-left-color: #f59e0b; }
    .warning span { color: #f59e0b; }

    .info { border-left-color: #3b82f6; }
    .info span { color: #3b82f6; }

    .close-btn {
      background: none;
      border: none;
      font-size: 1.25rem;
      color: #9ca3af;
      padding: 0;
      line-height: 1;
    }

    @keyframes slideIn {
      from { transform: translateX(100%); opacity: 0; }
      to { transform: translateX(0); opacity: 1; }
    }
  `]
})
export class NotificationComponent {
  service = inject(NotificationService);

  getIcon(type: string): string {
    switch (type) {
      case 'success': return 'check_circle';
      case 'error': return 'error';
      case 'warning': return 'warning';
      default: return 'info';
    }
  }
}
