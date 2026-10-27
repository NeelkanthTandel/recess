# Search and AI discoverability

This page covers how the website (`site/`) is set up so that people looking for a break reminder can find Recess, on search engines and through AI assistants. It has three parts: what the research found, what the site does about it, and the steps only the owner can take.

Research was done in October 2026. Where a primary page couldn't be loaded directly from the research sandbox, the finding comes from a search restricted to that vendor's own domain. Those are marked *(search excerpt)*: re-read them before quoting them.

## What the research found

### Search engines

- **AI answers in Google need nothing extra.** AI Overviews and AI Mode use the normal Search index. Google says there are "no additional requirements" and no "special optimizations" beyond being indexed and eligible for a snippet. `nosnippet` and `noindex` apply to AI features too. *(search excerpt)* [AI features and your website](https://developers.google.com/search/docs/appearance/ai-features), [AI optimization guide](https://developers.google.com/search/docs/fundamentals/ai-optimization-guide)
- **Helpful, original content is what counts.** Google asks for people-first content with real first-hand knowledge. It treats pages mass-produced to catch query variations as scaled content abuse, whether a person or an AI wrote them. *(search excerpt)* [Creating helpful content](https://developers.google.com/search/docs/fundamentals/creating-helpful-content), [Spam policies](https://developers.google.com/search/docs/essentials/spam-policies)
- **Titles and descriptions.** Write a unique, descriptive title with the brand, and an accurate short description. Google may rewrite either one. Bing says titles and descriptions affect whether a page is used to ground Copilot answers. *(search excerpt)* [Title links](https://developers.google.com/search/docs/appearance/title-link), [Bing Webmaster Guidelines](https://www.bing.com/webmasters/help/webmaster-guidelines-30fba23a)
- **`SoftwareApplication` structured data.** Google's rich result needs `name`, `offers.price` and either `aggregateRating` or `review`. Ratings must come from real users and be shown on the page. Without ratings there is no rich result, but the markup is still valid and still describes the app to search engines. *(search excerpt)* [Software app structured data](https://developers.google.com/search/docs/appearance/structured-data/software-app), [Review snippets](https://developers.google.com/search/docs/appearance/structured-data/review-snippet)
  - A desktop app uses `SoftwareApplication`; `MobileApplication` and `WebApplication` are its subtypes. `operatingSystem` takes text such as "Windows 7, OS X 10.6". Read from the vocabulary source: [schema.org SoftwareApplication](https://schema.org/SoftwareApplication).
- **FAQ rich results are gone.** Since August 2023 Google showed them only for authoritative government and health sites. They stopped appearing on 7 May 2026, and the documentation was removed. [2023 announcement](https://developers.google.com/search/blog/2023/08/howto-faq-changes), [Search updates](https://developers.google.com/search/updates) *(search excerpt)*
- **Sitemaps.** Google ignores `priority` and `changefreq`. It uses `lastmod` only when it's consistently accurate. List the sitemap in robots.txt or submit it in Search Console; the old ping endpoint is gone. Bing says `lastmod` helps it decide when to recrawl. *(search excerpt)* [Build a sitemap](https://developers.google.com/search/docs/crawling-indexing/sitemaps/build-sitemap), [Bing on sitemaps and AI search](https://blogs.bing.com/webmaster/2025/7/Keeping-Content-Discoverable-with-Sitemaps-in-AI-Powered-Search/)
- **Robots.txt.** A crawler obeys only the most specific group that names it, and that group isn't merged with `*`. So every named group needs its own `Allow`. *(search excerpt)* [Google's robots.txt spec](https://developers.google.com/crawling/docs/robots-txt/robots-txt-spec), [RFC 9309](https://www.rfc-editor.org/rfc/rfc9309)
- **Social images.**
  - Open Graph requires `og:title`, `og:type`, `og:image` and `og:url`, and says an `og:image` should come with `og:image:alt` ([ogp.me](https://ogp.me/), read from source).
  - X's large card is 2:1, at least 300×157 and under 5 MB.
  - Google also uses `og:image` for Search and Discover thumbnails. It suggests images at least 1200 px wide. *(search excerpt)* [Image SEO](https://developers.google.com/search/docs/appearance/google-images)
- **Core Web Vitals.** "Good" means LCP ≤ 2.5 s, INP ≤ 200 ms and CLS ≤ 0.1, measured at the 75th percentile. Give the LCP resource high fetch priority, give images dimensions, and preload fonts with `crossorigin`. *(search excerpt)* [Web Vitals](https://web.dev/articles/vitals), [Fetch priority](https://web.dev/articles/fetch-priority), [Optimize CLS](https://web.dev/articles/optimize-cls)
- **Meta keywords** do nothing. Bing called the tag "dead in terms of SEO value", and stuffing it looks like spam. [Bing, 2014](https://blogs.bing.com/webmaster/2014/10/03/blame-the-meta-keyword-tag)
- **IndexNow** lets a site tell Bing, Yandex, Naver, Seznam and Yep that a URL changed. Google doesn't take part. It is meant to sit alongside a sitemap, not replace one. *(search excerpt)* [IndexNow FAQ](https://www.indexnow.org/faq)

### AI assistants and their crawlers

Each company runs separate bots: some build a search index, some fetch a page when a user asks, and some collect training data. Blocking the search bots is what removes a site from answers.

| Company | Search index | Fetches for a user | Training | Source |
| --- | --- | --- | --- | --- |
| OpenAI | `OAI-SearchBot` (blocked sites aren't shown in ChatGPT search answers) | `ChatGPT-User` (robots.txt "may not apply") | `GPTBot` | [OpenAI bots](https://developers.openai.com/api/docs/bots), [Publishers FAQ](https://help.openai.com/en/articles/12627856) *(search excerpt)* |
| Anthropic | `Claude-SearchBot` | `Claude-User` | `ClaudeBot` | [Anthropic's crawlers](https://support.claude.com/en/articles/8896518-does-anthropic-crawl-data-from-the-web-and-how-can-site-owners-block-the-crawler) (read in full; all three honour robots.txt) |
| Perplexity | `PerplexityBot` | `Perplexity-User` (generally ignores robots.txt) | none | [Perplexity crawlers](https://docs.perplexity.ai/docs/resources/perplexity-crawlers) *(search excerpt)* |
| Google | `Googlebot` (also covers AI Overviews and AI Mode) | none | `Google-Extended`: Gemini training and grounding only; "does not impact a site's inclusion in Google Search" | [Google crawlers](https://developers.google.com/crawling/docs/crawlers-fetchers/google-common-crawlers) *(search excerpt)* |
| Apple | `Applebot` (Spotlight, Siri, Safari) | none | `Applebot-Extended` (a usage control, not a crawler) | [About Applebot](https://support.apple.com/en-us/119829) *(search excerpt)* |
| Microsoft | `Bingbot` (Bing and Copilot share one index) | none | none | [Bing crawlers](https://www.bing.com/webmasters/help/which-crawlers-does-bing-use-8c184ec0), [Guidelines](https://www.bing.com/webmasters/help/webmaster-guidelines-30fba23a) *(search excerpt)* |
| DuckDuckGo | `DuckDuckBot`, `DuckAssistBot` | none | none | [DuckAssistBot](https://duckduckgo.com/duckduckgo-help-pages/results/duckassistbot) *(search excerpt)* |
| Amazon | `Amzn-SearchBot` | `Amzn-User` | `Amazonbot` | [Amazonbot](https://developer.amazon.com/amazonbot) *(search excerpt)* |
| Meta | none | `meta-externalfetcher` | `meta-externalagent` | [Meta crawlers](https://developers.facebook.com/documentation/sharing/webmasters/web-crawlers) *(search excerpt)* |
| Mistral | `MistralAI-Index` | `MistralAI-User` | `MistralAI-Training` | [Mistral robots](https://docs.mistral.ai/robots) *(search excerpt)* |
| Common Crawl | none | none | `CCBot` (an open dataset many models train on) | [CCBot](https://commoncrawl.org/ccbot) *(search excerpt)* |

- **Getting cited.**
  - OpenAI's advice to publishers is to allow `OAI-SearchBot`.
  - Bing's is to be indexed and to keep a sitemap. Bing Webmaster Tools now has an *AI Performance* report showing Copilot citations ([announcement](https://blogs.bing.com/webmaster/February-2026/Introducing-AI-Performance-in-Bing-Webmaster-Tools-Public-Preview)).
  - None of these companies promises placement.
  - No primary source says that ChatGPT uses Bing's index, so this page doesn't assume it.
- **llms.txt** ([spec source](https://github.com/AnswerDotAI/llms-txt/blob/main/nbs/index.qmd), v2) is a Markdown file at `/llms.txt`.
  - Format: an H1 name, a blockquote summary, optional notes, then H2 sections that list links. `## Optional` marks secondary links.
  - `llms-full.txt` is a community habit, not part of the spec.
  - Google says Search doesn't use llms.txt and it "won't negatively or positively impact your visibility".
  - No primary source shows it affects citations. It is useful to an agent that goes looking for it, and it costs very little.

## What the site does

| Change | Why |
| --- | --- |
| Title "Recess: a free break reminder for Mac and Windows", and a rewritten description | Unique, descriptive and branded. It names what people search for (free, break reminder, Mac, Windows) without stuffing keywords. |
| Hero sentence now says "free, open-source break reminder" | The words people search for are now in the visible text, not only in the metadata. The headline and design are unchanged. |
| Full Open Graph set plus `twitter:card`, and a new 1200×630 image `site/assets/og.jpg` | Rendered from the site's own sky SVG (`docs/site-art/og.html`), with alt text. The old `og:image` was the 2400×1500 page background. |
| JSON-LD `SoftwareApplication` (one entity, both systems), `WebSite` and `Person` | The schema.org vocabulary helps search engines and assistants understand the page: free (`price` 0), MIT, version 1.1.0, three download URLs, category, features and author. It has no rating, because there are no real on-page reviews, so it won't get a rich result. That's fine. |
| A visible **Questions, answered** section with ten questions | These answer what people ask search engines and chatbots: is it free, Mac and Windows, the 20-20-20 rule, Pomodoro, stretching and posture, video calls, more than one screen, the security prompt, how it differs, who makes it. The answers are short and factual and match the app. The 20-20-20 answer links to the American Academy of Ophthalmology and says it isn't medical advice. |
| **No** `FAQPage` JSON-LD | Google retired the rich result in May 2026, and a second copy of every answer would drift out of date. The visible questions are plain `h3` and `p` elements, which any crawler can read. |
| `site/robots.txt` | Allows everyone. It names the search, user-fetch and training bots above, each group with its own `Allow: /`, so a future `Disallow` under `*` can't shut them out by accident. Training bots are allowed on purpose, so assistants know Recess exists. Remove that group to opt out; search visibility doesn't depend on it. |
| `site/sitemap.xml` | One URL with an accurate `lastmod`, listed in robots.txt. |
| `site/llms.txt` | Follows the spec: summary, key facts, the same answers as the page, download links, and the README as raw Markdown. There's no `llms-full.txt`: the README already is the full text, and a third copy would go stale. |
| Fonts converted from TTF to WOFF2 (184 KB to 76 KB), with matching preloads | Faster first paint and less layout shift from late fonts. Same files, same OFL licence. |
| Header icon now uses a 64 px copy (3.7 KB instead of 63 KB); `apple-touch-icon` added | The 512 px icon was being shrunk to 32 px. |
| `sky.jpg` preloaded (normal priority) | It's the hero background, which a CSS image would otherwise find late. The largest paint is the headline text, measured locally at about 0.15 s with no layout shift, so the fonts keep priority. WebP was tried and dropped: at any reasonable size it smudged the faint stars in the night art. |

**Deliberately not done:**

- **Extra landing pages**, such as separate Mac and Windows pages or a "best free break reminder" list. They would mostly repeat this page. Google's guidance treats pages made to catch query variations as low value, and a self-ranked "best of" list wouldn't be honest. One good page with real answers is better. A page is worth adding later if it has something new to say, such as a proper guide to setting up breaks for eye strain.
- **Naming other apps.** The "how is it different" answer talks about what Recess does, not about anyone else's product.
- **Meta keywords:** no effect.
- **IndexNow:** for a single page that changes a few times a year, submitting the URL in Bing Webmaster Tools does the same job without a key file or a deploy step.

### Keeping it up to date

When you cut a release, update the version in two places: `softwareVersion` and `datePublished` in the JSON-LD in `site/index.html`, and "Current version" in `site/llms.txt`. Also update `lastmod` in `site/sitemap.xml`. When an FAQ answer changes, change it in `llms.txt` too. [RELEASING.md](RELEASING.md) has the checklist.

## Steps only the owner can do

### 1. Google Search Console

The site is a subdomain served from GitHub Pages, so `recess` in Hostinger's DNS is almost certainly a **CNAME** record pointing at `neelkanthtandel.github.io`. A name with a CNAME can't also have a TXT record. Verify the parent domain instead; it covers every subdomain.

1. Go to <https://search.google.com/search-console>, choose **Add property**, then **Domain**, and enter `always17.com`.
2. Copy the TXT value Google shows. It looks like `google-site-verification=…`.
3. In Hostinger hPanel, open **Domains**, then `always17.com`, then **DNS / Nameservers**, then **DNS records**. Add a record:
   - **Type** TXT
   - **Name** `@`
   - **TXT value** the string you copied
   - **TTL** the default
4. Save, wait a few minutes (it can take up to a day), then click **Verify** in Search Console. Leave the TXT record in place for good.
5. Open the property and go to **Sitemaps**. Enter `https://recess.always17.com/sitemap.xml` and click **Submit**.
6. In **URL Inspection**, enter `https://recess.always17.com/` and click **Request indexing**. Do this once, after this PR is merged and deployed. Asking again doesn't help.
7. Optional: check **Settings › Search generative AI** and leave AI Overviews and AI Mode *on*.

If you'd rather not verify all of always17.com, add a **URL prefix** property for `https://recess.always17.com/` and choose **HTML tag**. Put the `<meta name="google-site-verification" …>` line into `<head>` in `site/index.html` with a small PR, then click Verify once it's deployed.

### 2. Bing Webmaster Tools (also feeds Copilot and DuckDuckGo)

1. Go to <https://www.bing.com/webmasters> and sign in.
2. Choose **Import from Google Search Console**. It verifies the site and copies the sitemap. Do this after step 1.
3. If you don't want to import, add `https://recess.always17.com/` by hand and verify it with the **CNAME** option. Bing gives you its own host name, so it doesn't clash with the `recess` CNAME. You could also use the meta tag option.
4. Under **Sitemaps**, make sure `https://recess.always17.com/sitemap.xml` is listed. Under **URL Submission**, submit the home page.
5. Later, check **AI Performance** to see when Copilot cites the site.

### 3. GitHub repository description and topics

The repository has no description yet. GitHub search, the social preview and AI tools that read GitHub all use it. Run this once from any machine signed in with `gh`:

```bash
gh repo edit NeelkanthTandel/recess \
  --description "Free, open-source break reminder for Mac and Windows. Dims your screen for eye, stretch and get-up breaks (20-20-20, micro-breaks). No account, no tracking." \
  --homepage "https://recess.always17.com/" \
  --add-topic break-reminder,eye-strain,20-20-20-rule,pomodoro,stretch-reminder,posture,rsi,productivity,wellbeing,macos,windows,menubar,swift,swiftui,csharp,wpf,dotnet,open-source
```

Also upload `site/assets/og.jpg` under **Settings › General › Social preview**, so links to the repository show the same image.

### 4. Places to list Recess

Post these yourself, one at a time, and say plainly that you made Recess. Check each community's self-promotion rules first. The drafts below are starting points; put them in your own voice.

**Product Hunt** (<https://www.producthunt.com/posts/new>)

- Name: Recess
- Tagline (60 characters max): `Free, open-source break reminder for Mac and Windows`
- Topics: Productivity, Health & Fitness, Mac, Windows, Open Source
- Description:

  > Recess dims your screen when it's time to rest your eyes, stretch or get up, and steps back the moment you return. Short micro-breaks (set them to 20-20-20 if you like) and a longer break each hour, with rules that wait for calls, fullscreen apps and calendar events. Time away from the keyboard counts as a break. Free, MIT licensed, no account, no tracking.

- First comment:

  > I made Recess because I kept ending the day with sore eyes and a stiff back. It's free and open source, and it works the same on a Mac and a Windows PC. It isn't notarized or code-signed yet, so the first launch asks you to confirm. Each release lists SHA-256 checksums, and every line of code is on GitHub. I'd love to hear what would make it fit your day better.

- Gallery: `site/assets/og.jpg`, plus real screenshots of a break screen on each system.

**Hacker News: Show HN** (<https://news.ycombinator.com/submit>)

- Title: `Show HN: Recess – a free, open-source break reminder for Mac and Windows`
- URL: `https://github.com/NeelkanthTandel/recess` (HN readers like the source link; the README links the site)
- Text (optional; HN prefers the link on its own, so post this as your first comment instead):

  > Recess dims the screen for short eye breaks and a longer get-up break each hour. Breaks can wait for calls or fullscreen apps, can wait until you stop typing, and can be as strict as you like (limits on skips, an optional break lock). Time away from the computer counts as a break. The Mac app is Swift/SwiftUI and the Windows app is C#/WPF; they share the same settings model. No network requests, no analytics. Happy to answer questions about either side.

**AlternativeTo** (<https://alternativeto.net>, choose *Add application*)

- Platforms: Mac, Windows. License: Free, Open Source. Tags: break-reminder, eye-strain, pomodoro, productivity, health, rsi-prevention.
- Description:

  > Recess is a free, open-source break reminder for Mac and Windows. It dims the screen for short eye breaks and longer stretch-and-move breaks, waits for calls, fullscreen apps and calendar events, and counts time away from the keyboard as rest. Gentle or strict: cap postpones and skips, or lock the computer into the break. No account, no tracking.

- After it's approved, mark it as an alternative to the break-reminder apps listed there that it really overlaps with.

**Reddit.** Post as yourself, say you're the developer, and use each subreddit's self-promotion thread or flair where one exists.

- r/macapps (uses a "Free" flair; check the rules on developer posts)
- r/windowsapps and r/software
- r/opensource
- r/productivity (strict about self-promotion; often only the weekly thread is allowed)
- Draft:

  > **Recess – a free, open-source break reminder for Mac and Windows.** I built it to stop ending the day with tired eyes. It dims the screen for a few seconds every so often (20-20-20 if you like), plus a longer break each hour to get up. It waits during calls and fullscreen apps, and counts time away from the computer as a break. No account, no tracking, MIT licensed: https://recess.always17.com/

**Awesome lists.** Open a PR from your own fork, match each list's existing line format and section, and read its CONTRIBUTING first.

- [jaywcjlove/awesome-mac](https://github.com/jaywcjlove/awesome-mac): a productivity or health section, with its open-source and freeware badges
- [serhii-londar/open-source-mac-os-apps](https://github.com/serhii-londar/open-source-mac-os-apps): Health or Productivity, language Swift
- [Awesome-Windows/Awesome](https://github.com/Awesome-Windows/Awesome): Productivity or Utilities
- Entry text: `Recess - Free, open-source break reminder that dims the screen for eye, stretch and get-up breaks.`

**Package managers**

- **Homebrew.** The main `homebrew/cask` tap hasn't accepted apps that fail Gatekeeper since 1 September 2026 ([Homebrew discussion](https://github.com/orgs/Homebrew/discussions/6482)), so Recess can't go there until it's notarized. Until then, publish your own tap. Create a public repository `NeelkanthTandel/homebrew-recess` containing `Casks/recess.rb`:

  ```ruby
  cask "recess" do
    version "1.1.0"
    sha256 "4db808c7462b0d16819357cb2bc922d02853c3f77b1cb0badf88fcc04a379773"

    url "https://github.com/NeelkanthTandel/recess/releases/download/v#{version}/Recess.dmg"
    name "Recess"
    desc "Break reminder that dims the screen for eye, stretch and get-up breaks"
    homepage "https://recess.always17.com/"

    depends_on macos: ">= :sonoma"

    app "Recess.app"

    zap trash: "~/Library/Application Support/Recess"
  end
  ```

  Then people can install it with `brew install --cask neelkanthtandel/recess/recess`. Check the `zap` path against where the app really keeps its files, and update `version` and `sha256` with each release. Once releases are notarized, submit to `homebrew/cask`.

- **winget.** On Windows, run `winget install wingetcreate`, then:

  ```
  wingetcreate new https://github.com/NeelkanthTandel/recess/releases/download/v1.1.0/Recess-Setup.exe
  ```

  It reads the Inno Setup installer and asks for the details: publisher Neelkanth Tandel, package `NeelkanthTandel.Recess`, license MIT, scope user. Let it open the PR to `microsoft/winget-pkgs`. The installer is unsigned, so expect the automated checks or a moderator to take longer.

- **Chocolatey.** Make a package with `choco new recess`. Point it at `Recess-Setup.exe` with its SHA-256 checksum and silent install arguments `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`. Push it to the community repository, where it goes through moderation.
- **Scoop.** The portable `Recess-windows.zip` fits the `extras` bucket well. Add a manifest pointing at the zip with its checksum, and `"bin"`/`"shortcuts"` set to `Recess.exe`.

### 5. After merging

Once the Site workflow has deployed, check:

- <https://recess.always17.com/robots.txt>, `/sitemap.xml` and `/llms.txt` load.
- Google's Rich Results Test (<https://search.google.com/test/rich-results>) reads the `SoftwareApplication` with no errors. It will say the item isn't eligible for a rich result because there are no ratings; that's expected.
- A link preview shows the new image, for example by pasting the URL into a Slack or Discord message to yourself.
