import { Injectable } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler, HttpEvent } from '@angular/common/http';
import { Observable } from 'rxjs';

/**
 * Adds the ASP.NET Core antiforgery token to outgoing requests so the server
 * can verify they were issued by this app (CSRF protection).
 *
 * The antiforgery cookie is set by the server as `XSRF-TOKEN` (SameSite=Strict).
 * This interceptor reads the cookie and copies its value into the
 * `RequestVerificationToken` request header on same-origin requests. Cross-site
 * requests cannot read the cookie (Strict + browser policy), so they cannot
 * reproduce the header.
 */
@Injectable({
  providedIn: 'root'
})
export class XsrfInterceptor implements HttpInterceptor {
  private static readonly cookieName = 'XSRF-TOKEN';
  private static readonly headerName = 'RequestVerificationToken';

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    const token = this.readCookie(XsrfInterceptor.cookieName);
    if (!token) {
      return next.handle(req);
    }

    // Don't attach the token to cross-origin requests (e.g. the auth provider
    // talking to an external IdentityServer). The interceptor's job is to
    // protect same-origin POSTs.
    if (this.isCrossOrigin(req)) {
      return next.handle(req);
    }

    const authReq = req.clone({
      setHeaders: { [XsrfInterceptor.headerName]: token },
    });
    return next.handle(authReq);
  }

  private readCookie(name: string): string | null {
    if (typeof document === 'undefined' || !document.cookie) {
      return null;
    }
    const prefix = `${name}=`;
    const parts = document.cookie.split(';');
    for (const raw of parts) {
      const trimmed = raw.trim();
      if (trimmed.startsWith(prefix)) {
        return decodeURIComponent(trimmed.substring(prefix.length));
      }
    }
    return null;
  }

  private isCrossOrigin(req: HttpRequest<any>): boolean {
    // If the request URL is relative, it is same-origin with the page that
    // issued it; treat as same-origin.
    try {
      const requestOrigin = new URL(req.url, window.location.origin).origin;
      return requestOrigin !== window.location.origin;
    } catch {
      // If the URL can't be parsed, fall back to a conservative cross-origin
      // assumption so we never leak the token to an unexpected host.
      return true;
    }
  }
}
