import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  afterRenderEffect,
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
      const format = new Intl.DateTimeFormat(undefined, { dateStyle: 'short', timeStyle: 'short' });
      this.chart?.destroy();
      this.chart = new Chart(this.canvas().nativeElement, {
        type: 'line',
        data: {
          datasets: [
            {
              label: 'Players',
              data: players,
              borderColor: '#2e7d32',
              backgroundColor: 'rgba(46,125,50,0.15)',
              fill: true,
              spanGaps: false,
              pointRadius: 0,
              yAxisID: 'y',
            },
            {
              label: 'Latency (ms)',
              data: latency,
              borderColor: '#1565c0',
              pointRadius: 0,
              borderWidth: 1,
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
              ticks: { callback: (v) => format.format(new Date(Number(v))), maxTicksLimit: 6 },
            },
            y: {
              beginAtZero: true,
              ticks: { precision: 0 },
              title: { display: true, text: 'Players' },
            },
            y1: {
              beginAtZero: true,
              position: 'right',
              grid: { drawOnChartArea: false },
              title: { display: true, text: 'ms' },
            },
          },
          plugins: {
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
