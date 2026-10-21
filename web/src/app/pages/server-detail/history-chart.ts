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
import {
  Chart,
  Filler,
  LineController,
  LineElement,
  LinearScale,
  PointElement,
  TimeSeriesScale,
  Tooltip,
  Legend,
} from 'chart.js';
import { Snapshot } from '../../core/models';
import { ThemeService, cssVar } from '../../core/theme.service';

Chart.register(
  LineController,
  LineElement,
  PointElement,
  LinearScale,
  TimeSeriesScale,
  Filler,
  Tooltip,
  Legend,
);

/** Players online over time; offline probes show as gaps at zero. No date adapter needed: x is epoch ms. */
@Component({
  selector: 'app-history-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div class="wrap">
    <canvas #canvas role="img" aria-label="Player count history"></canvas>
  </div>`,
  styles: `
    .wrap {
      position: relative;
      height: 280px;
    }
  `,
})
export class HistoryChart implements OnDestroy {
  readonly points = input<Snapshot[]>([]);
  private readonly theme = inject(ThemeService);
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private chart?: Chart;

  constructor() {
    afterRenderEffect(() => {
      const points = this.points();
      const players = points.map((p) => ({
        x: new Date(p.t).getTime(),
        y: p.online ? (p.players ?? 0) : null,
      }));
      const latency = points.map((p) => ({
        x: new Date(p.t).getTime(),
        y: p.online ? p.latencyMs : null,
      }));
      this.theme.dark(); // redraw when the theme changes
      const text = cssVar('--app-muted', '#666');
      const grid = cssVar('--app-grid', 'rgba(0,0,0,0.08)');
      const accent = cssVar('--app-accent', '#2e7d32');
      const xs = players.map((p) => p.x);
      const format = new Intl.DateTimeFormat(undefined, { dateStyle: 'short', timeStyle: 'short' });
      this.chart?.destroy();
      this.chart = new Chart(this.canvas().nativeElement, {
        type: 'line',
        data: {
          datasets: [
            {
              label: 'Players',
              data: players,
              borderColor: accent,
              backgroundColor: 'rgba(76, 175, 80, 0.16)',
              borderWidth: 2,
              tension: 0.3,
              fill: true,
              spanGaps: false,
              pointRadius: 0,
              yAxisID: 'y',
            },
            {
              label: 'Latency (ms)',
              data: latency,
              borderColor: 'rgba(66, 165, 245, 0.7)',
              pointRadius: 0,
              borderWidth: 1,
              tension: 0.3,
              yAxisID: 'y1',
            },
          ],
        },
        options: {
          maintainAspectRatio: false,
          animation: false,
          parsing: false,
          interaction: { mode: 'nearest', axis: 'x', intersect: false },
          scales: {
            x: {
              type: 'linear',
              // Exactly the data range (Chart.js would otherwise pad it with empty days).
              min: xs.length ? Math.min(...xs) : undefined,
              max: xs.length ? Math.max(...xs) : undefined,
              ticks: {
                callback: (v) => format.format(new Date(Number(v))),
                maxTicksLimit: 6,
                color: text,
              },
              grid: { color: grid },
              border: { display: false },
            },
            y: {
              beginAtZero: true,
              ticks: { precision: 0, color: text },
              grid: { color: grid },
              border: { display: false },
              title: { display: true, text: 'Players', color: text },
            },
            y1: {
              beginAtZero: true,
              position: 'right',
              ticks: { color: text },
              grid: { drawOnChartArea: false },
              border: { display: false },
              title: { display: true, text: 'ms', color: text },
            },
          },
          plugins: {
            legend: {
              labels: {
                color: text,
                boxWidth: 12,
                boxHeight: 12,
                useBorderRadius: true,
                borderRadius: 3,
              },
            },
            tooltip: {
              callbacks: {
                title: (items) => (items[0] ? format.format(new Date(items[0].parsed.x ?? 0)) : ''),
              },
            },
          },
        },
      });
    });
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
  }
}
