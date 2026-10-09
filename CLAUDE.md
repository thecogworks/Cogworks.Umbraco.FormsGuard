# Project Instructions for Claude Code

Cogworks.Umbraco.FormsGuard: spam filtering for Umbraco Forms. Entries are checked after they are saved, and suspected spam is quarantined or held for review. `Cogworks.Umbraco.FormsGuard.UmbracoAI` is an optional decision provider that uses an Umbraco.AI profile.

## Rules

- **Tests:** `dotnet test Cogworks.Umbraco.FormsGuard.sln`.
- **Package management:** Central Package Management. Versions live in `Directory.Packages.props`. `<PackageReference>` entries carry no version.
- **Backoffice client:** `Cogworks.Umbraco.FormsGuard/Client` (Lit, Vite, TypeScript). It builds into the gitignored `wwwroot`. `dotnet pack` runs `npm ci` and `npm run build` itself, so packing needs Node and npm.
- **Umbraco backoffice code:** load the matching official `umbraco-*` skill before writing extension code. Do not write it from memory.

## Two-repo split (CRITICAL)

This public repo holds the source. Planning and tooling live in the private sibling repo `../Cogworks.Umbraco.FormsGuard-planning/`. `_bmad/`, `_bmad-output/`, `.agents/`, `.claude/` and `skills-lock.json` are symlinks into it and are gitignored here.

| What changed | Commit in |
|---|---|
| Package projects, tests, test site, `demo/`, README, LICENSE, Marketplace files, `assets/` | this repo |
| Anything under the symlinked folders | the planning repo |

A finished story is usually two commits, one in each repo. Never `git add -f` a symlinked path here.
