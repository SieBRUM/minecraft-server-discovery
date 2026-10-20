import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  Facets,
  MapPoint,
  Paged,
  Player,
  PlayerDetail,
  ServerDetail,
  ServerFilter,
  ServerSummary,
  Snapshot,
  Stats,
} from './models';

/** Thin typed wrapper around the read-only API. Everything is relative, so it works behind any host name. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  servers(filter: ServerFilter): Observable<Paged<ServerSummary>> {
    return this.http.get<Paged<ServerSummary>>('/api/servers', { params: toParams(filter) });
  }

  server(id: number): Observable<ServerDetail> {
    return this.http.get<ServerDetail>(`/api/servers/${encodeURIComponent(id)}`);
  }

  history(id: number, days: number): Observable<Snapshot[]> {
    return this.http.get<Snapshot[]>(`/api/servers/${encodeURIComponent(id)}/history`, {
      params: toParams({ days }),
    });
  }

  faviconUrl(id: number): string {
    return `/api/servers/${encodeURIComponent(id)}/favicon`;
  }

  map(online?: boolean, edition?: string): Observable<MapPoint[]> {
    return this.http.get<MapPoint[]>('/api/map', { params: toParams({ online, edition }) });
  }

  players(q: string, page: number, pageSize: number): Observable<Paged<Player>> {
    return this.http.get<Paged<Player>>('/api/players', {
      params: toParams({ q, page, pageSize }),
    });
  }

  player(id: number): Observable<PlayerDetail> {
    return this.http.get<PlayerDetail>(`/api/players/${encodeURIComponent(id)}`);
  }

  stats(): Observable<Stats> {
    return this.http.get<Stats>('/api/stats');
  }

  facets(): Observable<Facets> {
    return this.http.get<Facets>('/api/facets');
  }
}

/** Drops empty values so the API only sees filters that are actually set. */
export function toParams(values: object): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(values)) {
    if (value === undefined || value === null || value === '' || Number.isNaN(value)) {
      continue;
    }
    params = params.set(key, String(value));
  }
  return params;
}
