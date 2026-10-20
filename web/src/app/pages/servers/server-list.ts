import { DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  Subject,
  catchError,
  debounceTime,
  distinctUntilChanged,
  map,
  of,
  switchMap,
  tap,
} from 'rxjs';
import { ApiService } from '../../core/api.service';
import { Facets, Paged, ServerFilter, ServerSummary } from '../../core/models';
import { Favicon } from '../../shared/favicon';
import { Motd } from '../../shared/motd';
import { FlagPipe, TimeAgoPipe } from '../../shared/pipes';
import { filterFromParams, filterToParams } from './filter-params';

@Component({
  selector: 'app-server-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DecimalPipe,
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatSelectModule,
    MatSortModule,
    MatTableModule,
    MatTooltipModule,
    Favicon,
    Motd,
    FlagPipe,
    TimeAgoPipe,
  ],
  templateUrl: './server-list.html',
  styleUrl: './server-list.scss',
})
export class ServerList implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly search$ = new Subject<string>();

  protected readonly columns = [
    'icon',
    'address',
    'motd',
    'version',
    'players',
    'country',
    'software',
    'auth',
    'lastSeen',
  ];
  protected readonly filter = signal<ServerFilter>({});
  protected readonly result = signal<Paged<ServerSummary> | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly facets = toSignal(
    this.api.facets().pipe(catchError(() => of<Facets | null>(null))),
    {
      initialValue: null,
    },
  );
  protected readonly hasFilters = computed(() => {
    const { sort, dir, page, pageSize, ...rest } = this.filter();
    return Object.keys(rest).length > 0;
  });

  ngOnInit(): void {
    this.route.queryParamMap
      .pipe(
        map(filterFromParams),
        tap((filter) => {
          this.filter.set(filter);
          this.loading.set(true);
          this.error.set(null);
        }),
        switchMap((filter) =>
          this.api.servers(filter).pipe(
            catchError(() => {
              this.error.set('Could not load servers. Is the API running?');
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        this.result.set(result);
        this.loading.set(false);
      });

    this.search$
      .pipe(debounceTime(350), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe((q) => this.update({ q: q.trim() || undefined }));
  }

  protected onSearch(value: string): void {
    this.search$.next(value);
  }

  /** Applies a filter change; any change except paging goes back to page 1. */
  protected update(change: Partial<ServerFilter>): void {
    const next: ServerFilter = { ...this.filter(), ...change };
    if (!('page' in change)) {
      delete next.page;
    }
    void this.router.navigate([], { queryParams: filterToParams(next), replaceUrl: true });
  }

  protected boolValue(value: string): boolean | undefined {
    return value === 'true' ? true : value === 'false' ? false : undefined;
  }

  protected authValue(): string {
    const f = this.filter();
    if (f.whitelisted) {
      return 'whitelist';
    }
    return f.onlineMode === true ? 'online' : f.onlineMode === false ? 'offline' : '';
  }

  protected setAuth(value: string): void {
    this.update({
      onlineMode: value === 'online' ? true : value === 'offline' ? false : undefined,
      whitelisted: value === 'whitelist' ? true : undefined,
    });
  }

  protected onSort(sort: Sort): void {
    this.update({
      sort: sort.direction ? sort.active : undefined,
      dir: sort.direction || undefined,
    });
  }

  protected onPage(event: PageEvent): void {
    this.update({ page: event.pageIndex + 1, pageSize: event.pageSize });
  }

  protected clear(): void {
    void this.router.navigate([], { queryParams: {}, replaceUrl: true });
  }
}
