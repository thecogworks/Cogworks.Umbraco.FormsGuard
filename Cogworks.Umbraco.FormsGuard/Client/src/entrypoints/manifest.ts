export const manifests: Array<UmbExtensionManifest> = [
  {
    name: "Forms Guard Entrypoint",
    alias: "Cogworks.FormsGuard.Entrypoint",
    type: "backofficeEntryPoint",
    js: () => import("./entrypoint.js"),
  },
];
