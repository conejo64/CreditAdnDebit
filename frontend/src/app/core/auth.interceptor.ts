import { HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
    const authService = inject(AuthService);

    if (!isApiRequest(req.url)) {
        return next(req);
    }

    // SEC-03: the access/refresh tokens are HttpOnly cookies — withCredentials makes the
    // browser attach them automatically. There is no Authorization header to build here.
    const request = withCredentialsRequest(req);

    return next(request).pipe(
        catchError((error) => {
            if (error.status !== 401 || isAuthRefreshBoundary(req.url)) {
                return throwError(() => error);
            }

            return authService.refreshSession().pipe(
                switchMap(() => next(withCredentialsRequest(req))),
                catchError((refreshError) => {
                    authService.logout();
                    return throwError(() => refreshError);
                })
            );
        })
    );
};

function withCredentialsRequest(request: HttpRequest<unknown>): HttpRequest<unknown> {
    return request.clone({ withCredentials: true });
}

function isApiRequest(url: string): boolean {
    return url.startsWith(environment.apiUrl) || url.startsWith(environment.isoSwitchUrl);
}

function isAnonymousAuthRequest(url: string): boolean {
    return url.includes('/auth/login') ||
        url.includes('/auth/register') ||
        url.includes('/auth/mfa/enable') ||
        url.includes('/auth/mfa/verify') ||
        url.includes('/auth/refresh');
}

function isAuthRefreshBoundary(url: string): boolean {
    return isAnonymousAuthRequest(url);
}
