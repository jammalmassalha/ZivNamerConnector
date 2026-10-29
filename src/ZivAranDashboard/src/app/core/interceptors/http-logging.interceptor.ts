import { HttpErrorResponse, HttpEvent, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { tap } from 'rxjs';

/** Logs every API request (method + URL + body) and its response or error. */
export const httpLoggingInterceptor: HttpInterceptorFn = (req, next) => {
  const started = performance.now();
  console.log(`[HTTP →] ${req.method} ${req.urlWithParams}`, req.body ?? '');

  return next(req).pipe(
    tap({
      next: (event: HttpEvent<unknown>) => {
        if (event instanceof HttpResponse) {
          console.log(
            `[HTTP ←] ${req.method} ${req.urlWithParams} ${event.status} (${Math.round(performance.now() - started)}ms)`,
            event.body
          );
        }
      },
      error: (err: HttpErrorResponse) => {
        console.error(
          `[HTTP ✖] ${req.method} ${req.urlWithParams} ${err.status} ${err.statusText}`,
          err.error ?? err.message
        );
      },
    })
  );
};
