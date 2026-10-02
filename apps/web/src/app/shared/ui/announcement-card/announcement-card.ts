import { DatePipe } from '@angular/common';
import { Component, input, output, signal } from '@angular/core';
import { fillTemplate, ui } from '../../../core/i18n/strings';
import { AnnouncementSummary } from '../../models/chat.models';

@Component({
  selector: 'vc-announcement-card',
  standalone: true,
  imports: [DatePipe],
  template: `
    @if (announcement(); as card) {
      <section class="announcement" role="group" [attr.aria-label]="ui.navAnnouncement">
        <p class="announcement__by">{{ fillTemplate(ui.announcementBy, { name: authorName() }) }}</p>
        <p class="announcement__meta">
          <time [attr.datetime]="createdAt()">{{ createdAt() | date: 'short' }}</time>
          @if (card.acknowledgeBy) {
            <span>{{ fillTemplate(ui.announcementDeadline, { date: (card.acknowledgeBy | date: 'short') ?? '' }) }}</span>
          }
          @if (card.closedAt) {
            <span>{{ ui.announcementClosed }}</span>
          }
        </p>
        @if (card.requiresAcknowledgement) {
          <p class="announcement__count">{{ fillTemplate(ui.announcementCount, { n: card.acknowledgementCount }) }}</p>
          <p class="announcement__legal">{{ ui.announcementNotLegal }}</p>
          @if (card.acknowledgedByMe) {
            <p class="announcement__state" role="status">{{ ui.announcementConfirmed }}</p>
          } @else if (card.canAcknowledge) {
            <button
              type="button"
              class="announcement__confirm"
              [disabled]="busy()"
              (click)="acknowledge.emit()"
            >
              {{ ui.announcementConfirm }}
            </button>
          } @else if (card.closedAt) {
            <p class="announcement__state" role="status">{{ ui.announcementClosed }}</p>
          }
          @if (card.canViewReport) {
            <button type="button" class="announcement__report" (click)="toggleReport()">
              {{ ui.announcementReport }}
            </button>
            @if (reportOpen()) {
              <ul class="announcement__people">
                @for (item of report(); track item.userId) {
                  <li>{{ item.displayName }}</li>
                }
              </ul>
            }
          }
          @if (card.canViewReport && !card.closedAt) {
            <button type="button" class="announcement__close" (click)="close.emit()">{{ ui.announcementClose }}</button>
          }
        }
        @if (error()) {
          <p class="announcement__error" role="alert">{{ error() }}</p>
        }
      </section>
    }
  `,
  styles: `
    :host {
      display: block;
      width: min(22rem, 100%);
    }
    .announcement {
      display: grid;
      gap: 0.35rem;
      min-width: 0;
    }
    .announcement__by {
      margin: 0;
      font-weight: 650;
    }
    .announcement__meta,
    .announcement__count,
    .announcement__legal,
    .announcement__state {
      margin: 0;
      color: var(--vc-ink-muted);
      font-size: 0.75rem;
    }
    .announcement__meta {
      display: flex;
      flex-wrap: wrap;
      gap: 0.35rem 0.75rem;
    }
    .announcement__confirm,
    .announcement__report,
    .announcement__close {
      justify-self: start;
      min-height: 2.75rem;
      padding: 0.4rem 0.75rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
      background: transparent;
      color: inherit;
      font: inherit;
      cursor: pointer;
    }
    .announcement__confirm {
      border-color: var(--vc-brand);
    }
    .announcement__confirm:focus-visible,
    .announcement__report:focus-visible,
    .announcement__close:focus-visible {
      outline: 2px solid var(--vc-brand);
      outline-offset: 2px;
    }
    .announcement__confirm:disabled {
      cursor: default;
      opacity: 0.6;
    }
    .announcement__people {
      margin: 0;
      padding-inline-start: 1.1rem;
      font-size: 0.8rem;
    }
    .announcement__error {
      margin: 0;
      color: var(--vc-danger, var(--vc-ink));
      font-size: 0.75rem;
    }
  `,
})
export class AnnouncementCard {
  readonly ui = ui;
  readonly fillTemplate = fillTemplate;
  readonly announcement = input.required<AnnouncementSummary>();
  readonly authorName = input('');
  readonly createdAt = input('');
  readonly report = input<Array<{ userId: string; displayName: string }>>([]);
  readonly error = input<string | null>(null);
  readonly busy = input(false);
  readonly acknowledge = output<void>();
  readonly close = output<void>();
  readonly loadReport = output<void>();
  readonly reportOpen = signal(false);

  toggleReport(): void {
    const next = !this.reportOpen();
    this.reportOpen.set(next);
    if (next) {
      this.loadReport.emit();
    }
  }
}
