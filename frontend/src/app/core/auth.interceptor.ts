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

    const request = attachBearerToken(req, authService.getAccessToken());

    return next(request).pipe(
        catchError((error) => {
            if (error.status !== 401 || isAuthRefreshBoundary(req.url)) {
                return throwError(() => error);
            }

            return authService.refreshSession().pipe(
                switchMap((token) => next(attachBearerToken(req, token))),
                catchError((refreshError) => {
                    authService.logout();
                    return throwError(() => refreshError);
                })
            );
        })
    );
};

function attachBearerToken(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
    if (!token || isAnonymousAuthRequest(request.url)) {
        return request;
    }

    return request.clone({
        setHeaders: {
            Authorization: `Bearer ${token}`
        }
    });
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
