import { TestBed } from '@angular/core/testing';
import { HttpClient, HTTP_INTERCEPTORS } from '@angular/common/http';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';

import { XsrfInterceptor } from './xsrf.interceptor';

describe('XsrfInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [
        { provide: HTTP_INTERCEPTORS, useClass: XsrfInterceptor, multi: true },
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  /** Seeds the interceptor cache, returning when the seed request completes. */
  function seedCache(token: string, url = '/api/seed'): void {
    http.post(url, {}).subscribe();
    controller.expectOne('/api/antiforgery').flush({ requestToken: token });
    controller.expectOne(url).flush({});
  }

  it('primes on cache miss and attaches the token', () => {
    http.post('/api/Import/validate', {}).subscribe();

    controller.expectOne('/api/antiforgery').flush({ requestToken: 'tok-1' });
    const req = controller.expectOne(r => r.url === '/api/Import/validate');
    expect(req.request.headers.get('RequestVerificationToken')).toBe('tok-1');
    req.flush({});
  });

  it('reuses the cached token without re-priming', () => {
    seedCache('cached');

    http.post('/api/other', {}).subscribe();
    // No second prime: the only outstanding request is the POST itself.
    const req = controller.expectOne(r => r.url === '/api/other');
    expect(req.request.headers.get('RequestVerificationToken')).toBe('cached');
    req.flush({});
  });

  it('recovers from a stale cached token with one re-prime', () => {
    seedCache('stale');

    let outcome = 'pending';
    http.post('/api/b', {}).subscribe({
      next: () => outcome = 'ok',
      error: () => outcome = 'error',
    });

    const rejected = controller.expectOne(r => r.url === '/api/b');
    expect(rejected.request.headers.get('RequestVerificationToken')).toBe('stale');
    rejected.flush(
      { type: 'https://datamart/antiforgery', title: 'Invalid anti-forgery token.', status: 400 },
      { status: 400, statusText: 'Bad Request' }
    );

    controller.expectOne('/api/antiforgery').flush({ requestToken: 'fresh' });
    const retry = controller.expectOne(r => r.url === '/api/b');
    expect(retry.request.headers.get('RequestVerificationToken')).toBe('fresh');
    retry.flush({});
    expect(outcome).toBe('ok');
  });

  it('surfaces the error when the retry also fails', () => {
    seedCache('stale');

    let outcome = 'pending';
    http.post('/api/b', {}).subscribe({
      next: () => outcome = 'ok',
      error: () => outcome = 'error',
    });

    controller.expectOne(r => r.url === '/api/b').flush(
      { type: 'https://datamart/antiforgery', title: 'Invalid anti-forgery token.', status: 400 },
      { status: 400, statusText: 'Bad Request' }
    );
    controller.expectOne('/api/antiforgery').flush({ requestToken: 'fresh' });
    controller.expectOne(r => r.url === '/api/b').flush(
      { title: 'Still broken' },
      { status: 500, statusText: 'Server Error' }
    );
    expect(outcome).toBe('error');
  });

  it('does not retry non-antiforgery 400s', () => {
    seedCache('cached');

    let outcome = 'pending';
    http.post('/api/c', {}).subscribe({
      next: () => outcome = 'ok',
      error: () => outcome = 'error',
    });

    controller.expectOne(r => r.url === '/api/c').flush(
      { title: 'Validation failed' },
      { status: 400, statusText: 'Bad Request' }
    );
    expect(outcome).toBe('error');
    // No re-prime happened: verify() in afterEach would fail on it.
  });

  it('passes GETs through untouched', () => {
    http.get('/api/entity-rules?targetEntity=X').subscribe();

    const req = controller.expectOne(r => r.url.startsWith('/api/entity-rules'));
    expect(req.request.headers.has('RequestVerificationToken')).toBeFalse();
    req.flush([]);
  });
});
