import { Observable } from 'rxjs';

export type PollOutcome<T> =
  | { kind: 'done'; value: T }
  | { kind: 'error'; error: unknown };

export type PollDecision<T> = (value: T) => boolean;

export interface PollerHandle {
  stop(): void;
}

export interface StartPollerOptions {
  intervalMs: number;
  /**
   * How many consecutive failed fetches the poller tolerates before it stops
   * and reports { kind: 'error' }. Defaults to 1: any single error is terminal
   * (right for import/validation polling, where an error means the job is gone).
   * Long-lived dashboards pass a higher value so one dropped request does not
   * silently kill auto-refresh.
   */
  maxConsecutiveErrors?: number;
}

const noopHandle: PollerHandle = { stop: () => {} };

export function startPoller<T>(
  fetch: () => Observable<T>,
  isDone: PollDecision<T>,
  onUpdate: (value: T) => void,
  onFinish: (outcome: PollOutcome<T>) => void,
  options: StartPollerOptions,
): PollerHandle {
  const maxConsecutiveErrors = options.maxConsecutiveErrors ?? 1;
  let stopped = false;
  let handle: number | null = null;
  let consecutiveErrors = 0;

  const stop = (): void => {
    if (stopped) return;
    stopped = true;
    if (handle !== null) {
      window.clearInterval(handle);
      handle = null;
    }
  };

  const tick = (): void => {
    if (stopped) return;
    fetch().subscribe({
      next: (value) => {
        if (stopped) return;
        consecutiveErrors = 0;
        onUpdate(value);
        if (isDone(value)) {
          stop();
          onFinish({ kind: 'done', value });
        }
      },
      error: (error) => {
        if (stopped) return;
        consecutiveErrors++;
        if (consecutiveErrors >= maxConsecutiveErrors) {
          stop();
          onFinish({ kind: 'error', error });
        }
        // else: transient failure — keep the interval running and let the
        // next tick retry.
      },
    });
  };

  handle = window.setInterval(tick, options.intervalMs);

  return {
    stop,
  };
}

export const passiveHandle: PollerHandle = noopHandle;
