import { Injectable, signal } from '@angular/core';
import themes from 'devextreme/ui/themes';

export type Theme = 'auto' | 'light' | 'dark';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly STORAGE_KEY = 'picoColorScheme';

  private readonly prefersDark = window.matchMedia?.('(prefers-color-scheme: dark)') ?? null;

  theme = signal<Theme>('auto');

  constructor() {
    const stored = localStorage.getItem(this.STORAGE_KEY) as Theme | null;
    const initial: Theme = stored ?? 'auto';
    this.theme.set(initial);
    this.applyTheme(initial);

    this.prefersDark?.addEventListener('change', (e) => {
      if (this.theme() === 'auto') {
        this.applyDevExtreme(e.matches ? 'dark' : 'light');
      }
    });
  }

  setTheme(value: Theme): void {
    this.theme.set(value);
    localStorage.setItem(this.STORAGE_KEY, value);
    this.applyTheme(value);
  }

  private applyTheme(theme: Theme): void {
    if (theme === 'auto') {
      document.documentElement.removeAttribute('data-theme');
    } else {
      document.documentElement.setAttribute('data-theme', theme);
    }

    const resolved = theme === 'auto'
      ? (this.prefersDark?.matches ? 'dark' : 'light')
      : theme;
    this.applyDevExtreme(resolved);
  }

  private applyDevExtreme(scheme: 'light' | 'dark'): void {
    themes.current(`fluent.blue.${scheme}`);
  }
}
