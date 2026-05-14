import { Injectable, signal } from '@angular/core';

export interface Notification {
  id: number;
  message: string;
  type: 'success' | 'error' | 'info' | 'warning';
}

@Injectable({
  providedIn: 'root'
})
export class NotificationService {
  notifications = signal<Notification[]>([]);
  private nextId = 1;

  show(message: string, type: 'success' | 'error' | 'info' | 'warning' = 'info', duration = 5000) {
    const id = this.nextId++;
    const notification: Notification = { id, message, type };
    
    this.notifications.update(prev => [...prev, notification]);

    if (duration > 0) {
      setTimeout(() => {
        this.remove(id);
      }, duration);
    }
  }

  success(message: string) { this.show(message, 'success'); }
  error(message: string) { this.show(message, 'error', 10000); } // Errors last longer
  info(message: string) { this.show(message, 'info'); }
  warning(message: string) { this.show(message, 'warning'); }

  remove(id: number) {
    this.notifications.update(prev => prev.filter(n => n.id !== id));
  }
}
