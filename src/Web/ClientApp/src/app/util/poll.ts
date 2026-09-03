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
}

const noopHandle: PollerHandle = { stop: () => {} };

export function startPoller<T>(
  fetch: () => Observable<T>,
  isDone: PollDecision<T>,
  onUpdate: (value: T) => void,
  onFinish: (outcome: PollOutcome<T>) => void,
  options: StartPollerOptions,
): PollerHandle {
  let stopped = false;
  let handle: number | null = null;

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
        onUpdate(value);
        if (isDone(value)) {
          stop();
          onFinish({ kind: 'done', value });
        }
      },
      error: (error) => {
        if (stopped) return;
        stop();
        onFinish({ kind: 'error', error });
      },
    });
  };

  handle = window.setInterval(tick, options.intervalMs);

  return {
    stop,
  };
}

export const passiveHandle: PollerHandle = noopHandle;
