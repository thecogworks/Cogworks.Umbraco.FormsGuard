import { css, customElement, html, nothing, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { umbConfirmModal } from "@umbraco-cms/backoffice/modal";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import {
  SettingsService,
  type FormFieldModel,
  type FormSettingsModel,
  type FormSettingsResponse,
  type FormReadinessModel,
  type FormSummary,
  type QuestionModel,
  type RuleModel,
  type ThresholdsModel,
} from "../api/index.js";
import { sharedStyles, statusOf, valueOf, type ViewState } from "./shared.js";

const ROLES = ["SpamSignal", "GenuineSignal", "Informational"] as const;
const POLICIES = ["ApproveNotChecked", "Review"] as const;
const RULE_TYPES = ["BlockedDomain", "AllowedDomain", "BlockedPhrase"] as const;
type RuleType = (typeof RULE_TYPES)[number];
// Non-empty so it does not collide with uui-select's hidden placeholder option (value "").
const EMAIL_AUTO = "auto";

const checkedOf = (event: Event) => Boolean((event.target as unknown as { checked: boolean }).checked);

@customElement("cogworks-forms-guard-settings-view")
export class FormsGuardSettingsViewElement extends UmbLitElement {
  @state() private _state: ViewState = "loading";
  @state() private _forms: Array<FormSummary> = [];

  // Editor: null means the form list is shown.
  @state() private _form: FormSettingsResponse | null = null;
  @state() private _guarded = false;
  @state() private _working: FormSettingsModel = {};
  @state() private _allowlistCustom = false;
  @state() private _customIds: Array<string> = [];
  @state() private _saveErrors: Array<string> = [];
  @state() private _busy = false;

  @state() private _rules: Array<RuleModel> = [];
  @state() private _newPatterns: Partial<Record<RuleType, string>> = {};
  @state() private _ruleErrors: Partial<Record<RuleType, Array<string>>> = {};

  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => {
      this.#notificationContext = context;
    });
  }

  override connectedCallback() {
    super.connectedCallback();
    this.#loadForms();
  }

  // --- Loading -------------------------------------------------------------------------------------------------

  async #loadForms() {
    this._state = "loading";
    this._form = null;

    // The client inherits Umbraco's config, which rejects on error statuses rather than returning them.
    try {
      const { data } = await SettingsService.getForms();
      if (!data) {
        this._state = "error";
        return;
      }
      this._forms = data;
      this._state = "ready";
    } catch (error) {
      this._state = statusOf(error) === 403 ? "forbidden" : "error";
    }
  }

  async #openForm(formId: string) {
    this._state = "loading";
    try {
      const [settings, rules] = await Promise.all([
        SettingsService.getFormSettings({ path: { formId } }),
        SettingsService.getRules({ path: { formId } }),
      ]);
      if (!settings.data || !rules.data) {
        this._state = "error";
        return;
      }
      this.#applyResponse(settings.data);
      this._rules = rules.data;
      this._newPatterns = {};
      this._ruleErrors = {};
      this._state = "ready";
    } catch (error) {
      this._state = statusOf(error) === 403 ? "forbidden" : "error";
    }
  }

  async #reloadRules() {
    if (!this._form) return;
    try {
      const { data } = await SettingsService.getRules({ path: { formId: this._form.formId } });
      if (data) this._rules = data;
    } catch (error) {
      if (statusOf(error) === 403) this._state = "forbidden";
    }
  }

  /** Takes the server's copy of the form as the new working copy. */
  #applyResponse(response: FormSettingsResponse) {
    this._form = response;
    this._guarded = response.guarded;
    this._working = structuredClone(response.settings);
    const allowed = response.settings.allowedFieldIds;
    this._allowlistCustom = allowed != null;
    this._customIds = allowed ? [...allowed] : [];
    this._saveErrors = [];
  }

  // --- Helpers -------------------------------------------------------------------------------------------------

  /** The `errors` list of a 400 response, or null when the error is something else. */
  #errorsOf(error: unknown): Array<string> | null {
    if (statusOf(error) !== 400) return null;
    const errors = (error as { errors?: unknown }).errors;
    return Array.isArray(errors) ? errors.filter((e): e is string => typeof e === "string") : null;
  }

  #notify(color: "positive" | "warning" | "danger", key: string) {
    this.#notificationContext?.peek(color, { data: { message: this.localize.term(key) } });
  }

  #fieldLabel(field: FormFieldModel) {
    return field.caption || field.alias || field.id;
  }

  #update(patch: Partial<FormSettingsModel>) {
    this._working = { ...this._working, ...patch };
  }

  #updateQuestion(index: number, patch: Partial<QuestionModel>) {
    const questions = [...(this._working.questions ?? [])];
    questions[index] = { ...questions[index], ...patch };
    this.#update({ questions });
  }

  #updateThreshold(key: keyof ThresholdsModel, raw: string) {
    const value = raw.trim() === "" ? null : Number(raw);
    this.#update({ thresholds: { ...this._working.thresholds, [key]: Number.isNaN(value) ? null : value } });
  }

  // --- Actions -------------------------------------------------------------------------------------------------

  async #save() {
    if (!this._form || this._busy) return;
    const form = this._form;

    const nonExcluded = new Set(form.fields.filter((f) => !f.excluded).map((f) => f.id));
    const settings: FormSettingsModel = {
      ...this._working,
      allowedFieldIds: this._allowlistCustom ? this._customIds.filter((id) => nonExcluded.has(id)) : null,
    };

    this._busy = true;
    try {
      const { data } = await SettingsService.saveFormSettings({
        path: { formId: form.formId },
        body: { guarded: this._guarded, settings },
      });
      if (data) {
        this.#applyResponse(data);
        this.#notify("positive", "formsGuard_settingsSaved");
      } else {
        this.#notify("danger", "formsGuard_settingsSaveFailed");
      }
    } catch (error) {
      const errors = this.#errorsOf(error);
      if (errors) {
        // Keep the user's edits; the API is the only validator.
        this._saveErrors = errors;
      } else {
        switch (statusOf(error)) {
          case 403:
            this._state = "forbidden";
            break;
          case 404:
            this.#notify("danger", "formsGuard_settingsFormNotFound");
            break;
          default:
            this.#notify("danger", "formsGuard_settingsSaveFailed");
        }
      }
    }
    this._busy = false;
  }

  async #addRule(ruleType: RuleType) {
    if (!this._form || this._busy) return;
    const pattern = this._newPatterns[ruleType] ?? "";

    this._busy = true;
    try {
      const { data } = await SettingsService.createRule({
        path: { formId: this._form.formId },
        body: { ruleType, pattern },
      });
      if (data) this._rules = [...this._rules, data];
      this._newPatterns = { ...this._newPatterns, [ruleType]: "" };
      this._ruleErrors = { ...this._ruleErrors, [ruleType]: [] };
      this.#notify("positive", "formsGuard_settingsRuleAdded");
    } catch (error) {
      const errors = this.#errorsOf(error);
      if (errors) {
        this._ruleErrors = { ...this._ruleErrors, [ruleType]: errors };
      } else if (statusOf(error) === 403) {
        this._state = "forbidden";
      } else if (statusOf(error) === 404) {
        this.#notify("danger", "formsGuard_settingsFormNotFound");
      } else {
        this.#notify("danger", "formsGuard_settingsRuleFailed");
      }
    }
    this._busy = false;
  }

  async #deleteRule(rule: RuleModel) {
    if (!this._form || this._busy) return;
    try {
      await umbConfirmModal(this, {
        headline: this.localize.term("formsGuard_settingsRuleDeleteHeadline"),
        content: this.localize.term("formsGuard_settingsRuleDeleteContent", rule.pattern),
        color: "danger",
        confirmLabel: this.localize.term("formsGuard_settingsRuleDelete"),
      });
    } catch {
      return;
    }

    this._busy = true;
    try {
      await SettingsService.deleteRule({ path: { formId: this._form.formId, ruleId: rule.id } });
      this._rules = this._rules.filter((r) => r.id !== rule.id);
      this.#notify("positive", "formsGuard_settingsRuleDeleted");
    } catch (error) {
      switch (statusOf(error)) {
        case 403:
          this._state = "forbidden";
          break;
        case 404:
          this.#notify("warning", "formsGuard_settingsRuleNotFound");
          await this.#reloadRules();
          break;
        default:
          this.#notify("danger", "formsGuard_settingsRuleFailed");
      }
    }
    this._busy = false;
  }

  // --- Rendering: list -----------------------------------------------------------------------------------------

  #renderGuardedTag(guarded: boolean) {
    return guarded
      ? html`<uui-tag look="primary" color="positive">${this.localize.term("formsGuard_settingsGuarded")}</uui-tag>`
      : html`<uui-tag look="secondary">${this.localize.term("formsGuard_settingsNotGuarded")}</uui-tag>`;
  }

  #renderAttentionTag(readiness: FormReadinessModel | null | undefined) {
    if (!readiness || (!readiness.manualApprovalOff && readiness.submitWorkflowNames.length === 0)) return nothing;
    return html`<uui-tag look="primary" color="warning">${this.localize.term("formsGuard_settingsNeedsAttention")}</uui-tag>`;
  }

  #renderList() {
    if (this._forms.length === 0) {
      return html`<p class="muted">${this.localize.term("formsGuard_settingsFormsEmpty")}</p>`;
    }
    return html`
      <uui-table aria-label=${this.localize.term("formsGuard_settingsFormsTableLabel")}>
        <uui-table-head>
          <uui-table-head-cell>${this.localize.term("formsGuard_settingsColumnName")}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term("formsGuard_settingsColumnGuarded")}</uui-table-head-cell>
          <uui-table-head-cell></uui-table-head-cell>
        </uui-table-head>
        ${this._forms.map(
          (form) => html`
            <uui-table-row>
              <uui-table-cell>
                <uui-button
                  look="default"
                  compact
                  label=${form.name}
                  @click=${() => this.#openForm(form.id)}>${form.name}</uui-button>
              </uui-table-cell>
              <uui-table-cell>
                <span class="tags">
                  ${this.#renderGuardedTag(form.guarded)} ${form.guarded ? this.#renderAttentionTag(form.readiness) : nothing}
                </span>
              </uui-table-cell>
              <uui-table-cell>
                <uui-button
                  look="secondary"
                  label=${this.localize.term("formsGuard_settingsEdit")}
                  @click=${() => this.#openForm(form.id)}></uui-button>
              </uui-table-cell>
            </uui-table-row>
          `,
        )}
      </uui-table>
    `;
  }

  // --- Rendering: editor ---------------------------------------------------------------------------------------

  #renderGeneral() {
    return html`<uui-box headline=${this.localize.term("formsGuard_settingsGeneralHeadline")}>
      <div class="field">
        <uui-toggle
          label=${this.localize.term("formsGuard_settingsGuardedLabel")}
          .checked=${this._guarded}
          @change=${(e: Event) => (this._guarded = checkedOf(e))}></uui-toggle>
        <p class="hint">${this.localize.term("formsGuard_settingsGuardedHint")}</p>
      </div>
      <div class="field">
        <uui-label for="organisation">${this.localize.term("formsGuard_settingsOrganisationLabel")}</uui-label>
        <p class="hint">${this.localize.term("formsGuard_settingsOrganisationHint")}</p>
        <uui-textarea
          id="organisation"
          label=${this.localize.term("formsGuard_settingsOrganisationLabel")}
          .value=${this._working.organisation ?? ""}
          @input=${(e: Event) => this.#update({ organisation: valueOf(e) })}></uui-textarea>
      </div>
    </uui-box>`;
  }

  #renderAllowlist(fields: Array<FormFieldModel>) {
    const selected = new Set(this._customIds);
    return html`<uui-box headline=${this.localize.term("formsGuard_settingsAllowlistHeadline")}>
      <uui-radio-group
        .value=${this._allowlistCustom ? "custom" : "default"}
        @change=${(e: Event) => (this._allowlistCustom = valueOf(e) === "custom")}>
        <uui-radio value="default" label=${this.localize.term("formsGuard_settingsAllowlistDefault")}></uui-radio>
        <uui-radio value="custom" label=${this.localize.term("formsGuard_settingsAllowlistCustom")}></uui-radio>
      </uui-radio-group>
      <p class="hint">
        ${this._allowlistCustom
          ? this.localize.term("formsGuard_settingsAllowlistCustomHint")
          : this.localize.term("formsGuard_settingsAllowlistDefaultHint")}
      </p>
      ${this._allowlistCustom
        ? fields.length === 0
          ? html`<p class="muted">${this.localize.term("formsGuard_settingsNoFields")}</p>`
          : html`<div class="checklist">
              ${fields.map(
                (field) => html`<uui-checkbox
                  label=${field.excluded
                    ? `${this.#fieldLabel(field)} (${this.localize.term("formsGuard_settingsAllowlistExcluded")})`
                    : this.#fieldLabel(field)}
                  ?disabled=${field.excluded}
                  .checked=${!field.excluded && selected.has(field.id)}
                  @change=${(e: Event) => {
                    const checked = checkedOf(e);
                    this._customIds = checked
                      ? [...this._customIds.filter((id) => id !== field.id), field.id]
                      : this._customIds.filter((id) => id !== field.id);
                  }}></uui-checkbox>`,
              )}
            </div>`
        : nothing}
      <div class="field">
        <uui-toggle
          label=${this.localize.term("formsGuard_settingsSendEmailDomainLabel")}
          .checked=${this._working.sendEmailDomain ?? false}
          @change=${(e: Event) => this.#update({ sendEmailDomain: checkedOf(e) })}></uui-toggle>
        <p class="hint">${this.localize.term("formsGuard_settingsSendEmailDomainHint")}</p>
      </div>
    </uui-box>`;
  }

  #renderEmail(fields: Array<FormFieldModel>) {
    const override = this._working.emailFieldId ?? null;
    const candidates = fields.filter((f) => f.looksLikeEmail);

    let status: unknown = nothing;
    if (!override) {
      if (candidates.length === 1) {
        status = html`<p>${this.localize.term("formsGuard_settingsEmailDetected", this.#fieldLabel(candidates[0]))}</p>`;
      } else {
        const key = candidates.length === 0 ? "formsGuard_settingsEmailNoneWarning" : "formsGuard_settingsEmailSeveralWarning";
        status = html`<div class="warning" role="alert">${this.localize.term(key)}</div>`;
      }
    }

    const choices = fields.filter((f) => !f.excluded || f.id === override);
    const options = [
      { name: this.localize.term("formsGuard_settingsEmailAuto"), value: EMAIL_AUTO, selected: !override },
      ...choices.map((f) => ({ name: this.#fieldLabel(f), value: f.id, selected: f.id === override })),
    ];

    return html`<uui-box headline=${this.localize.term("formsGuard_settingsEmailHeadline")}>
      <p class="hint">${this.localize.term("formsGuard_settingsEmailHint")}</p>
      ${status}
      <div class="field">
        <uui-label for="email-field">${this.localize.term("formsGuard_settingsEmailFieldLabel")}</uui-label>
        <uui-select
          id="email-field"
          label=${this.localize.term("formsGuard_settingsEmailFieldLabel")}
          .options=${options}
          @change=${(e: Event) => {
            const value = valueOf(e);
            this.#update({ emailFieldId: value && value !== EMAIL_AUTO ? value : null });
          }}></uui-select>
      </div>
    </uui-box>`;
  }

  #renderQuestion(question: QuestionModel, index: number) {
    const roleOptions = ROLES.map((role) => ({
      name: this.localize.term(`formsGuard_settingsRole_${role}`),
      value: role,
      selected: question.role === role,
    }));
    return html`<div class="question">
      <div class="question-head">
        <strong>${this.localize.term("formsGuard_settingsQuestionLabel", index + 1)}</strong>
        <uui-toggle
          label=${this.localize.term("formsGuard_settingsQuestionEnabled")}
          .checked=${question.enabled ?? false}
          @change=${(e: Event) => this.#updateQuestion(index, { enabled: checkedOf(e) })}></uui-toggle>
        <uui-button
          look="secondary"
          color="danger"
          compact
          label=${this.localize.term("formsGuard_settingsQuestionRemove")}
          @click=${() => this.#update({ questions: (this._working.questions ?? []).filter((_, i) => i !== index) })}></uui-button>
      </div>
      <div class="grid">
        <div class="field">
          <uui-label>${this.localize.term("formsGuard_settingsQuestionKey")}</uui-label>
          <uui-input
            label=${this.localize.term("formsGuard_settingsQuestionKey")}
            .value=${question.key ?? ""}
            @input=${(e: Event) => this.#updateQuestion(index, { key: valueOf(e) })}></uui-input>
        </div>
        <div class="field">
          <uui-label>${this.localize.term("formsGuard_settingsQuestionRole")}</uui-label>
          <uui-select
            label=${this.localize.term("formsGuard_settingsQuestionRole")}
            .options=${roleOptions}
            @change=${(e: Event) => this.#updateQuestion(index, { role: valueOf(e) })}></uui-select>
        </div>
      </div>
      <div class="field">
        <uui-label>${this.localize.term("formsGuard_settingsQuestionText")}</uui-label>
        <uui-textarea
          label=${this.localize.term("formsGuard_settingsQuestionText")}
          .value=${question.text ?? ""}
          @input=${(e: Event) => this.#updateQuestion(index, { text: valueOf(e) })}></uui-textarea>
      </div>
      <div class="grid">
        <div class="field">
          <uui-label>${this.localize.term("formsGuard_settingsQuestionTrueCriteria")}</uui-label>
          <uui-textarea
            label=${this.localize.term("formsGuard_settingsQuestionTrueCriteria")}
            .value=${question.trueCriteria ?? ""}
            @input=${(e: Event) => this.#updateQuestion(index, { trueCriteria: valueOf(e) || null })}></uui-textarea>
        </div>
        <div class="field">
          <uui-label>${this.localize.term("formsGuard_settingsQuestionFalseCriteria")}</uui-label>
          <uui-textarea
            label=${this.localize.term("formsGuard_settingsQuestionFalseCriteria")}
            .value=${question.falseCriteria ?? ""}
            @input=${(e: Event) => this.#updateQuestion(index, { falseCriteria: valueOf(e) || null })}></uui-textarea>
        </div>
      </div>
    </div>`;
  }

  #renderQuestions() {
    const questions = this._working.questions ?? [];
    return html`<uui-box headline=${this.localize.term("formsGuard_settingsQuestionsHeadline")}>
      <p class="hint">${this.localize.term("formsGuard_settingsQuestionsHint")}</p>
      ${questions.map((q, i) => this.#renderQuestion(q, i))}
      <uui-button
        look="secondary"
        label=${this.localize.term("formsGuard_settingsQuestionAdd")}
        @click=${() =>
          this.#update({
            questions: [...questions, { key: "guard.", text: "", role: "SpamSignal", enabled: true }],
          })}></uui-button>
    </uui-box>`;
  }

  #renderThreshold(key: keyof ThresholdsModel, labelKey: string) {
    const value = this._working.thresholds?.[key];
    return html`<div class="field">
      <uui-label for=${key}>${this.localize.term(labelKey)}</uui-label>
      <uui-input
        id=${key}
        type="number"
        step="0.01"
        min="0"
        max="1"
        label=${this.localize.term(labelKey)}
        .value=${value == null ? "" : String(value)}
        @input=${(e: Event) => this.#updateThreshold(key, valueOf(e))}></uui-input>
    </div>`;
  }

  #renderThresholds() {
    return html`<uui-box headline=${this.localize.term("formsGuard_settingsThresholdsHeadline")}>
      <p class="hint">${this.localize.term("formsGuard_settingsThresholdsHint")}</p>
      <div class="grid three">
        ${this.#renderThreshold("quarantineSpamMin", "formsGuard_settingsQuarantineSpamMin")}
        ${this.#renderThreshold("approveSpamMax", "formsGuard_settingsApproveSpamMax")}
        ${this.#renderThreshold("approveGenuineMin", "formsGuard_settingsApproveGenuineMin")}
      </div>
    </uui-box>`;
  }

  #renderFailurePolicy() {
    const options = POLICIES.map((policy) => ({
      name: this.localize.term(`formsGuard_settingsPolicy_${policy}`),
      value: policy,
      selected: this._working.failurePolicy === policy,
    }));
    return html`<uui-box headline=${this.localize.term("formsGuard_settingsFailurePolicyHeadline")}>
      <p class="hint">${this.localize.term("formsGuard_settingsFailurePolicyHint")}</p>
      <uui-select
        label=${this.localize.term("formsGuard_settingsFailurePolicyHeadline")}
        .options=${options}
        @change=${(e: Event) => this.#update({ failurePolicy: valueOf(e) })}></uui-select>
    </uui-box>`;
  }

  #renderErrors(headingKey: string, errors: Array<string> | undefined) {
    if (!errors || errors.length === 0) return nothing;
    return html`<div class="errors" role="alert">
      <p>${this.localize.term(headingKey)}</p>
      <ul>
        ${errors.map((e) => html`<li>${e}</li>`)}
      </ul>
    </div>`;
  }

  #renderRuleList(ruleType: RuleType) {
    const rules = this._rules.filter((r) => r.ruleType === ruleType);
    return html`<div class="rule-list">
      <h4>${this.localize.term(`formsGuard_settingsRuleType_${ruleType}`)}</h4>
      ${rules.length === 0
        ? html`<p class="muted">${this.localize.term("formsGuard_settingsRulesEmpty")}</p>`
        : html`<ul>
            ${rules.map(
              (rule) => html`<li>
                <code>${rule.pattern}</code>
                <uui-button
                  look="secondary"
                  color="danger"
                  compact
                  label=${this.localize.term("formsGuard_settingsRuleDelete")}
                  ?disabled=${this._busy}
                  @click=${() => this.#deleteRule(rule)}></uui-button>
              </li>`,
            )}
          </ul>`}
      <form
        class="rule-form"
        @submit=${(e: Event) => {
          e.preventDefault();
          this.#addRule(ruleType);
        }}>
        <uui-input
          label=${this.localize.term(`formsGuard_settingsRuleType_${ruleType}`)}
          placeholder=${this.localize.term(`formsGuard_settingsRulePlaceholder_${ruleType}`)}
          .value=${this._newPatterns[ruleType] ?? ""}
          @input=${(e: Event) => (this._newPatterns = { ...this._newPatterns, [ruleType]: valueOf(e) })}></uui-input>
        <uui-button
          type="submit"
          look="secondary"
          label=${this.localize.term("formsGuard_settingsRuleAdd")}
          ?disabled=${this._busy}></uui-button>
      </form>
      ${this.#renderErrors("formsGuard_settingsRuleErrorsHeading", this._ruleErrors[ruleType])}
    </div>`;
  }

  #renderRules() {
    return html`<uui-box headline=${this.localize.term("formsGuard_settingsRulesHeadline")}>
      <p class="hint">${this.localize.term("formsGuard_settingsRulesHint")}</p>
      <div class="grid three">${RULE_TYPES.map((t) => this.#renderRuleList(t))}</div>
    </uui-box>`;
  }

  #renderReadiness(readiness: FormReadinessModel) {
    const names = readiness.submitWorkflowNames;
    if (!this._guarded || (!readiness.manualApprovalOff && names.length === 0)) return nothing;
    return html`<div class="warning" role="alert">
      <strong>${this.localize.term("formsGuard_settingsReadinessHeadline")}</strong>
      ${readiness.manualApprovalOff ? html`<p>${this.localize.term("formsGuard_settingsReadinessManualApproval")}</p>` : nothing}
      ${names.length > 0
        ? html`<p>${this.localize.term(
            "formsGuard_settingsReadinessSubmitWorkflows",
            names
              .map((n) => (n.trim() ? n : this.localize.term("formsGuard_settingsReadinessUnnamedWorkflow")))
              .join(", "),
          )}</p>`
        : nothing}
    </div>`;
  }

  #renderEditor(form: FormSettingsResponse) {
    return html`
      <div class="editor-head">
        <uui-button
          look="secondary"
          label=${this.localize.term("formsGuard_settingsBack")}
          ?disabled=${this._busy}
          @click=${() => this.#loadForms()}></uui-button>
        <h2>${form.formName}</h2>
        ${this.#renderGuardedTag(form.guarded)}
      </div>
      ${this.#renderReadiness(form.readiness)} ${this.#renderGeneral()} ${this.#renderAllowlist(form.fields)} ${this.#renderEmail(form.fields)}
      ${this.#renderQuestions()} ${this.#renderThresholds()} ${this.#renderFailurePolicy()}
      <div class="save">
        ${this.#renderErrors("formsGuard_settingsSaveErrorsHeading", this._saveErrors)}
        <uui-button
          look="primary"
          color="positive"
          label=${this.localize.term("formsGuard_settingsSave")}
          ?disabled=${this._busy}
          @click=${() => this.#save()}></uui-button>
      </div>
      ${this.#renderRules()}
    `;
  }

  override render() {
    switch (this._state) {
      case "loading":
        return html`<uui-loader></uui-loader>`;
      case "forbidden":
        return html`<uui-box headline=${this.localize.term("formsGuard_settingsHeadline")}>
          <p>${this.localize.term("formsGuard_settingsForbidden")}</p>
        </uui-box>`;
      case "error":
        return html`<uui-box headline=${this.localize.term("formsGuard_settingsHeadline")}>
          <p>${this.localize.term("formsGuard_settingsError")}</p>
          <uui-button
            look="secondary"
            label=${this.localize.term("formsGuard_settingsBack")}
            @click=${() => this.#loadForms()}></uui-button>
        </uui-box>`;
      default:
        return this._form
          ? this.#renderEditor(this._form)
          : html`<uui-box headline=${this.localize.term("formsGuard_settingsHeadline")}>${this.#renderList()}</uui-box>`;
    }
  }

  static override styles = [
    sharedStyles,
    css`
      uui-box {
        margin-bottom: var(--uui-size-space-5);
      }
      .hint {
        color: var(--uui-color-text-alt);
        margin: var(--uui-size-space-1) 0 var(--uui-size-space-3);
      }
      .field {
        margin-bottom: var(--uui-size-space-4);
      }
      .field uui-textarea,
      .field uui-select {
        display: block;
        width: 100%;
      }
      /* uui-input lays out its inner input with its own inline-flex; overriding display collapses it. */
      .field uui-input {
        width: 100%;
      }
      .grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));
        gap: var(--uui-size-space-4);
      }
      .checklist {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-2);
        margin-bottom: var(--uui-size-space-4);
      }
      .warning {
        margin-bottom: var(--uui-size-space-4);
      }
      .warning p {
        margin: var(--uui-size-space-2) 0 0;
      }
      .tags {
        display: inline-flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-2);
      }
      .errors {
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        border-left: 4px solid var(--uui-color-danger);
        background: var(--uui-color-surface-alt);
        color: var(--uui-color-danger);
        margin-bottom: var(--uui-size-space-3);
      }
      .errors p {
        margin: 0 0 var(--uui-size-space-2);
        font-weight: bold;
      }
      .errors ul {
        margin: 0;
        padding-left: var(--uui-size-space-5);
      }
      .question {
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        padding: var(--uui-size-space-4);
        margin-bottom: var(--uui-size-space-4);
      }
      .question-head {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-4);
        margin-bottom: var(--uui-size-space-3);
      }
      .question-head strong {
        flex: 1;
      }
      .editor-head {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-4);
        margin-bottom: var(--uui-size-space-5);
      }
      .editor-head h2 {
        margin: 0;
      }
      .save {
        margin-bottom: var(--uui-size-space-5);
      }
      .rule-list h4 {
        margin: 0 0 var(--uui-size-space-2);
      }
      .rule-list ul {
        list-style: none;
        margin: 0 0 var(--uui-size-space-3);
        padding: 0;
      }
      .rule-list li {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-1) 0;
        overflow-wrap: anywhere;
      }
      .rule-form {
        display: flex;
        gap: var(--uui-size-space-2);
        margin-bottom: var(--uui-size-space-2);
      }
      .rule-form uui-input {
        flex: 1;
      }
    `,
  ];
}

export default FormsGuardSettingsViewElement;

declare global {
  interface HTMLElementTagNameMap {
    "cogworks-forms-guard-settings-view": FormsGuardSettingsViewElement;
  }
}
