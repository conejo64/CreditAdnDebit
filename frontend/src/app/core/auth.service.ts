import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, UrlTree } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { catchError, finalize, map, shareReplay, switchMap, tap } from 'rxjs/operators';
import { environment } from '../../environments/environment';

const ACCESS_TOKEN_KEY = 'auth.access_token';
const REFRESH_TOKEN_KEY = 'auth.refresh_token';
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
    accessToken: string | null;
    refreshToken: string | null;
    message: string | null;
    user: AuthenticatedUserResponse | null;
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

    private refreshRequest$: Observable<string> | null = null;

    constructor() {
        if (!this.getAccessToken()) {
            this.clearSession(false);
        }
    }

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

    refreshSession(): Observable<string> {
        const refreshToken = this.getRefreshToken();
        if (!refreshToken) {
            return throwError(() => new Error('No refresh token available.'));
        }

        if (!this.refreshRequest$) {
            this.refreshRequest$ = this.http
                .post<AuthSessionResponse>(`${environment.apiUrl}/auth/refresh`, { refreshToken })
                .pipe(
                    map((response) => {
                        if (!this.applySessionResponse(response)) {
                            throw new Error('Invalid refresh response.');
                        }

                        return response.accessToken as string;
                    }),
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

    ensureAuthenticated(): Observable<boolean | UrlTree> {
        const accessToken = this.getAccessToken();
        if (!accessToken) {
            this.clearSession(false);
            return of(this.router.createUrlTree(['/auth/login']));
        }

        if (this.currentUserSignal() && !this.isTokenExpired(accessToken)) {
            return of(true);
        }

        if (this.isTokenExpired(accessToken) && this.getRefreshToken()) {
            return this.refreshSession().pipe(
                switchMap(() => this.loadCurrentUser()),
                map(() => true),
                catchError(() => {
                    this.clearSession();
                    return of(this.router.createUrlTree(['/auth/login']));
                })
            );
        }

        return this.loadCurrentUser().pipe(
            map(() => true),
            catchError(() => {
                this.clearSession();
                return of(this.router.createUrlTree(['/auth/login']));
            })
        );
    }

    ensureAuthorized(allowedRoles: string[]): Observable<boolean | UrlTree> {
        return this.ensureAuthenticated().pipe(
            map((result) => {
                if (result !== true) {
                    return result;
                }

                if (allowedRoles.length === 0) {
                    return true;
                }

                const user = this.currentUserSignal();
                if (user && allowedRoles.some((role) => user.roles.includes(role))) {
                    return true;
                }

                return this.router.createUrlTree(['/app/dashboard']);
            })
        );
    }

    logout(redirect = true): void {
        this.clearSession(redirect);
    }

    getAccessToken(): string | null {
        return localStorage.getItem(ACCESS_TOKEN_KEY);
    }

    getRefreshToken(): string | null {
        return localStorage.getItem(REFRESH_TOKEN_KEY);
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
        if (!response.accessToken || !response.refreshToken || !response.user) {
            this.clearSession(false);
            return false;
        }

        const user = this.mapUser(response.user);
        localStorage.setItem(ACCESS_TOKEN_KEY, response.accessToken);
        localStorage.setItem(REFRESH_TOKEN_KEY, response.refreshToken);
        localStorage.setItem(USER_KEY, JSON.stringify(user));
        this.currentUserSignal.set(user);
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
        localStorage.removeItem(ACCESS_TOKEN_KEY);
        localStorage.removeItem(REFRESH_TOKEN_KEY);
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

    private isTokenExpired(token: string): boolean {
        try {
            const payload = JSON.parse(atob(token.split('.')[1] ?? ''));
            if (typeof payload.exp !== 'number') {
                return false;
            }

            return payload.exp * 1000 <= Date.now();
        }
        catch {
            return true;
        }
    }
}
