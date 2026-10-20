import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule],
  template: `
    <mat-toolbar class="toolbar">
      <a routerLink="/" class="brand">MC Discovery</a>
      <nav>
        @for (link of links; track link.path) {
          <a mat-button [routerLink]="link.path" routerLinkActive="active">{{ link.label }}</a>
        }
      </nav>
    </mat-toolbar>
    <main><router-outlet /></main>
    <footer>
      Research tool ·
      <a href="https://db-ip.com" target="_blank" rel="noopener noreferrer"
        >IP Geolocation by DB-IP</a
      >
      · <a routerLink="/about">About &amp; credits</a>
    </footer>
  `,
  styleUrl: './app.scss',
})
export class App {
  protected readonly links = [
    { path: '/servers', label: 'Servers' },
    { path: '/map', label: 'Map' },
    { path: '/players', label: 'Players' },
    { path: '/stats', label: 'Stats' },
    { path: '/about', label: 'About' },
  ];
}
