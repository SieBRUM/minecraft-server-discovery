import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { ApiService } from '../core/api.service';

/** Server icon (served by the API as image/png) with a neutral placeholder. */
@Component({
  selector: 'app-favicon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (src() && !failed()) {
      <img
        [src]="src()"
        [width]="size()"
        [height]="size()"
        alt=""
        loading="lazy"
        (error)="failed.set(true)"
      />
    } @else {
      <span
        class="placeholder"
        [style.width.px]="size()"
        [style.height.px]="size()"
        aria-hidden="true"
      ></span>
    }`,
  styles: `
    :host {
      display: inline-flex;
      flex: none;
    }
    img {
      image-rendering: pixelated;
      border-radius: 4px;
    }
    .placeholder {
      display: inline-block;
      border-radius: 4px;
      background: var(--mat-sys-surface-container-high, #ddd);
    }
  `,
})
export class Favicon {
  private readonly api = inject(ApiService);
  readonly serverId = input.required<number>();
  readonly hasFavicon = input(true);
  readonly size = input(32);
  protected readonly failed = signal(false);
  protected readonly src = computed(() =>
    this.hasFavicon() ? this.api.faviconUrl(this.serverId()) : null,
  );
}
