import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MotdSegment } from '../core/models';

/**
 * Renders a Minecraft MOTD. Each segment is bound with Angular text interpolation, so server-provided
 * content is always shown as text and never parsed as HTML. Colours are validated #rrggbb values from the
 * API and additionally pass Angular's style sanitizer.
 */
@Component({
  selector: 'app-motd',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="motd" [class.dark]="dark()">
    @for (s of segments(); track $index) {
      <span
        [style.color]="s.color"
        [class.b]="s.bold"
        [class.i]="s.italic"
        [class.u]="s.underlined"
        [class.s]="s.strikethrough"
        [class.o]="s.obfuscated"
        >{{ s.text }}</span
      >
    } @empty {
      <span class="none">{{ fallback() }}</span>
    }
  </span>`,
  styles: `
    .motd {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }
    .motd.dark {
      display: block;
      background: #1e1e1e;
      color: #aaaaaa;
      padding: 12px 16px;
      border-radius: 8px;
      font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace;
      line-height: 1.5;
    }
    .b {
      font-weight: 700;
    }
    .i {
      font-style: italic;
    }
    .u {
      text-decoration: underline;
    }
    .s {
      text-decoration: line-through;
    }
    .u.s {
      text-decoration: underline line-through;
    }
    .o {
      filter: blur(3px);
    }
    .none {
      opacity: 0.6;
      font-style: italic;
    }
  `,
})
export class Motd {
  readonly segments = input<MotdSegment[]>([]);
  readonly fallback = input('');
  readonly dark = input(false);
}
