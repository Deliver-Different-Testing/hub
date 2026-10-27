# Hub ClientApp

React 19 + Vite 8 + TypeScript (strict) + Mantine 9 SPA/PWA for the Hub. Built into `../wwwroot` (ASP.NET serves it; unknown non-file paths fall back to `index.html`). Replaces the old Razor/Bootstrap pages.

## Commands (run from `ClientApp/`)

- `npm run dev` - Vite on :5173 (use `http://hub.local.deliverdifferent.com:5173` so the shared `.local.deliverdifferent.com` cookie works). Proxies `/api`, `/connect`, `/Account/AcceptTenantSwitchToken`, `/Account/CreditCard`, `/images`, `/health` to Kestrel (`HUB_BACKEND`, default `http://localhost:5000`).
- `npm run lint` / `npm test` / `npm run test:coverage` (Vitest + MSW + Testing Library; thresholds in `vite.config.ts`).
- `npm run build` - `tsc --noEmit` + `vite build` into `../wwwroot` (`emptyOutDir: false`, assets under `/static`, `sw.js` + `manifest.webmanifest` at the root, unhashed `consent.css`).
- `npm run gen:icons` - regenerates `public/icons/*` (192, 512, maskable) from `wwwroot/images/DFRNT_Mark.png`.

## Contract

All server calls go through `src/api/hub.ts` against `docs/spa-api-contract.md` (repo root). Change the contract doc and the backend together. Same-origin cookie auth; `GET api/antiforgery` runs once before the first mutation and `X-XSRF-TOKEN` is read from the `XSRF-TOKEN` cookie (`src/api/client.ts`). 401 on an authed route navigates to `/Account/Login?returnUrl=`. Dates are tenant wall-clock strings: display as-is, never convert zones (`src/lib/format.ts`).

## Theme rules

- Semantic tokens only: `var(--mantine-color-*)`, `--dd-*`, `theme.other.shell`. **No literal hex outside `src/styles`** (enforced by `src/test/noLiteralHex.test.ts`; the only other literals are in `vite.config.ts`, which imports them from `md3.ts`).
- `src/styles/{mantineTheme,md3,heroBackground}.ts`, `src/hooks/useColorMode.tsx` (key `hub-color-mode`) and `src/components/dialog/` are copies of the `dfrnt-brand-theming` skill assets. Update them from the skill, do not hand-edit.
- Brand comes from `GET api/me` `currentTenant.countryCode` (`brandForCountry`): non-US = `urgent` amber, US/unknown = `dfrnt` cyan. Anonymous routes (login/forgot/reset) are always cyan; authed routes render a neutral loader until api/me resolves.
- Plus Jakarta Sans static weights via `@fontsource` (400/500/600/700), never the variable font.
- Icons only through `components/Icon` (lucide for chrome, tabler for logistics; default 36px, stroke 1.25, smaller inline sizes allowed). Home tile icons are `<img>` from API paths.
- Dialogs use `DialogShell`/`DialogHeader`/`DialogFooter`, not a bare `Modal`.

## PWA

`vite-plugin-pwa`, `registerType: 'prompt'`; `UpdatePrompt` shows a Mantine notification with Reload. `/api` is `NetworkOnly`; navigation fallback never covers `/api`, `/connect`, `/health`, `/diagnostics`, `/Account/AcceptTenantSwitchToken`, `/Account/CreditCard`, `/images`. Only Vite output is precached (lazy `zxcvbn` chunk excluded). Tests alias `virtual:pwa-register/react` to `src/test/pwaRegisterStub.ts` (mock it with `vi.mock`). The plugin is off under Vitest.

## Conventions

- Pages in `src/pages`, tests colocated `*.test.tsx`; shared MSW fixtures in `src/test/handlers.ts`, render helper `src/test/utils.tsx`.
- Full-page navigations go through `src/lib/navigate.ts` so tests can mock them (jsdom cannot navigate).
- Write the failing test first; keep coverage above the thresholds.
