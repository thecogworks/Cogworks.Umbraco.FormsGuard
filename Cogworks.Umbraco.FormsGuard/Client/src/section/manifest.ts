import { FORMS_GUARD_SECTION_ALIAS } from "../permissions/constants.js";

export const manifests: Array<UmbExtensionManifest> = [
  {
    type: "section",
    alias: FORMS_GUARD_SECTION_ALIAS,
    name: "Forms Guard Section",
    weight: 10,
    meta: {
      label: "#formsGuard_sectionLabel",
      pathname: "forms-guard",
    },
  },
  {
    // Same visibility as Decisions; the API enforces the Review verb. Higher weight puts it first.
    type: "sectionView",
    alias: "Cogworks.FormsGuard.SectionView.ReviewQueue",
    name: "Forms Guard Review Queue Section View",
    element: () => import("./review-queue-view.element.js"),
    weight: 200,
    meta: {
      label: "#formsGuard_reviewTab",
      pathname: "review",
      icon: "icon-inbox",
    },
    conditions: [
      {
        alias: "Umb.Condition.SectionAlias",
        match: FORMS_GUARD_SECTION_ALIAS,
      },
    ],
  },
  {
    // Shown to everyone with the section, so a user without Review sees the "no permission" state
    // rather than an empty section. The API enforces the Review verb.
    type: "sectionView",
    alias: "Cogworks.FormsGuard.SectionView.Decisions",
    name: "Forms Guard Decisions Section View",
    element: () => import("./decisions-view.element.js"),
    weight: 100,
    meta: {
      label: "#formsGuard_decisionsTab",
      pathname: "decisions",
      icon: "icon-shield",
    },
    conditions: [
      {
        alias: "Umb.Condition.SectionAlias",
        match: FORMS_GUARD_SECTION_ALIAS,
      },
    ],
  },
  {
    // Shown to everyone with the section; the API enforces the ManageSettings verb and the view shows the
    // "no permission" state on 403. Lowest weight puts it last.
    type: "sectionView",
    alias: "Cogworks.FormsGuard.SectionView.Settings",
    name: "Forms Guard Settings Section View",
    element: () => import("./settings-view.element.js"),
    weight: 50,
    meta: {
      label: "#formsGuard_settingsTab",
      pathname: "settings",
      icon: "icon-settings",
    },
    conditions: [
      {
        alias: "Umb.Condition.SectionAlias",
        match: FORMS_GUARD_SECTION_ALIAS,
      },
    ],
  },
];
