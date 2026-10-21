import { DatePipe, DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import {
  BehaviorSubject,
  catchError,
  debounceTime,
  distinctUntilChanged,
  of,
  switchMap,
} from 'rxjs';
import { ApiService } from '../../core/api.service';
import { Paged, Player } from '../../core/models';
import { TimeAgoPipe } from '../../shared/pipes';
import { pixelAvatar } from '../../shared/avatar';

@Component({
  selector: 'app-player-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    DecimalPipe,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatPaginatorModule,
    MatTableModule,
    TimeAgoPipe,
  ],
  template: `
    <div class="page-head">
      <div>
        <h1>Players</h1>
        <p>Names seen in server list samples and query responses</p>
      </div>
    </div>
    <mat-form-field class="search" appearance="outline" subscriptSizing="dynamic">
      <mat-label>Player name (prefix) or UUID</mat-label>
      <input matInput type="search" maxlength="36" (input)="search($any($event.target).value)" />
    </mat-form-field>

    <div class="table-wrap panel">
      <table mat-table [dataSource]="result()?.items ?? []">
        <ng-container matColumnDef="name">
          <th mat-header-cell *matHeaderCellDef>Name</th>
          <td mat-cell *matCellDef="let p">
            <a class="player" [routerLink]="['/players', p.id]"
              ><img [src]="avatar(p.name)" alt="" width="20" height="20" />{{ p.name }}</a
            >
          </td>
        </ng-container>
        <ng-container matColumnDef="uuid">
          <th mat-header-cell *matHeaderCellDef>UUID</th>
          <td mat-cell *matCellDef="let p" class="mono">{{ p.uuid ?? '—' }}</td>
        </ng-container>
        <ng-container matColumnDef="servers">
          <th mat-header-cell *matHeaderCellDef>Servers</th>
          <td mat-cell *matCellDef="let p">{{ p.serverCount | number }}</td>
        </ng-container>
        <ng-container matColumnDef="firstSeen">
          <th mat-header-cell *matHeaderCellDef>First seen</th>
          <td mat-cell *matCellDef="let p">{{ p.firstSeenUtc | date: 'short' }}</td>
        </ng-container>
        <ng-container matColumnDef="lastSeen">
          <th mat-header-cell *matHeaderCellDef>Last seen</th>
          <td mat-cell *matCellDef="let p">{{ p.lastSeenUtc | timeAgo }}</td>
        </ng-container>
        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns"></tr>
        <tr class="mat-mdc-row" *matNoDataRow>
          <td class="empty" [attr.colspan]="columns.length">No players found.</td>
        </tr>
      </table>
    </div>
    <mat-paginator
      [length]="result()?.total ?? 0"
      [pageIndex]="page$.value.page - 1"
      [pageSize]="page$.value.size"
      [pageSizeOptions]="[25, 50, 100]"
      (page)="onPage($event)"
    />
  `,
  styles: `
    .player {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      font-weight: 600;
    }
    .player img {
      border-radius: 4px;
      image-rendering: pixelated;
    }
    .search {
      width: 100%;
      max-width: 480px;
      margin-bottom: 12px;
    }
    .table-wrap {
      overflow-x: auto;
    }
    table {
      width: 100%;
    }
    .mono {
      font-family: ui-monospace, Consolas, monospace;
      font-size: 12px;
    }
    .empty {
      padding: 24px;
      text-align: center;
      opacity: 0.7;
    }
  `,
})
export class PlayerList implements OnInit {
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly query$ = new BehaviorSubject('');
  protected readonly page$ = new BehaviorSubject({ page: 1, size: 25 });
  protected readonly columns = ['name', 'uuid', 'servers', 'firstSeen', 'lastSeen'];
  protected readonly result = signal<Paged<Player> | null>(null);

  ngOnInit(): void {
    this.query$
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        switchMap((q) => {
          // A new search starts at page 1.
          this.page$.next({ ...this.page$.value, page: 1 });
          return this.page$.pipe(
            switchMap((p) => this.api.players(q, p.page, p.size).pipe(catchError(() => of(null)))),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.result.set(result));
  }

  /** A deterministic pixel "head" so players are easy to tell apart (generated locally, no skin lookups). */
  protected avatar(name: string): string {
    return pixelAvatar(name);
  }

  protected search(value: string): void {
    this.query$.next(value.trim());
  }

  protected onPage(event: PageEvent): void {
    this.page$.next({ page: event.pageIndex + 1, size: event.pageSize });
  }
}
