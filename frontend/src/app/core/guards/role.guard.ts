import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { AuthService } from '../auth.service';

export const roleGuard: CanActivateFn = (route) => {
    const authService = inject(AuthService);
    const roles = route.data['roles'];

    return authService.ensureAuthorized(Array.isArray(roles) ? roles : []);
};
