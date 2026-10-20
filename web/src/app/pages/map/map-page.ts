import { DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  NgZone,
  afterNextRender,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { Router } from '@angular/router';
import * as L from 'leaflet';
import { ApiService } from '../../core/api.service';
import { MapPoint } from '../../core/models';

@Component({
  selector: 'app-map-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, MatButtonToggleModule],
  template: `
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
        <mat-button-toggle value="">Both editions</mat-button-toggle>
        <mat-button-toggle value="java">Java</mat-button-toggle>
        <mat-button-toggle value="bedrock">Bedrock</mat-button-toggle>
      </mat-button-toggle-group>
      <span class="info"
        >{{ total() | number }} located servers · locations are approximate (city level at
        best)</span
      >
    </div>
    <div #map class="map" role="application" aria-label="Map of servers"></div>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      height: calc(100vh - 140px);
      min-height: 400px;
    }
    .bar {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: center;
      margin-bottom: 12px;
    }
    .info {
      opacity: 0.7;
      font-size: 13px;
    }
    .map {
      flex: 1;
      border-radius: 8px;
    }
  `,
})
export class MapPage {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly zone = inject(NgZone);
  private readonly mapElement = viewChild.required<ElementRef<HTMLDivElement>>('map');

  protected readonly online = signal<'all' | 'online'>('all');
  protected readonly edition = signal('');
  protected readonly total = signal(0);

  private map?: L.Map;
  private layer?: L.LayerGroup;

  constructor() {
    afterNextRender(() => {
      this.map = L.map(this.mapElement().nativeElement, {
        preferCanvas: true,
        worldCopyJump: true,
      }).setView([30, 10], 2);
      L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 12,
        attribution:
          '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
      }).addTo(this.map);
      this.layer = L.layerGroup().addTo(this.map);
      this.load();
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

  private load(): void {
    this.api
      .map(this.online() === 'online' ? true : undefined, this.edition() || undefined)
      .subscribe({
        next: (points) => this.draw(points),
        error: () => this.draw([]),
      });
  }

  private draw(points: MapPoint[]): void {
    const layer = this.layer;
    if (!layer) {
      return;
    }
    layer.clearLayers();
    this.total.set(points.reduce((sum, p) => sum + p.count, 0));
    for (const p of points) {
      const marker = L.circleMarker([p.lat, p.lon], {
        radius: Math.min(4 + Math.log2(p.count) * 2, 22),
        color: p.online > 0 ? '#2e7d32' : '#757575',
        weight: 1,
        fillOpacity: 0.55,
      });
      // Built with DOM APIs and textContent, never HTML strings.
      marker.bindPopup(() => this.popup(p));
      marker.addTo(layer);
    }
  }

  private popup(p: MapPoint): HTMLElement {
    const root = document.createElement('div');
    const line = document.createElement('div');
    line.textContent = `${p.count} server(s), ${p.online} online, ${p.players} player(s)`;
    const link = document.createElement('a');
    link.href = '#';
    link.textContent = 'Show in list';
    link.addEventListener('click', (event) => {
      event.preventDefault();
      this.zone.run(
        () =>
          void this.router.navigate(['/servers'], {
            queryParams: { cellLat: p.lat, cellLon: p.lon },
          }),
      );
    });
    root.append(line, link);
    return root;
  }
}
