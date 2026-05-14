import { Injectable, signal } from '@angular/core';

@Injectable({
    providedIn: 'root'
})
export class LoadingService {
    private activeRequests = 0;

    // Expose a signal to the components
    isLoading = signal<boolean>(false);

    show() {
        this.activeRequests++;
        if (this.activeRequests === 1) {
            this.isLoading.set(true);
        }
    }

    hide() {
        this.activeRequests--;
        if (this.activeRequests <= 0) {
            this.activeRequests = 0;
            this.isLoading.set(false);
        }
    }
}
