import { css, html } from "@umbraco-cms/backoffice/external/lit";

/** Rows per page in the queue and the log. */
export const PAGE_SIZE = 20;

export type ViewState = "loading" | "ready" | "forbidden" | "error";

/** The HTTP status of a rejected API call, if it has one. */
export const statusOf = (error: unknown) => (error as { status?: number } | undefined)?.status;

/** Reads the value of the UUI or native control that raised the event. */
export const valueOf = (event: Event) => (event.target as unknown as { value: string }).value ?? "";

export const formatDate = (value: string) => new Date(value).toLocaleString("en-GB");

/** One line per probability, or `noneLabel` (muted) when there are none. */
export function renderProbabilities(
  probabilities: { [key: string]: number } | null | undefined,
  noneLabel: string,
) {
  if (!probabilities) return html`<span class="muted">${noneLabel}</span>`;
  return html`${Object.entries(probabilities).map(
    ([key, value]) => html`<div><code>${key}</code> ${value.toFixed(2)}</div>`,
  )}`;
}

/** Styles every section view uses; margins on `.warning` stay with each view. */
export const sharedStyles = css`
  :host {
    display: block;
    padding: var(--uui-size-layout-1);
  }
  .muted {
    color: var(--uui-color-text-alt);
  }
  uui-pagination {
    display: block;
    margin-top: var(--uui-size-space-5);
  }
  .warning {
    padding: var(--uui-size-space-3) var(--uui-size-space-4);
    border-left: 4px solid var(--uui-color-warning-standalone);
    background: var(--uui-color-warning, var(--uui-color-surface-alt));
    color: var(--uui-color-warning-contrast, inherit);
  }
`;
