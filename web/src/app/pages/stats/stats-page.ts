import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { catchError, of } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { Count, Stats } from '../../core/models';
import { BarChart } from '../../shared/bar-chart';
import { LoginLabelPipe, flag } from '../../shared/pipes';

@Component({
  selector: 'app-stats-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe, RouterLink, MatCardModule, MatTableModule, BarChart],
  templateUrl: './stats-page.html',
  styleUrl: './stats-page.scss',
})
export class StatsPage {
  private readonly api = inject(ApiService);
  private readonly loginLabel = new LoginLabelPipe();

  protected readonly stats = toSignal(
    this.api.stats().pipe(catchError(() => of<Stats | null>(null))),
  );
  protected readonly scanColumns = [
    'started',
    'kind',
    'engine',
    'targets',
    'open',
    'online',
    'new',
    'state',
  ];

  protected readonly tiles = computed(() => {
    const s = this.stats();
    if (!s) {
      return [];
    }
    return [
      { label: 'Servers', value: s.servers, link: '/servers' },
      { label: 'Online now', value: s.online, link: '/servers', params: { online: true } },
      { label: 'Players online', value: s.playersOnline },
      { label: 'Known players', value: s.knownPlayers, link: '/players' },
      { label: 'Java', value: s.java, link: '/servers', params: { edition: 'java' } },
      { label: 'Bedrock', value: s.bedrock, link: '/servers', params: { edition: 'bedrock' } },
      { label: 'New (24h)', value: s.newLast24h },
      { label: 'Seen (24h)', value: s.seenLast24h },
      {
        label: 'Query enabled',
        value: s.withQuery,
        link: '/servers',
        params: { hasPlugins: true },
      },
      { label: 'Modded', value: s.modded, link: '/servers', params: { modded: true } },
    ];
  });

  protected labels(
    items: Count[],
    kind: 'country' | 'login' | 'asn' | 'plain' = 'plain',
  ): string[] {
    return items.map((c) => {
      switch (kind) {
        case 'country':
          return `${flag(c.key)} ${c.label ?? c.key}`;
        case 'login':
          return this.loginLabel.transform(c.key);
        case 'asn':
          return `${c.key} ${c.label ?? ''}`.trim().slice(0, 40);
        default:
          return c.key;
      }
    });
  }

  protected values(items: Count[]): number[] {
    return items.map((c) => c.count);
  }

  protected height(items: Count[]): number {
    return Math.max(120, items.length * 24 + 40);
  }
}
