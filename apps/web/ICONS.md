# Website icons

The original editable artwork is `app/icon.svg`: the header's four-point star,
with an open diamond center and two celestial dots. It uses the existing charcoal
(`#101114`), gold (`#d5ad61`), and ivory (`#f4efe7`) palette. A gold outline keeps
the silhouette visible against dark browser tabs. No external artwork or fonts
are used.

Next.js file-based metadata at the app root supplies the icons across all root
layouts, including Thai, English, redirect, and admin pages:

- `/icon.svg`: scalable browser icon and editable 512px source.
- `/favicon.ico`: embedded 16, 32, and 48px PNG images for browser compatibility.
- `/apple-icon.png`: opaque 180px Apple touch icon.
- `/favicon-16x16.png`, `/favicon-32x32.png`, `/favicon-48x48.png`: standalone exports.

Regenerate committed raster assets with `node scripts/build-icons.mjs` from
`apps/web`. This uses Sharp, installed with Next.js. No generation is required
at deployment. No previous favicon assets or default metadata references existed.

Validation: production build (including TypeScript and existing asset/content
checks), eight Playwright favicon/locale checks, and visual inspection of actual
16/32/48px exports on white and dark gray backgrounds. Run the browser checks with
`npx playwright test tests/favicon.spec.ts tests/locale-routing.spec.ts`.
The favicon tests check rendered metadata and successful image responses on
`/th`, `/th/`, `/en`, and `/en/`. Live deployment remains outside this change.
