# Site artwork

`sky.html` and `night.html` are the sources of `site/assets/sky.jpg` and `site/assets/night.jpg`, and `og.html` is the source of the 1200×630 link-preview image `site/assets/og.jpg`. Both are drawn entirely in SVG: gradients plus noise-generated clouds and stars, with no photos or third-party artwork.

To regenerate one, open it in Safari at 1600×1000, take a 2× screenshot, then shrink and compress it:

```bash
sips --resampleWidth 2400 sky.png --out sky-2400.png
sips -s format jpeg -s formatOptions 78 sky-2400.png --out ../../site/assets/sky.jpg
```

`og.html` uses the same sky with the app icon and the site's fonts. Open it in a browser at exactly 1200×630 (for example with the responsive design mode), take a 1× screenshot of the page, and save it as a JPEG at about 84% quality:

```bash
sips -s format jpeg -s formatOptions 84 og.png --out ../../site/assets/og.jpg
```
