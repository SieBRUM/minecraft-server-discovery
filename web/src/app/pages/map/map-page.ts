import { DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  NgZone,
  afterNextRender,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { Router } from '@angular/router';
import * as L from 'leaflet';
import { feature } from 'topojson-client';
import type { GeometryCollection, Topology } from 'topojson-specification';
import { ApiService } from '../../core/api.service';
import { MapPoint } from '../../core/models';
import { ThemeService, cssVar } from '../../core/theme.service';

/**
 * World map of servers. The base map is a bundled Natural Earth outline (world-atlas), so it works offline,
 * follows the theme and makes no third-party requests. OpenStreetMap tiles can be switched on for detail.
 */
@Component({
  selector: 'app-map-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, MatButtonToggleModule, MatSlideToggleModule],
  template: `
    <div class="page-head">
      <div>
        <h1>Map</h1>
        <p>
          {{ total() | number }} located servers · locations are approximate (city level at best)
        </p>
      </div>
      <div class="bar">
        <mat-button-toggle-group
          [value]="online()"
          (change)="setOnline($event.value)"
          aria-label="Status"
        >
          <mat-button-toggle value="all">All</mat-button-toggle>
          <mat-button-toggle value="online">Online</mat-button-toggle>
        </mat-button-toggle-group>
        <mat-button-toggle-group
          [value]="edition()"
          (change)="setEdition($event.value)"
          aria-label="Edition"
        >
          <mat-button-toggle value="">Both</mat-button-toggle>
          <mat-button-toggle value="java">Java</mat-button-toggle>
          <mat-button-toggle value="bedrock">Bedrock</mat-button-toggle>
        </mat-button-toggle-group>
        <mat-slide-toggle [checked]="streets()" (change)="setStreets($event.checked)"
          >Street map (OpenStreetMap)</mat-slide-toggle
        >
      </div>
    </div>
    <div #map class="map panel" role="application" aria-label="Map of servers"></div>
    <div class="legend">
      <span><i class="on"></i> has online servers</span>
      <span><i class="off"></i> offline only</span>
      <span>circle size = number of servers</span>
    </div>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      height: calc(100vh - 150px);
      min-height: 460px;
    }
    .bar {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: center;
    }
    .map {
      flex: 1;
      overflow: hidden;
    }
    .legend {
      display: flex;
      gap: 18px;
      flex-wrap: wrap;
      font-size: 12px;
      color: var(--app-muted);
      margin-top: 10px;
    }
    .legend i {
      display: inline-block;
      width: 10px;
      height: 10px;
      border-radius: 50%;
      vertical-align: -1px;
      margin-right: 4px;
    }
    .legend .on {
      background: #43a047;
    }
    .legend .off {
      background: #9e9e9e;
    }
  `,
})
export class MapPage {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly zone = inject(NgZone);
  private readonly theme = inject(ThemeService);
  private readonly mapElement = viewChild.required<ElementRef<HTMLDivElement>>('map');

  protected readonly online = signal<'all' | 'online'>('all');
  protected readonly edition = signal('');
  protected readonly streets = signal(false);
  protected readonly total = signal(0);

  private map?: L.Map;
  private world?: L.GeoJSON;
  private tiles?: L.TileLayer;
  private markers?: L.LayerGroup;
  private points: MapPoint[] = [];

  constructor() {
    afterNextRender(() => {
      this.map = L.map(this.mapElement().nativeElement, {
        preferCanvas: true,
        worldCopyJump: true,
        minZoom: 2,
        maxZoom: 12,
        zoomSnap: 0.5,
      }).setView([25, 10], 2.5);
      this.map.attributionControl.setPrefix(false);
      void this.addWorld();
      this.markers = L.layerGroup().addTo(this.map);
      this.load();
    });

    // Recolour the base map and markers when the theme changes.
    effect(() => {
      this.theme.dark();
      this.styleWorld();
      this.draw();
    });

    inject(DestroyRef).onDestroy(() => this.map?.remove());
  }

  protected setOnline(value: 'all' | 'online'): void {
    this.online.set(value);
    this.load();
  }

  protected setEdition(value: string): void {
    this.edition.set(value);
    this.load();
  }

  protected setStreets(on: boolean): void {
    this.streets.set(on);
    if (!this.map) {
      return;
    }
    if (on) {
      this.tiles ??= L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 12,
        className: 'osm-tiles',
        attribution:
          '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
      });
      this.tiles.addTo(this.map);
      this.tiles.bringToBack();
    } else {
      this.tiles?.remove();
    }
  }

  private async addWorld(): Promise<void> {
    // Lazy chunk: ~100 KB of Natural Earth 1:110m country outlines.
    const topology = (await import('world-atlas/countries-110m.json'))
      .default as unknown as Topology<{
      countries: GeometryCollection;
    }>;
    if (!this.map) {
      return;
    }
    const countries = feature(topology, topology.objects.countries);
    // Antarctica (no servers, and its outline wraps the pole) is left out; other rings that cross the
    // 180° meridian are unwrapped so Leaflet doesn't draw stripes across the whole map.
    countries.features = countries.features.filter((f) => f.id !== '010');
    for (const f of countries.features) {
      unwrapAntimeridian(f.geometry);
    }
    this.world = L.geoJSON(countries, {
      interactive: false,
      attribution: 'Outlines: <a href="https://www.naturalearthdata.com">Natural Earth</a>',
    }).addTo(this.map);
    this.world.bringToBack();
    this.styleWorld();
  }

  private styleWorld(): void {
    this.world?.setStyle({
      color: cssVar('--map-border', '#c7d0c9'),
      weight: 0.8,
      fillColor: cssVar('--map-land', '#f7f8f5'),
      fillOpacity: 1,
    });
  }

  private load(): void {
    this.api
      .map(this.online() === 'online' ? true : undefined, this.edition() || undefined)
      .subscribe({
        next: (points) => {
          this.points = points;
          this.draw();
        },
        error: () => {
          this.points = [];
          this.draw();
        },
      });
  }

  private draw(): void {
    const layer = this.markers;
    if (!layer) {
      return;
    }
    layer.clearLayers();
    this.total.set(this.points.reduce((sum, p) => sum + p.count, 0));
    const dark = this.theme.dark();
    for (const p of this.points) {
      const on = p.online > 0;
      const marker = L.circleMarker([p.lat, p.lon], {
        radius: Math.min(5 + Math.log2(p.count) * 2.5, 24),
        color: on ? (dark ? '#81c784' : '#2e7d32') : '#9e9e9e',
        weight: 1.5,
        fillColor: on ? '#43a047' : '#9e9e9e',
        fillOpacity: dark ? 0.55 : 0.45,
      });
      // Built with DOM APIs and textContent, never HTML strings.
      marker.bindPopup(() => this.popup(p));
      marker.addTo(layer);
    }
  }

  private popup(p: MapPoint): HTMLElement {
    const root = document.createElement('div');
    root.className = 'map-popup';
    const title = document.createElement('strong');
    title.textContent = `${p.count} server${p.count === 1 ? '' : 's'}`;
    const line = document.createElement('span');
    line.textContent = `${p.online} online · ${p.players} player${p.players === 1 ? '' : 's'}`;
    const link = document.createElement('a');
    link.href = '#';
    link.textContent = 'Show in list →';
    link.addEventListener('click', (event) => {
      event.preventDefault();
      this.zone.run(
        () =>
          void this.router.navigate(['/servers'], {
            queryParams: { cellLat: p.lat, cellLon: p.lon },
          }),
      );
    });
    root.append(title, line, link);
    return root;
  }
}

/** Makes longitudes continuous along each ring (e.g. 179 → -179 becomes 179 → 181). */
function unwrapAntimeridian(geometry: GeoJSON.Geometry | null): void {
  const fixRing = (ring: GeoJSON.Position[]) => {
    let offset = 0;
    for (let i = 1; i < ring.length; i++) {
      const previous = ring[i - 1][0];
      const delta = ring[i][0] + offset - previous;
      if (delta > 180) {
        offset -= 360;
      } else if (delta < -180) {
        offset += 360;
      }
      ring[i] = [ring[i][0] + offset, ring[i][1]];
    }
  };
  if (geometry?.type === 'Polygon') {
    geometry.coordinates.forEach(fixRing);
  } else if (geometry?.type === 'MultiPolygon') {
    geometry.coordinates.forEach((polygon) => polygon.forEach(fixRing));
  }
}
