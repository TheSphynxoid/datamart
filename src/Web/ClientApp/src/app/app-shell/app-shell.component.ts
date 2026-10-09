import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from 'src/api-authorization/auth.service';
import { ThemeToggleComponent } from '../theme-toggle/theme-toggle.component';

@Component({
  standalone: false,
  selector: 'app-app-shell',
  templateUrl: './app-shell.component.html',
  styleUrls: ['./app-shell.component.scss']
})
export class AppShellComponent {
  drawerOpen = typeof window !== 'undefined' ? window.innerWidth >= 769 : true;
  isAuthenticated$ = this.authService.isAuthenticated$;

  readonly navItems = [
    { text: 'Dashboard', icon: 'home', route: '', exact: true },
    { text: 'Excel import', icon: 'upload', route: 'excelmap', exact: false },
    { text: 'Validation rules', icon: 'settings', route: 'rules', exact: false },
  ];

  constructor(private authService: AuthService, private router: Router) {}

  toggleDrawer(): void {
    this.drawerOpen = !this.drawerOpen;
  }

  closeNav(): void {
    if (typeof window !== 'undefined' && window.innerWidth < 769) {
      this.drawerOpen = false;
    }
  }

  navigate(route: string): void {
    this.router.navigate([route]);
    this.closeNav();
  }

  logout(): void {
    this.authService.logout().subscribe(() => this.router.navigate(['/login']));
  }
}
