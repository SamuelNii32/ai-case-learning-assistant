Deployment notes
================

This repository includes a CI workflow that builds the app and publishes the
production `dist/` output to the `gh-pages` branch when a `release/*` branch
is pushed. The workflow file is: `.github/workflows/deploy.yml`.

How it works
------------
- Push to a release branch, for example `release/feat-save-work`.
- GitHub Actions will run `npm ci` and `npm run build`, then publish `./dist`
  to the `gh-pages` branch using the built-in `GITHUB_TOKEN`.
- After the workflow completes, enable GitHub Pages for the repository and
  choose the `gh-pages` branch (root) as the source in repository Settings → Pages.

Expected URL
------------
Once Pages is enabled, the site will typically be available at:

  https://<your-github-username>.github.io/<repository-name>/

For this repo that will usually be:

  https://SamuelNii32.github.io/ai-case-learning-assistant/

Notes & environment
-------------------
- This is a static site deployment. If your app depends on a runtime API
  (API_BASE) you will need to host the API separately and set any required
  environment variables or configure the frontend to point to the API URL.
- If you prefer Netlify/Vercel, connect the repository via their UI and use
  the same build commands (`npm ci` + `npm run build`) — typically simpler
  for preview environments.

Setting the production API URL (recommended)
-------------------------------------------
- Recommended: do NOT commit production URLs or secrets. Instead set the
  `VITE_API_BASE` environment variable in your hosting provider (Vercel/Netlify/GitHub
  Actions). This ensures the production build uses the correct API without
  storing sensitive values in the repo.

- Quick steps for Vercel:
  1. In the Vercel dashboard open your project → Settings → Environment Variables.
  2. Add `VITE_API_BASE` with the production API value for both "Production"
     and "Preview" environments.
  3. Redeploy the project.

- Alternatively you may keep a file locally named `.env.production.local` on
  the build machine (this file should be ignored by git). A safe template is
  provided as `.env.production.example` in the repo.

Rollbacks & cleanup
-------------------
- To remove a bad publish, delete the `gh-pages` branch and re-run the
  workflow or re-publish from a fixed commit.
