import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { AuthService } from '../auth.service';
import { User } from '../auth.service';

/**
 * Pure function: evaluates whether a user is authorized to access a route.
 *
 * Auth contract (v76-mora-temprana / CanViewCollections):
 *   Access is granted if:
 *   1. allowedRoles is empty → open route, always true
 *   2. user has at least one role from allowedRoles, OR
 *   3. user has at least one permission from requiredPermissions
 *
 * This mirrors the backend policy:
 *   CanViewCollections = role Admin | role Operator | claim collections:view
 */
export function isRouteAuthorized(
    user: User | null,
    allowedRoles: string[],
    requiredPermissions: string[]
): boolean {
    if (!user) {
        return false;
    }

    if (allowedRoles.length === 0 && requiredPermissions.length === 0) {
        return true;
    }

    const hasRole = allowedRoles.length > 0 && allowedRoles.some(role => user.roles.includes(role));
    if (hasRole) {
        return true;
    }

    const hasPermission = requiredPermissions.length > 0 && requiredPermissions.some(p => user.permissions.includes(p));
    return hasPermission;
}

export const roleGuard: CanActivateFn = (route) => {
    const authService = inject(AuthService);
    const roles: string[] = Array.isArray(route.data['roles']) ? route.data['roles'] : [];
    const permissions: string[] = Array.isArray(route.data['permissions']) ? route.data['permissions'] : [];

    return authService.ensureAuthorized(roles, permissions);
};
