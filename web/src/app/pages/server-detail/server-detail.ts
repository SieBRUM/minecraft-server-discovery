import { DatePipe, DecimalPipe, PercentPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { catchError, combineLatest, map, of, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { ServerDetail as Detail, Snapshot } from '../../core/models';
import { Favicon } from '../../shared/favicon';
import { Motd } from '../../shared/motd';
import { FlagPipe, LoginLabelPipe, TimeAgoPipe } from '../../shared/pipes';
import { HistoryChart } from './history-chart';

type Loaded<T> = { state: 'loading' } | { state: 'error' } | { state: 'ok'; value: T };

@Component({
  selector: 'app-server-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    DecimalPipe,
    PercentPipe,
    RouterLink,
    MatButtonModule,
    MatButtonToggleModule,
    MatCardModule,
    MatTableModule,
    Favicon,
    Motd,
    FlagPipe,
    LoginLabelPipe,
    TimeAgoPipe,
    HistoryChart,
  ],
  templateUrl: './server-detail.html',
  styleUrl: './server-detail.scss',
})
export class ServerDetail {
  private readonly api = inject(ApiService);

  /** Route parameter (bound by withComponentInputBinding). */
  readonly id = input.required<string>();
  protected readonly days = signal(30);
  protected readonly copied = signal(false);

  private readonly serverId = computed(() => Number.parseInt(this.id(), 10));

  protected readonly detail = toSignal(
    toObservable(this.serverId).pipe(
      switchMap((id) =>
        Number.isSafeInteger(id) && id > 0
          ? this.api.server(id).pipe(
              map((value): Loaded<Detail> => ({ state: 'ok', value })),
              catchError(() => of<Loaded<Detail>>({ state: 'error' })),
            )
          : of<Loaded<Detail>>({ state: 'error' }),
      ),
    ),
    { initialValue: { state: 'loading' } as Loaded<Detail> },
  );

  protected readonly history = toSignal(
    combineLatest([toObservable(this.serverId), toObservable(this.days)]).pipe(
      switchMap(([id, days]) =>
        Number.isSafeInteger(id) && id > 0
          ? this.api.history(id, days).pipe(catchError(() => of<Snapshot[]>([])))
          : of<Snapshot[]>([]),
      ),
    ),
    { initialValue: [] as Snapshot[] },
  );

  protected readonly uptime = computed(() => {
    const points = this.history();
    return points.length ? points.filter((p) => p.online).length / points.length : null;
  });

  protected readonly playerColumns = ['name', 'uuid', 'firstSeen', 'lastSeen', 'timesSeen'];

  protected mapLink(d: Detail): string | null {
    if (d.latitude === null || d.longitude === null) {
      return null;
    }
    const lat = encodeURIComponent(d.latitude);
    const lon = encodeURIComponent(d.longitude);
    return `https://www.openstreetmap.org/?mlat=${lat}&mlon=${lon}#map=9/${lat}/${lon}`;
  }

  protected async copy(text: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 1500);
    } catch {
      // clipboard not available (e.g. plain http on a non-localhost host)
    }
  }
}
