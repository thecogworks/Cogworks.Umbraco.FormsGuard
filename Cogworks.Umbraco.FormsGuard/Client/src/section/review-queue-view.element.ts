import { css, customElement, html, nothing, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import type { UUIPaginationEvent } from "@umbraco-cms/backoffice/external/uui";
import { umbConfirmModal } from "@umbraco-cms/backoffice/modal";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import { ReviewService, type ReviewQueueItem } from "../api/index.js";
import { PAGE_SIZE, formatDate, renderProbabilities, sharedStyles, statusOf, type ViewState } from "./shared.js";

type ReviewAction = "approve" | "confirmSpam" | "restore";

@customElement("cogworks-forms-guard-review-queue-view")
export class FormsGuardReviewQueueViewElement extends UmbLitElement {
  @state() private _state: ViewState = "loading";
  @state() private _items: Array<ReviewQueueItem> = [];
  @state() private _total = 0;
  @state() private _page = 1;
  @state() private _busy = false;
  @state() private _canEdit = false;

  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => {
      this.#notificationContext = context;
    });
  }

  override connectedCallback() {
    super.connectedCallback();
    this.#load();
  }

  async #load(): Promise<void> {
    this._state = "loading";

    // The client inherits Umbraco's config, which rejects on error statuses rather than returning them.
    let data;
    try {
      ({ data } = await ReviewService.getReviewQueue({
        query: { skip: (this._page - 1) * PAGE_SIZE, take: PAGE_SIZE },
      }));
    } catch (error) {
      this._state = statusOf(error) === 403 ? "forbidden" : "error";
      return;
    }

    if (!data) {
      this._state = "error";
      return;
    }

    // Step back a page when the last row on this one was just handled.
    if (data.items.length === 0 && data.total > 0 && this._page > 1) {
      this._page = Math.max(1, Math.ceil(data.total / PAGE_SIZE));
      return this.#load();
    }

    this._items = data.items;
    this._total = data.total;
    this._canEdit = data.canEditEntries;
    this._state = "ready";
  }

  async #run(item: ReviewQueueItem, action: ReviewAction) {
    if (this._busy) return;

    if (action === "confirmSpam") {
      try {
        await umbConfirmModal(this, {
          headline: this.localize.term("formsGuard_reviewConfirmSpamHeadline"),
          content: this.localize.term("formsGuard_reviewConfirmSpamContent"),
          color: "danger",
          confirmLabel: this.localize.term("formsGuard_reviewConfirmSpam"),
        });
      } catch {
        return;
      }
    }

    this._busy = true;
    const options = { path: { recordId: item.decision.recordId } };
    try {
      if (action === "approve") await ReviewService.approve(options);
      else if (action === "confirmSpam") await ReviewService.confirmSpam(options);
      else await ReviewService.restore(options);
      this.#notify("positive", `formsGuard_reviewDone_${action}`);
    } catch (error) {
      switch (statusOf(error)) {
        case 409:
          this.#notify("warning", "formsGuard_reviewRefused");
          break;
        case 404:
          this.#notify("danger", "formsGuard_reviewNotFound");
          break;
        case 403:
          // Usually the Forms edit-entries right; if Review itself was removed, the reload below shows the forbidden state.
          this.#notify("danger", "formsGuard_reviewNoEditRight");
          break;
        default:
          this.#notify("danger", "formsGuard_reviewFailed");
      }
    }

    this._busy = false;
    await this.#load();
  }

  #notify(color: "positive" | "warning" | "danger", key: string) {
    this.#notificationContext?.peek(color, { data: { message: this.localize.term(key) } });
  }

  #onPageChange(event: UUIPaginationEvent) {
    this._page = event.target.current;
    this.#load();
  }

  #renderStatus(status: string) {
    const color = status === "Quarantined" ? "danger" : "warning";
    // Only the queue statuses have terms here; anything else shows its raw value.
    const label = status === "Quarantined" || status === "Review" ? this.localize.term(`formsGuard_status_${status}`) : status;
    return html`<uui-tag look="primary" color=${color}>${label}</uui-tag>`;
  }

  #renderFields(item: ReviewQueueItem) {
    if (item.recordMissing) {
      return html`<span class="muted">${this.localize.term("formsGuard_reviewRecordMissing")}</span>`;
    }
    if (item.fields.length === 0) {
      return html`<span class="muted">${this.localize.term("formsGuard_reviewNoFields")}</span>`;
    }
    return html`<dl>
      ${item.fields.map(
        (field) => html`<div class="field">
          <dt>${field.caption ?? ""}</dt>
          <dd>${field.value ?? ""}</dd>
        </div>`,
      )}
    </dl>`;
  }

  #renderActions(item: ReviewQueueItem) {
    // Every action returns 404 when the Forms record is gone, so offer none.
    if (item.recordMissing) return nothing;
    // Without the Forms edit-entries right every action returns 403.
    if (!this._canEdit) return nothing;
    const status = item.decision.status;
    if (status === "Review") {
      return html`<div class="actions">
        <uui-button
          look="primary"
          color="positive"
          label=${this.localize.term("formsGuard_reviewApprove")}
          ?disabled=${this._busy}
          @click=${() => this.#run(item, "approve")}></uui-button>
        <uui-button
          class="spam"
          look="primary"
          label=${this.localize.term("formsGuard_reviewConfirmSpam")}
          ?disabled=${this._busy}
          @click=${() => this.#run(item, "confirmSpam")}></uui-button>
      </div>`;
    }
    if (status === "Quarantined") {
      return html`<div class="actions">
        <uui-button
          look="secondary"
          label=${this.localize.term("formsGuard_reviewRestore")}
          ?disabled=${this._busy}
          @click=${() => this.#run(item, "restore")}></uui-button>
      </div>`;
    }
    return nothing;
  }

  #renderTable() {
    if (this._items.length === 0) {
      return html`<p class="muted">${this.localize.term("formsGuard_reviewEmpty")}</p>`;
    }

    const pages = Math.max(1, Math.ceil(this._total / PAGE_SIZE));

    return html`
      <uui-table aria-label=${this.localize.term("formsGuard_reviewTableLabel")}>
        <uui-table-head>
          <uui-table-head-cell>${this.localize.term("formsGuard_columnCreated")}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term("formsGuard_columnStatus")}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term("formsGuard_columnForm")}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term("formsGuard_columnProbabilities")}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term("formsGuard_columnEntry")}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term("formsGuard_columnActions")}</uui-table-head-cell>
        </uui-table-head>
        ${this._items.map(
          (item) => html`
            <uui-table-row>
              <uui-table-cell>${formatDate(item.decision.createdUtc)}</uui-table-cell>
              <uui-table-cell>${this.#renderStatus(item.decision.status)}</uui-table-cell>
              <uui-table-cell>
                ${item.formName ?? html`<span class="muted">${this.localize.term("formsGuard_reviewFormMissing")}</span>`}
              </uui-table-cell>
              <uui-table-cell>${renderProbabilities(item.decision.probabilities, this.localize.term("formsGuard_probabilitiesNone"))}</uui-table-cell>
              <uui-table-cell class="entry">${this.#renderFields(item)}</uui-table-cell>
              <uui-table-cell>${this.#renderActions(item)}</uui-table-cell>
            </uui-table-row>
          `,
        )}
      </uui-table>
      ${pages > 1
        ? html`<uui-pagination
            .current=${this._page}
            .total=${pages}
            @change=${this.#onPageChange}></uui-pagination>`
        : nothing}
    `;
  }

  #renderBody() {
    switch (this._state) {
      case "loading":
        return html`<uui-loader></uui-loader>`;
      case "forbidden":
        return html`<p>${this.localize.term("formsGuard_decisionsForbidden")}</p>`;
      case "error":
        return html`<p>${this.localize.term("formsGuard_reviewError")}</p>`;
      default:
        return this.#renderTable();
    }
  }

  override render() {
    return html`<uui-box headline=${this.localize.term("formsGuard_reviewHeadline")}>${this.#renderBody()}</uui-box>`;
  }

  static override styles = [
    sharedStyles,
    css`
      .entry {
        min-width: 16rem;
      }
      dl {
        margin: 0;
      }
      .field {
        margin-bottom: var(--uui-size-space-2);
      }
      dt {
        font-weight: bold;
      }
      dd {
        margin: 0;
        white-space: pre-wrap;
        overflow-wrap: anywhere;
      }
      /* Buttons side by side at equal widths. */
      .actions {
        display: inline-grid;
        grid-auto-flow: column;
        grid-auto-columns: 1fr;
        gap: var(--uui-size-space-2);
      }
      /* Burnt orange: a deliberate action, not an error or a cancel. White text passes AA (about 5:1). */
      uui-button.spam {
        --uui-button-background-color: #c2410c;
        --uui-button-background-color-hover: #9a3412;
        --uui-button-contrast: #fff;
        --uui-button-contrast-hover: #fff;
      }
    `,
  ];
}

export default FormsGuardReviewQueueViewElement;

declare global {
  interface HTMLElementTagNameMap {
    "cogworks-forms-guard-review-queue-view": FormsGuardReviewQueueViewElement;
  }
}
