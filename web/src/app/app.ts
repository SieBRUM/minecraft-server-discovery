import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ThemeService } from './core/theme.service';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatTooltipModule],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly theme = inject(ThemeService);
  protected readonly themeLabel = computed(
    () =>
      ({ system: 'Theme: follows your system', light: 'Theme: light', dark: 'Theme: dark' })[
        this.theme.mode()
      ],
  );
  protected readonly links = [
    { path: '/servers', label: 'Servers' },
    { path: '/map', label: 'Map' },
    { path: '/players', label: 'Players' },
    { path: '/stats', label: 'Stats' },
    { path: '/about', label: 'About' },
  ];
}
