import { css, customElement, html, nothing, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import type { UUIPaginationEvent } from "@umbraco-cms/backoffice/external/uui";
import {
  DecisionsService,
  type AuditListItem,
  type DecisionListItem,
  type DecisionStatus,
  type LogFormOption,
} from "../api/index.js";
import {
  PAGE_SIZE,
  formatDate,
  renderProbabilities,
  sharedStyles,
  statusOf,
  valueOf,
  type ViewState,
} from "./shared.js";

type LogTab = "decisions" | "activity";

const STATUSES: ReadonlyArray<DecisionStatus> = ["Pending", "Approved", "ApprovedNotChecked", "Quarantined", "Review"];
const AUDIT_ACTIONS = [
  "decision",
  "approve",
  "confirm-spam",
  "restore",
  "settings-save",
  "rule-create",
  "rule-delete",
  "record-deleted",
  "quarantine-purge",
  "decision-removed",
] as const;

/** Start of a local day (yyyy-mm-dd), as a UTC instant; `addDays` moves it on. Undefined when blank or unreadable. */
function localDayToUtc(day: string, addDays = 0): string | undefined {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(day);
  if (!match) return undefined;
  const date = new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]) + addDays);
  return Number.isNaN(date.getTime()) ? undefined : date.toISOString();
}

@customElement("cogworks-forms-guard-decisions-view")
export class FormsGuardDecisionsViewElement extends UmbLitElement {
  @state() private _state: ViewState = "loading";
  @state() private _forms: Array<LogFormOption> = [];

  // Filters. Empty strings mean "any".
  @state() private _formId = "";
  @state() private _status: DecisionStatus | "" = "";
  @state() private _from = "";
  @state() private _to = "";

  @state() private _decisions: Array<DecisionListItem> = [];
  @state() private _decisionsTotal = 0;
  @state() private _notCheckedTotal = 0;
  @state() private _decisionsPage = 1;

  // Both tables load with the filters; the tab only picks which one shows.
  @state() private _tab: LogTab = "decisions";

  @state() private _audit: Array<AuditListItem> = [];
  @state() private _auditTotal = 0;
  @state() private _auditPage = 1;

  // Ignores responses that arrive after a newer request was sent.
  #decisionsRequest = 0;
  #auditRequest = 0;

  override connectedCallback() {
    super.connectedCallback();
    this.#loadForms();
    this.#loadAll();
  }

