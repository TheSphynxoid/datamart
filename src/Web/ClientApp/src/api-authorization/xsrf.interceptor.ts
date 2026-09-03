import { Injectable } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler, HttpEvent, HttpBackend, HttpResponse, HttpEventType } from '@angular/common/http';
import { Observable, switchMap, catchError, filter, of } from 'rxjs';

/**
 * Adds the ASP.NET Core antiforgery token to outgoing requests so the server
 * can verify they were issued by this app (CSRF protection).
 *
 * The antiforgery system has two halves:
 * - the <em>cookie token</em> (the secret) stored in the `XSRF-TOKEN` cookie, and
 * - the <em>request token</em> (derived from the cookie token) that must be sent back
 *   on each state-changing request.
 *
 * The `XSRF-TOKEN` cookie is HttpOnly (the framework default) — JS can't read it. The
 * `/api/antiforgery` prime endpoint returns the matching request token in the response
 * body. This interceptor caches that token and echoes it back in the
 * `RequestVerificationToken` header on same-origin state-changing requests.
 *
 * Sending the cookie value as the header would be wrong: the cookie holds the secret,
 * the header needs the derived request token. Swapping them is exactly what the
 * framework rejects with "The cookie token and the request token were swapped".
 *
 * Self-heal: if a state-changing request goes out without a cached token, this
 * interceptor primes the token via GET /api/antiforgery and retries once. The prime
 * itself is GET, which doesn't need the header, and it returns the fresh request token.
 */
@Injectable({
  providedIn: 'root'
})
export class XsrfInterceptor implements HttpInterceptor {
  private static readonly headerName = 'RequestVerificationToken';
  private static readonly primeUrl = '/api/antiforgery';

  private cachedRequestToken: string | null = null;

  constructor(private backend: HttpBackend) {}

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    if (this.isCrossOrigin(req)) {
      return next.handle(req);
    }

    if (!this.isStateChanging(req)) {
      return next.handle(req);
    }

    if (this.cachedRequestToken) {
      return next.handle(this.withToken(req, this.cachedRequestToken));
    }

    return this.primeAndRetry(req, next);
  }

  private withToken(req: HttpRequest<any>, token: string): HttpRequest<any> {
    return req.clone({
      setHeaders: { [XsrfInterceptor.headerName]: token },
    });
  }

  private primeAndRetry(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    // Use HttpBackend (not HttpClient) to bypass the interceptor chain and avoid a
    // circular dependency: HttpClient -> HTTP_INTERCEPTORS -> XsrfInterceptor -> HttpClient.
    //
    // Structure: the prime GET runs as a separate observable. Its success feeds into a
    // switchMap that returns the original request observable. The catchError sits
    // BEFORE the switchMap, so it only catches errors from the prime — errors from
    // the original request propagate normally to the subscriber.
    return this.backend.handle(new HttpRequest('GET', XsrfInterceptor.primeUrl, {
      withCredentials: true,
    })).pipe(
      filter((event: HttpEvent<any>) => event.type === HttpEventType.Response),
      switchMap((event: HttpResponse<{ requestToken: string }>) => {
        const token = (event.body as any)?.requestToken;
        if (!token) {
          return next.handle(req);
        }
        this.cachedRequestToken = token;
        // Return the original request as the next observable. Errors here will
        // surface to the subscriber untouched (no surrounding catchError).
        return next.handle(this.withToken(req, token));
      })
      // NOTE: no catchError here on purpose. Catching the inner next.handle would
      // re-issue the request without the token and mislabel the error as "prime failed".
    );
  }

  private isStateChanging(req: HttpRequest<any>): boolean {
    const method = req.method.toUpperCase();
    return method === 'POST' || method === 'PUT' || method === 'PATCH' || method === 'DELETE';
  }

  private isCrossOrigin(req: HttpRequest<any>): boolean {
    try {
      const requestOrigin = new URL(req.url, window.location.origin).origin;
      return requestOrigin !== window.location.origin;
    } catch {
      return true;
    }
  }
}