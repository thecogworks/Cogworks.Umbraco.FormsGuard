import {
  FORMS_GUARD_ENTITY_TYPE,
  FORMS_GUARD_MANAGE_SETTINGS_VERB,
  FORMS_GUARD_REVIEW_VERB,
} from "./constants.js";

// Custom verbs shown in the user group editor. The server enforces them (FormsGuardAuthorization.cs).
export const manifests: Array<UmbExtensionManifest> = [
  {
    type: "entityUserPermission",
    alias: "Cogworks.FormsGuard.UserPermission.Review",
    name: "Forms Guard Review Permission",
    forEntityTypes: [FORMS_GUARD_ENTITY_TYPE],
    weight: 200,
    meta: {
      verbs: [FORMS_GUARD_REVIEW_VERB],
      label: "#formsGuard_permissionReviewLabel",
      description: "#formsGuard_permissionReviewDescription",
    },
  },
  {
    type: "entityUserPermission",
    alias: "Cogworks.FormsGuard.UserPermission.ManageSettings",
    name: "Forms Guard Manage Settings Permission",
    forEntityTypes: [FORMS_GUARD_ENTITY_TYPE],
    weight: 100,
    meta: {
      verbs: [FORMS_GUARD_MANAGE_SETTINGS_VERB],
      label: "#formsGuard_permissionManageSettingsLabel",
      description: "#formsGuard_permissionManageSettingsDescription",
    },
  },
];
