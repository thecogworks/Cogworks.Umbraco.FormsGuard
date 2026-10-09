import { defineConfig } from "vite";

export default defineConfig({
  build: {
    lib: {
      entry: "src/bundle.manifests.ts", // Bundle registers one or more manifests
      formats: ["es"],
      fileName: "forms-guard",
    },
    // The csproj serves wwwroot at /App_Plugins/FormsGuard, so build flat into wwwroot.
    outDir: "../wwwroot",
    emptyOutDir: true,
    sourcemap: true,
    rollupOptions: {
      external: [/^@umbraco/],
    },
  },
});
