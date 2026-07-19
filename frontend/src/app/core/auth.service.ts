import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Router, UrlTree } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { catchError, finalize, map, shareReplay, switchMap, tap } from 'rxjs/operators';
import { environment } from '../../environments/environment';

const USER_KEY = 'auth.user';

export type Role = string;

export interface User {
    id: string;
    name: string;
    email: string;
    role: string;
    roles: string[];
    permissions: string[];
}

interface AuthenticatedUserResponse {
    id: string;
    email: string;
    name: string;
    primaryRole: string;
    roles: string[];
    permissions: string[];
}

interface AuthSessionResponse {
    mfaRequired: boolean;
    message: string | null;
    user: AuthenticatedUserResponse | null;
}

function isUnauthorizedError(error: unknown): boolean {
    return error instanceof HttpErrorResponse && error.status === 401;
}

@Injectable({
    providedIn: 'root'
})
export class AuthService {
    private readonly router = inject(Router);
    private readonly http = inject(HttpClient);

    private readonly currentUserSignal = signal<User | null>(this.readStoredUser());
    private readonly authErrorSignal = signal<string | null>(null);
    private readonly isSubmittingSignal = signal(false);

    private refreshRequest$: Observable<void> | null = null;

    get currentUser() {
        return this.currentUserSignal.asReadonly();
    }

    get authError() {
        return this.authErrorSignal.asReadonly();
    }

    get isSubmitting() {
        return this.isSubmittingSignal.asReadonly();
    }

    login(email: string, password: string): void {
        this.authErrorSignal.set(null);
        this.isSubmittingSignal.set(true);

        this.http.post<AuthSessionResponse>(`${environment.apiUrl}/auth/login`, { email, password })
            .pipe(finalize(() => this.isSubmittingSignal.set(false)))
            .subscribe({
                next: (response) => {
                    if (response.mfaRequired) {
                        this.authErrorSignal.set('MFA requerido. Ese flujo aun no esta conectado en el frontend.');
                        return;
                    }

                    if (!this.applySessionResponse(response)) {
                        this.authErrorSignal.set('La API no devolvio una sesion valida.');
                        return;
                    }

                    this.router.navigate(['/app/dashboard']);
                },
                error: () => {
                    this.clearSession(false);
                    this.authErrorSignal.set('No fue posible iniciar sesion con el backend.');
                }
            });
    }

    refreshSession(): Observable<void> {
        // SEC-03: the refresh token travels as the HttpOnly cv_rt cookie (withCredentials,
        // set by the interceptor) — there is no client-readable token to send here.
        if (!this.refreshRequest$) {
            this.refreshRequest$ = this.http
                .post<AuthSessionResponse>(`${environment.apiUrl}/auth/refresh`, {})
                .pipe(
                    map(() => void 0),
                    catchError((error) => {
                        this.clearSession(false);
                        return throwError(() => error);
                    }),
                    finalize(() => {
                        this.refreshRequest$ = null;
                    }),
                    shareReplay(1)
                );
        }

        return this.refreshRequest$;
    }

    loadCurrentUser(): Observable<User> {
        return this.http.get<AuthenticatedUserResponse>(`${environment.apiUrl}/auth/me`).pipe(
            map((response) => this.mapUser(response)),
            tap((user) => this.setCurrentUser(user))
        );
    }

    forgotPassword(email: string): Observable<void> {
        return this.http.post<void>(`${environment.apiUrl}/auth/forgot-password`, { email });
    }

    resetPassword(token: string, newPassword: string): Observable<void> {
        return this.http.post<void>(`${environment.apiUrl}/auth/reset-password`, { token, newPassword });
    }

    ensureAuthenticated(): Observable<boolean | UrlTree> {
        // SEC-03: session validity can no longer be checked client-side (the access token
        // is an HttpOnly cookie, unreadable by JS). /auth/me is the source of truth; on a
        // genuine 401 we attempt one refresh (cookie-driven) and retry /auth/me once more.
        return this.loadCurrentUser().pipe(
            map(() => true),
            catchError((error: unknown) => {
                // Only a real 401 means the session is invalid and worth a cookie-driven
                // refresh. A transient failure (500, timeout, network drop) must NOT tear
                // down a still-valid session — rethrow it as a retryable error instead of
                // forcing a false logout.
                if (!isUnauthorizedError(error)) {
                    return throwError(() => error);
                }

                return this.refreshSession().pipe(
                    switchMap(() => this.loadCurrentUser()),
                    map(() => true),
                    catchError(() => {
                        this.clearSession(false);
                        return of(this.router.createUrlTree(['/auth/login']));
                    })
                );
            })
        );
    }

    ensureAuthorized(allowedRoles: string[], requiredPermissions: string[] = []): Observable<boolean | UrlTree> {
        return this.ensureAuthenticated().pipe(
            map((result) => {
                if (result !== true) {
                    return result;
                }

                if (allowedRoles.length === 0 && requiredPermissions.length === 0) {
                    return true;
                }

                const user = this.currentUserSignal();
                if (!user) {
                    return this.router.createUrlTree(['/app/dashboard']);
                }

                const hasRole = allowedRoles.length > 0 && allowedRoles.some((role) => user.roles.includes(role));
                if (hasRole) {
                    return true;
                }

                const hasPermission = requiredPermissions.length > 0 && requiredPermissions.some(p => user.permissions.includes(p));
                if (hasPermission) {
                    return true;
                }

                return this.router.createUrlTree(['/app/dashboard']);
            })
        );
    }

    logout(redirect = true): void {
        // SEC-03: revoke the server-side refresh token and clear the HttpOnly cookies.
        // Local session state is cleared regardless of whether the backend call succeeds.
        this.http.post(`${environment.apiUrl}/auth/logout`, {}).subscribe({
            next: () => this.clearSession(redirect),
            error: () => this.clearSession(redirect)
        });
    }

    hasRole(role: string): boolean {
        const user = this.currentUserSignal();
        return !!user && user.roles.includes(role);
    }

    hasAnyRole(...roles: string[]): boolean {
        const user = this.currentUserSignal();
        if (!user) {
            return false;
        }

        return roles.some((role) => user.roles.includes(role));
    }

    hasPermission(permission: string): boolean {
        const user = this.currentUserSignal();
        if (!user) {
            return false;
        }

        if (user.roles.includes('Admin')) {
            return true;
        }

        return user.permissions.includes(permission);
    }

    private applySessionResponse(response: AuthSessionResponse): boolean {
        if (!response.user) {
            this.clearSession(false);
            return false;
        }

        this.setCurrentUser(this.mapUser(response.user));
        this.authErrorSignal.set(null);
        return true;
    }

    private setCurrentUser(user: User): void {
        localStorage.setItem(USER_KEY, JSON.stringify(user));
        this.currentUserSignal.set(user);
    }

    private clearSession(redirect = true): void {
        this.currentUserSignal.set(null);
        this.authErrorSignal.set(null);
        localStorage.removeItem(USER_KEY);

        if (redirect) {
            this.router.navigate(['/auth/login']);
        }
    }

    private readStoredUser(): User | null {
        const stored = localStorage.getItem(USER_KEY);
        if (!stored) {
            return null;
        }

        try {
            return JSON.parse(stored) as User;
        }
        catch {
            localStorage.removeItem(USER_KEY);
            return null;
        }
    }

    private mapUser(response: AuthenticatedUserResponse): User {
        return {
            id: response.id,
            name: response.name,
            email: response.email,
            role: response.primaryRole,
            roles: response.roles ?? [],
            permissions: response.permissions ?? []
        };
    }

}
