import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { finalize, tap } from 'rxjs/operators';
import { LoadingService } from './loading.service';

export const loadingInterceptor: HttpInterceptorFn = (req, next) => {
    const loadingService = inject(LoadingService);

    // Skip loader for specific silent requests, such as polling metrics or audit
    if (req.headers.has('X-Skip-Loader') || req.url.includes('health')) {
        return next(req);
    }

    // Use setTimeout to avoid ExpressionChangedAfterItHasBeenCheckedError 
    // if signal updates immediately during CD.
    setTimeout(() => loadingService.show(), 0);

    return next(req).pipe(
        finalize(() => {
            setTimeout(() => loadingService.hide(), 0);
        })
    );
};
