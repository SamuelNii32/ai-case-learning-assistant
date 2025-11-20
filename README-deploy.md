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

Rollbacks & cleanup
-------------------
- To remove a bad publish, delete the `gh-pages` branch and re-run the
  workflow or re-publish from a fixed commit.
