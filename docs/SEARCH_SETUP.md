# Website publication and Google indexing

The public website source is `docs/index.html`. It contains an accessible interactive concept illustration, SEO metadata, a canonical URL, structured data and an XML sitemap. Installer links are resolved from published GitHub release assets at deployment time, including prereleases; GitHub's `/releases/latest` omits Alpha prereleases.

## One-time Pages setting

In **Settings → Pages → Build and deployment → Source**, select **GitHub Actions**. Then run **Actions → Publish DeskPages website → Run workflow**. The connected GitHub tools cannot change repository Pages administration settings. The workflow does not bypass this requirement.

Expected URL after a successful deployment: https://adamabutoamy.github.io/DeskPages/

The workflow updates the deployed download when a release is published or edited. If another workflow publishes releases using `GITHUB_TOKEN`, GitHub does not trigger a second workflow from that event; run this website workflow manually after those releases. Public-source edits to `docs/` also trigger it.

## Search Console

Add the URL-prefix property `https://adamabutoamy.github.io/DeskPages/` in Google Search Console. Verify using the supplied HTML verification file in `docs/` and redeploy (or add the exact Google verification meta tag). Submit `sitemap.xml` and inspect/request indexing for the homepage.

A project Pages site cannot control `/robots.txt` at the `adamabutoamy.github.io` root; a file under `/DeskPages/robots.txt` would not be the domain's robots file. No crawler block is added here. Sitemap submission and the homepage metadata are used instead.

Indexing and ranking are decided by Google and are not guaranteed by successful deployment.
