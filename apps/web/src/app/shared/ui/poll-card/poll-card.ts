import { Component, computed, input, output } from '@angular/core';
import { PollSummary } from '../../models/chat.models';
import { pollIsClosed, pollIsTie, pollLeaderIndexes } from '../../polls/poll-summary';

@Component({
  selector: 'vc-poll-card',
  standalone: true,
  template: `
    @if (poll(); as card) {
      <section class="poll" role="group" [attr.aria-label]="card.question">
        <header class="poll__head">
          <strong>{{ card.question }}</strong>
          <span class="poll__meta">
            @if (closed()) {
              Encerrada
            } @else if (card.allowMultiple) {
              Vários votos
            } @else {
              Um voto
            }
            @if (card.anonymous) {
              · Anônima
            }
          </span>
        </header>
        <ul class="poll__options">
          @for (option of card.options; track option.id; let i = $index) {
            <li>
              <button
                type="button"
                class="poll__option"
                [class.is-mine]="option.votedByMe"
                [class.is-leader]="closed() && isLeader(i)"
                [disabled]="!canInteract()"
                [attr.aria-pressed]="option.votedByMe"
                (click)="toggle.emit(option.id)"
              >
                <span class="poll__bar" [style.width.%]="option.percent"></span>
                <span class="poll__label">{{ option.text }}</span>
                <span class="poll__count">{{ option.percent }}% · {{ option.voteCount }}</span>
              </button>
            </li>
          }
        </ul>
        <footer class="poll__foot">
          <span>{{ card.totalVotes }} voto{{ card.totalVotes === 1 ? '' : 's' }}</span>
          @if (closed() && tie()) {
            <span>Empate</span>
          }
          @if (!closed() && canClose()) {
            <button type="button" class="poll__close" (click)="close.emit()">Encerrar</button>
          }
        </footer>
      </section>
    }
  `,
  styles: `
    .poll {
      display: grid;
      gap: 0.5rem;
      min-width: 16rem;
    }
    .poll__head {
      display: grid;
      gap: 0.15rem;
    }
    .poll__meta,
    .poll__foot,
    .poll__count {
      color: var(--vc-ink-muted);
      font-size: 0.75rem;
    }
    .poll__options {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.35rem;
    }
    .poll__option {
      position: relative;
      display: flex;
      justify-content: space-between;
      gap: 0.5rem;
      width: 100%;
      text-align: left;
      border: 1px solid var(--vc-line);
      border-radius: 0.5rem;
      background: transparent;
      color: inherit;
      padding: 0.4rem 0.6rem;
      overflow: hidden;
    }
    .poll__option:disabled {
      cursor: default;
    }
    .poll__option.is-mine {
      border-color: var(--vc-accent);
    }
    .poll__option.is-leader {
      font-weight: 700;
    }
    .poll__bar {
      position: absolute;
      inset: 0 auto 0 0;
      background: color-mix(in srgb, var(--vc-accent) 18%, transparent);
    }
    .poll__label,
    .poll__count {
      position: relative;
    }
    .poll__foot {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 0.5rem;
    }
    .poll__close {
      border: 0;
      background: none;
      color: var(--vc-accent);
      cursor: pointer;
      font: inherit;
    }
  `,
})
export class PollCard {
  readonly poll = input.required<PollSummary>();
  readonly canClose = input(false);
  readonly toggle = output<string>();
  readonly close = output<void>();

  readonly closed = computed(() => pollIsClosed(this.poll()));
  readonly tie = computed(() => this.closed() && pollIsTie(this.poll().options));
  readonly canInteract = computed(() => this.poll().canVote && !this.closed());
  private readonly leaders = computed(() => pollLeaderIndexes(this.poll().options));

  isLeader(index: number): boolean {
    return this.leaders().includes(index);
  }
}
