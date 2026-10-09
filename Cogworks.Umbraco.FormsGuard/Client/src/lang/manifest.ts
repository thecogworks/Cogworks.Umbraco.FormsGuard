export const manifests: Array<UmbExtensionManifest> = [
  {
    type: "localization",
    alias: "Cogworks.FormsGuard.Localization.En",
    name: "Forms Guard English",
    meta: { culture: "en" },
    js: () => import("./en.js"),
  },
];
