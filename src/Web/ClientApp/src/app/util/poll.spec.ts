import { TestBed, fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import { of, throwError, Subject } from 'rxjs';
import { startPoller } from './poll';

describe('startPoller', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({});
  });

  it('polls at the requested interval and emits updates', fakeAsync(() => {
    const updates: number[] = [];
    const finishes: unknown[] = [];
    let counter = 0;

    const handle = startPoller(
      () => of(++counter),
      (n) => n >= 3,
      (n) => updates.push(n),
      (outcome) => finishes.push(outcome),
      { intervalMs: 100 },
    );

    tick(350);
    handle.stop();
    discardPeriodicTasks();

    expect(updates).toEqual([1, 2, 3]);
    expect(finishes.length).toBe(1);
    expect((finishes[0] as { kind: string }).kind).toBe('done');
  }));

  it('stops on the first error and reports it', fakeAsync(() => {
    const updates: number[] = [];
    const finishes: unknown[] = [];
    let calls = 0;

    const handle = startPoller(
      () => {
        calls++;
        return calls === 1 ? throwError(() => new Error('boom')) : of(42);
      },
      () => false,
      (n) => updates.push(n),
      (outcome) => finishes.push(outcome),
      { intervalMs: 100 },
    );

    tick(250);
    handle.stop();
    discardPeriodicTasks();

    expect(updates).toEqual([]);
    expect(finishes.length).toBe(1);
    expect((finishes[0] as { kind: string; error: Error }).kind).toBe('error');
    expect((finishes[0] as { kind: string; error: Error }).error.message).toBe('boom');
  }));

  it('stops polling when stop() is called', fakeAsync(() => {
    const updates: number[] = [];
    let counter = 0;

    const handle = startPoller(
      () => of(++counter),
      () => false,
      (n) => updates.push(n),
      () => {},
      { intervalMs: 100 },
    );

    tick(250);
    handle.stop();
    const seen = updates.length;
    tick(500);
    expect(updates.length).toBe(seen);
  }));

  it('does not emit updates after stop() even if a tick is in flight', fakeAsync(() => {
    const subject = new Subject<number>();
    const updates: number[] = [];
    const finishes: unknown[] = [];

    const handle = startPoller(
      () => subject.asObservable(),
      () => false,
      (n) => updates.push(n),
      (outcome) => finishes.push(outcome),
      { intervalMs: 100 },
    );

    tick(100);
    handle.stop();
    subject.next(1);
    subject.next(2);
    tick(100);

    expect(updates).toEqual([]);
    expect(finishes).toEqual([]);
  }));

  it('tolerates up to maxConsecutiveErrors transient failures', fakeAsync(() => {
    const updates: number[] = [];
    const finishes: unknown[] = [];
    let calls = 0;

    const handle = startPoller(
      () => {
        calls++;
        // First two calls fail, the third succeeds.
        return calls <= 2 ? throwError(() => new Error('transient')) : of(42);
      },
      () => false,
      (n) => updates.push(n),
      (outcome) => finishes.push(outcome),
      { intervalMs: 100, maxConsecutiveErrors: 3 },
    );

    tick(350);
    handle.stop();
    discardPeriodicTasks();

    expect(updates).toEqual([42]);
    expect(finishes).toEqual([]);
  }));

  it('a success resets the consecutive-error counter', fakeAsync(() => {
    const finishes: unknown[] = [];
    let calls = 0;

    const handle = startPoller(
      () => {
        calls++;
        // Fail on every odd call: errors never accumulate consecutively.
        return calls % 2 === 1 ? throwError(() => new Error('flaky')) : of(calls);
      },
      () => false,
      () => {},
      (outcome) => finishes.push(outcome),
      { intervalMs: 100, maxConsecutiveErrors: 2 },
    );

    tick(550);
    handle.stop();
    discardPeriodicTasks();

    expect(finishes).toEqual([]);
  }));

  it('stops and reports once consecutive errors reach the tolerance', fakeAsync(() => {
    const finishes: unknown[] = [];
    let calls = 0;

    const handle = startPoller(
      () => {
        calls++;
        return throwError(() => new Error(`down-${calls}`));
      },
      () => false,
      () => {},
      (outcome) => finishes.push(outcome),
      { intervalMs: 100, maxConsecutiveErrors: 3 },
    );

    tick(350);
    handle.stop();
    discardPeriodicTasks();

    expect(calls).toBe(3);
    expect(finishes.length).toBe(1);
    expect((finishes[0] as { kind: string; error: Error }).kind).toBe('error');
    expect((finishes[0] as { kind: string; error: Error }).error.message).toBe('down-3');
  }));
});
