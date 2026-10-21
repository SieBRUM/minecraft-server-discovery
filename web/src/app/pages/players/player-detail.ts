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
import { pixelAvatar } from '../../shared/avatar';

@Component({
  selector: 'app-player-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe, RouterLink, MatCardModule, MatTableModule, TimeAgoPipe],
  template: `
    @if (detail(); as d) {
      @if (d === 'error') {
        <p class="error" role="alert">Player not found.</p>
      } @else {
        <div class="head">
          <img [src]="avatar(d.player.name)" alt="" width="56" height="56" />
          <div>
            <h1>{{ d.player.name }}</h1>
            <span class="muted mono">{{
              d.player.uuid ?? 'no UUID (seen via the query protocol)'
            }}</span>
          </div>
        </div>
        <p>
          Seen on {{ d.player.serverCount | number }} server(s), first
          {{ d.player.firstSeenUtc | date: 'medium' }}, last {{ d.player.lastSeenUtc | timeAgo }}.
        </p>
        <div class="table-wrap panel">
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
              <td mat-cell *matCellDef="let s">
                <span class="pill" [class.ok]="s.isOnline">{{
                  s.isOnline ? 'online' : 'offline'
                }}</span>
              </td>
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
    .head {
      display: flex;
      gap: 16px;
      align-items: center;
      margin: 8px 0 16px;
    }
    .head img {
      border-radius: 12px;
      image-rendering: pixelated;
      box-shadow: var(--app-shadow);
    }
    .head h1 {
      margin: 0 0 4px;
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
      color: var(--app-danger);
    }
  `,
})
export class PlayerDetail {
  private readonly api = inject(ApiService);
  readonly id = input.required<string>();
  protected readonly columns = ['server', 'motd', 'status', 'firstSeen', 'lastSeen', 'times'];
  protected avatar(name: string): string {
    return pixelAvatar(name);
  }

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