  async #loadForms() {
    try {
      const { data } = await DecisionsService.getLogForms();
      this._forms = data ?? [];
    } catch {
      // The decisions and audit calls report access and errors; without forms the filter shows only "All forms".
      this._forms = [];
    }
  }

  async #loadAll() {
    this._state = "loading";
    const [decisions, audit] = await Promise.all([this.#loadDecisions(), this.#loadAudit()]);
    if (decisions === "stale" || audit === "stale") return;
    if (decisions === "forbidden" || audit === "forbidden") this._state = "forbidden";
    else if (decisions === "error" || audit === "error") this._state = "error";
    else this._state = "ready";
  }

  #filterQuery() {
    return {
      formId: this._formId || undefined,
      fromUtc: localDayToUtc(this._from),
      // Inclusive "to" day: send the start of the next day, which the API treats as exclusive.
      toUtc: localDayToUtc(this._to, 1),
    };
  }

  async #loadDecisions(): Promise<"ok" | "forbidden" | "error" | "stale"> {
    const request = ++this.#decisionsRequest;
    // The client inherits Umbraco's config, which rejects on error statuses rather than returning them.
    let data;
    try {
      ({ data } = await DecisionsService.getDecisions({
        query: {
          skip: (this._decisionsPage - 1) * PAGE_SIZE,
          take: PAGE_SIZE,
          status: this._status || undefined,
          ...this.#filterQuery(),
        },
      }));
    } catch (error) {
      if (request !== this.#decisionsRequest) return "stale";
      return statusOf(error) === 403 ? "forbidden" : "error";
    }

    if (request !== this.#decisionsRequest) return "stale";
    if (!data) return "error";

    this._decisions = data.items;
    this._decisionsTotal = data.total;
    this._notCheckedTotal = data.approvedNotCheckedTotal;
    return "ok";
  }

  async #loadAudit(): Promise<"ok" | "forbidden" | "error" | "stale"> {
    const request = ++this.#auditRequest;
    let data;
    try {
      ({ data } = await DecisionsService.getAudit({
        query: { skip: (this._auditPage - 1) * PAGE_SIZE, take: PAGE_SIZE, ...this.#filterQuery() },
      }));
    } catch (error) {
      if (request !== this.#auditRequest) return "stale";
      return statusOf(error) === 403 ? "forbidden" : "error";
    }

    if (request !== this.#auditRequest) return "stale";
    if (!data) return "error";

    this._audit = data.items;
    this._auditTotal = data.total;
    return "ok";
  }

  #applyFilters(change: () => void) {
    change();
    this._decisionsPage = 1;
    this._auditPage = 1;
    this.#loadAll();
  }

  #clearFilters() {
    this.#applyFilters(() => {
      this._formId = "";
      this._status = "";
      this._from = "";
      this._to = "";
    });
  }

  async #onDecisionsPageChange(event: UUIPaginationEvent) {
    this._decisionsPage = event.target.current;
    const result = await this.#loadDecisions();
    if (result === "forbidden") this._state = "forbidden";
    else if (result === "error") this._state = "error";
  }

  async #onAuditPageChange(event: UUIPaginationEvent) {
    this._auditPage = event.target.current;
    const result = await this.#loadAudit();
    if (result === "forbidden") this._state = "forbidden";
    else if (result === "error") this._state = "error";
  }

  #formName(formId: string | null | undefined) {
    if (!formId) return "";
    return this._forms.find((f) => f.id === formId)?.name || formId;
  }

  #statusLabel(status: string) {
    return (STATUSES as ReadonlyArray<string>).includes(status)
      ? this.localize.term(`formsGuard_status_${status}`)
      : status;
  }

  #actionLabel(action: string) {
    return (AUDIT_ACTIONS as ReadonlyArray<string>).includes(action)
      ? this.localize.term(`formsGuard_auditAction_${action}`)
      : action;
  }

  #renderFilters() {
    const formOptions = [
      { name: this.localize.term("formsGuard_logAllForms"), value: "", selected: !this._formId },
      ...this._forms.map((f) => ({ name: f.name || f.id, value: f.id, selected: f.id === this._formId })),
    ];
    const statusOptions = [
      { name: this.localize.term("formsGuard_logAllStatuses"), value: "", selected: !this._status },
      ...STATUSES.map((s) => ({ name: this.#statusLabel(s), value: s, selected: s === this._status })),
    ];
    const filtered = this._formId || this._status || this._from || this._to;

    return html`<div class="filters">
      <div class="filter">
        <uui-label for="log-form">${this.localize.term("formsGuard_logFormFilter")}</uui-label>
        <uui-select
          id="log-form"
          label=${this.localize.term("formsGuard_logFormFilter")}
          .options=${formOptions}
          .value=${this._formId}
          @change=${(e: Event) => this.#applyFilters(() => (this._formId = valueOf(e)))}></uui-select>
      </div>
      <div class="filter">
        <uui-label for="log-status">${this.localize.term("formsGuard_logStatusFilter")}</uui-label>
        <uui-select
          id="log-status"
          label=${this.localize.term("formsGuard_logStatusFilter")}
          .options=${statusOptions}
          .value=${this._status}
          @change=${(e: Event) => this.#applyFilters(() => (this._status = valueOf(e) as DecisionStatus | ""))}></uui-select>
      </div>
      <div class="filter">
        <uui-label for="log-from">${this.localize.term("formsGuard_logFrom")}</uui-label>
        <uui-input
          id="log-from"
          type="date"
          label=${this.localize.term("formsGuard_logFrom")}
          .value=${this._from}
          @change=${(e: Event) => this.#applyFilters(() => (this._from = valueOf(e)))}></uui-input>
      </div>
      <div class="filter">
        <uui-label for="log-to">${this.localize.term("formsGuard_logTo")}</uui-label>
        <uui-input
          id="log-to"
          type="date"
          label=${this.localize.term("formsGuard_logTo")}
          .value=${this._to}
          @change=${(e: Event) => this.#applyFilters(() => (this._to = valueOf(e)))}></uui-input>
      </div>
      <div class="filter clear">
        <uui-button
          look="secondary"
          label=${this.localize.term("formsGuard_logClear")}
          ?disabled=${!filtered}
          @click=${this.#clearFilters}></uui-button>
      </div>
    </div>
    <p class="hint">${this.localize.term("formsGuard_logStatusHint")}</p>`;
  }

  #renderNotChecked() {
    if (this._notCheckedTotal <= 0) return nothing;
    const showing = this._status === "ApprovedNotChecked";
    return html`<div class="warning" role="alert">
      <strong>${this.localize.term("formsGuard_logNotCheckedHeadline")}</strong>
      <p>${this.localize.term("formsGuard_logNotCheckedContent", this._notCheckedTotal)}</p>
      ${showing
        ? nothing
        : html`<uui-button
            look="primary"
            color="warning"
            label=${this.localize.term("formsGuard_logNotCheckedShow")}
            @click=${() =>
              this.#applyFilters(() => {
                this._status = "ApprovedNotChecked";
                this._tab = "decisions";
              })}></uui-button>`}
    </div>`;
  }

  #renderPagination(page: number, total: number, onChange: (event: UUIPaginationEvent) => void) {
    const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));
    return pages > 1
      ? html`<uui-pagination .current=${page} .total=${pages} @change=${onChange}></uui-pagination>`
      : nothing;
  }

  #renderDecisions() {
    if (this._decisions.length === 0) {
      return html`<p class="muted">${this.localize.term("formsGuard_decisionsEmpty")}</p>`;
    }

    return html`
      <div class="table-scroll">
        <uui-table aria-label=${this.localize.term("formsGuard_decisionsTableLabel")}>
          <uui-table-head>
            <uui-table-head-cell>${this.localize.term("formsGuard_columnCreated")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("formsGuard_columnForm")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("formsGuard_columnStatus")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("formsGuard_columnSource")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("formsGuard_columnRuleHit")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("formsGuard_columnProvider")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("formsGuard_columnProbabilities")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("formsGuard_columnAttempts")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("formsGuard_columnReviewer")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("formsGuard_columnRecord")}</uui-table-head-cell>
          </uui-table-head>
          ${this._decisions.map(
            (item) => html`
              <uui-table-row>
                <uui-table-cell>${formatDate(item.createdUtc)}</uui-table-cell>
                <uui-table-cell>${this.#formName(item.formId)}</uui-table-cell>
                <uui-table-cell><uui-tag look="secondary">${this.#statusLabel(item.status)}</uui-tag></uui-table-cell>
                <uui-table-cell>${item.source ?? ""}</uui-table-cell>
                <uui-table-cell>${item.ruleHit ?? ""}</uui-table-cell>
                <uui-table-cell>
                  ${item.provider ?? ""}${item.modelVersion
                    ? html`<div class="muted">${item.modelVersion}</div>`
                    : nothing}
                </uui-table-cell>
                <uui-table-cell>${renderProbabilities(item.probabilities, this.localize.term("formsGuard_probabilitiesNone"))}</uui-table-cell>
                <uui-table-cell>${item.attempts}</uui-table-cell>
                <uui-table-cell>${item.reviewerName ?? item.reviewer ?? ""}</uui-table-cell>
                <uui-table-cell><code title="Form ${item.formId}">${item.recordId}</code></uui-table-cell>
              </uui-table-row>
            `,
          )}
        </uui-table>
      </div>
      ${this.#renderPagination(this._decisionsPage, this._decisionsTotal, (e) => this.#onDecisionsPageChange(e))}
    `;
  }

  #renderAudit() {
    if (this._audit.length === 0) {
      return html`<p class="muted">${this.localize.term("formsGuard_auditEmpty")}</p>`;
    }

    return html`
      <div class="table-scroll">
        <uui-table aria-label=${this.localize.term("formsGuard_auditTableLabel")}>
          <uui-table-head>
            <uui-table-head-cell>${this.localize.term("formsGuard_columnTime")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("formsGuard_columnAction")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("formsGuard_columnBy")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("formsGuard_columnDetail")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("formsGuard_columnRecord")}</uui-table-head-cell>
          </uui-table-head>
          ${this._audit.map(
            (item) => html`
              <uui-table-row>
                <uui-table-cell>${formatDate(item.createdUtc)}</uui-table-cell>
                <uui-table-cell>${this.#actionLabel(item.action)}</uui-table-cell>
                <uui-table-cell>${item.actorName ?? item.actor ?? ""}</uui-table-cell>
                <uui-table-cell>${item.detail ?? ""}</uui-table-cell>
                <uui-table-cell>
                  ${item.recordId
                    ? html`<code title=${item.formId ? `Form ${item.formId}` : ""}>${item.recordId}</code>`
                    : item.formId
                      ? this.#formName(item.formId)
                      : nothing}
                </uui-table-cell>
              </uui-table-row>
            `,
          )}
        </uui-table>
      </div>
      ${this.#renderPagination(this._auditPage, this._auditTotal, (e) => this.#onAuditPageChange(e))}
    `;
  }

  #renderTab(tab: LogTab, icon: string, term: string, total: number, loading: boolean) {
    const label = this.localize.term(term);
    return html`<uui-tab label=${label} ?active=${this._tab === tab} @click=${() => (this._tab = tab)}>
      <uui-icon slot="icon" name=${icon}></uui-icon>
      ${label}${loading ? nothing : html` <span class="count">${total}</span>`}
    </uui-tab>`;
  }

  #renderBody() {
    switch (this._state) {
      case "forbidden":
        return html`<uui-box><p>${this.localize.term("formsGuard_decisionsForbidden")}</p></uui-box>`;
      case "error":
        return html`<uui-box><p>${this.localize.term("formsGuard_decisionsError")}</p></uui-box>`;
      default: {
        const loading = this._state === "loading";
        const decisions = this._tab === "decisions";
        return html`
          ${this.#renderNotChecked()}
          <uui-box>
            <uui-tab-group slot="header">
              ${this.#renderTab("decisions", "icon-list", "formsGuard_logTabDecisions", this._decisionsTotal, loading)}
              ${this.#renderTab("activity", "icon-history", "formsGuard_logTabActivity", this._auditTotal, loading)}
            </uui-tab-group>
            ${loading ? html`<uui-loader></uui-loader>` : decisions ? this.#renderDecisions() : this.#renderAudit()}
          </uui-box>
        `;
      }
    }
  }

  override render() {
    return html`
      <uui-box headline=${this.localize.term("formsGuard_decisionsHeadline")}>${this.#renderFilters()}</uui-box>
      ${this.#renderBody()}
    `;
  }

  static override styles = [
    sharedStyles,
    css`
      uui-box + uui-box,
      uui-box + .warning,
      .warning + uui-box {
        margin-top: var(--uui-size-layout-1);
      }
      .hint {
        color: var(--uui-color-text-alt);
        margin: var(--uui-size-space-3) 0 0;
      }
      .filters {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-4);
        align-items: flex-end;
      }
      .filter {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-1);
      }
      uui-tab-group {
        --uui-tab-divider: var(--uui-color-border);
        width: 100%;
      }
      .count {
        color: var(--uui-color-text-alt);
        font-weight: normal;
      }
      .table-scroll {
        overflow-x: auto;
      }
      .warning p {
        margin: var(--uui-size-space-2) 0 var(--uui-size-space-3);
      }
    `,
  ];
}

export default FormsGuardDecisionsViewElement;

declare global {
  interface HTMLElementTagNameMap {
    "cogworks-forms-guard-decisions-view": FormsGuardDecisionsViewElement;
  }
}
