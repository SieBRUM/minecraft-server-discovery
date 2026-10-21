import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  afterRenderEffect,
  inject,
  input,
  viewChild,
} from '@angular/core';
import { ThemeService, cssVar } from '../core/theme.service';
import { BarController, BarElement, CategoryScale, Chart, LinearScale, Tooltip } from 'chart.js';

Chart.register(BarController, BarElement, CategoryScale, LinearScale, Tooltip);

/** Horizontal bar chart for "top N" counts. */
@Component({
  selector: 'app-bar-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (!labels().length) {
      <p class="empty">No data yet</p>
    }
    <div class="wrap" [hidden]="!labels().length" [style.height.px]="height()">
      <canvas #canvas [attr.aria-label]="label()" role="img"></canvas>
    </div>`,
  styles: `
    .wrap {
      position: relative;
      width: 100%;
    }
    .empty {
      opacity: 0.6;
      margin: 8px 0;
    }
  `,
})
export class BarChart implements OnDestroy {
  readonly labels = input<string[]>([]);
  readonly values = input<number[]>([]);
  readonly label = input('');
  readonly color = input('#3f8f3f');
  readonly height = input(320);

  private readonly theme = inject(ThemeService);
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private chart?: Chart;

  constructor() {
    afterRenderEffect(() => {
      const labels = this.labels();
      const values = this.values();
      this.theme.dark(); // redraw when the theme changes
      const text = cssVar('--app-muted', '#666');
      const grid = cssVar('--app-grid', 'rgba(0,0,0,0.08)');
      this.chart?.destroy();
      this.chart = new Chart(this.canvas().nativeElement, {
        type: 'bar',
        data: {
          labels,
          datasets: [
            { label: this.label(), data: values, backgroundColor: this.color(), borderRadius: 3 },
          ],
        },
        options: {
          indexAxis: 'y',
          maintainAspectRatio: false,
          animation: false,
          plugins: { legend: { display: false } },
          scales: {
            x: {
              beginAtZero: true,
              ticks: { precision: 0, color: text },
              grid: { color: grid },
              border: { display: false },
            },
            y: { ticks: { color: text }, grid: { display: false }, border: { display: false } },
          },
        },
      });
    });
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
  }
}
