import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { catchError, map, of, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { PlayerDetail as Detail } from '../../core/models';
import { TimeAgoPipe } from '../../shared/pipes';

@Component({
  selector: 'app-player-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe, RouterLink, MatCardModule, MatTableModule, TimeAgoPipe],
  template: `
    @if (detail(); as d) {
      @if (d === 'error') {
        <p class="error" role="alert">Player not found.</p>
      } @else {
        <h1>{{ d.player.name }}</h1>
        <p class="muted mono">{{ d.player.uuid ?? 'no UUID (seen via the query protocol)' }}</p>
        <p>
          Seen on {{ d.player.serverCount | number }} server(s), first
          {{ d.player.firstSeenUtc | date: 'medium' }}, last {{ d.player.lastSeenUtc | timeAgo }}.
        </p>
        <div class="table-wrap">
          <table mat-table [dataSource]="d.servers">
            <ng-container matColumnDef="server">
              <th mat-header-cell *matHeaderCellDef>Server</th>
              <td mat-cell *matCellDef="let s">
                <a [routerLink]="['/servers', s.serverId]">{{ s.address }}:{{ s.port }}</a>
              </td>
            </ng-container>
            <ng-container matColumnDef="motd">
              <th mat-header-cell *matHeaderCellDef>MOTD</th>
              <td mat-cell *matCellDef="let s" class="motd">{{ s.motdPlain }}</td>
            </ng-container>
            <ng-container matColumnDef="status">
              <th mat-header-cell *matHeaderCellDef>Status</th>
              <td mat-cell *matCellDef="let s">{{ s.isOnline ? 'online' : 'offline' }}</td>
            </ng-container>
            <ng-container matColumnDef="firstSeen">
              <th mat-header-cell *matHeaderCellDef>First seen here</th>
              <td mat-cell *matCellDef="let s">{{ s.firstSeenUtc | date: 'short' }}</td>
            </ng-container>
            <ng-container matColumnDef="lastSeen">
              <th mat-header-cell *matHeaderCellDef>Last seen here</th>
              <td mat-cell *matCellDef="let s">{{ s.lastSeenUtc | timeAgo }}</td>
            </ng-container>
            <ng-container matColumnDef="times">
              <th mat-header-cell *matHeaderCellDef>Times</th>
              <td mat-cell *matCellDef="let s">{{ s.timesSeen | number }}</td>
            </ng-container>
            <tr mat-header-row *matHeaderRowDef="columns"></tr>
            <tr mat-row *matRowDef="let row; columns: columns"></tr>
          </table>
        </div>
      }
    } @else {
      <p>Loading…</p>
    }
  `,
  styles: `
    .muted {
      opacity: 0.7;
    }
    .mono {
      font-family: ui-monospace, Consolas, monospace;
    }
    .table-wrap {
      overflow-x: auto;
    }
    table {
      width: 100%;
    }
    .motd {
      max-width: 420px;
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class PlayerDetail {
  private readonly api = inject(ApiService);
  readonly id = input.required<string>();
  protected readonly columns = ['server', 'motd', 'status', 'firstSeen', 'lastSeen', 'times'];
  private readonly playerId = computed(() => Number.parseInt(this.id(), 10));

  protected readonly detail = toSignal(
    toObservable(this.playerId).pipe(
      switchMap((id) =>
        Number.isSafeInteger(id) && id > 0
          ? this.api.player(id).pipe(
              map((d): Detail | 'error' => d),
              catchError(() => of<'error'>('error')),
            )
          : of<'error'>('error'),
      ),
    ),
  );
}
