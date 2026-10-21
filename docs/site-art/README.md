# Site artwork

`sky.html` and `night.html` are the sources of `site/assets/sky.jpg` and `site/assets/night.jpg`. Both are drawn entirely in SVG: gradients plus noise-generated clouds and stars, with no photos or third-party artwork.

To regenerate one, open it in Safari at 1600×1000, take a 2× screenshot, then shrink and compress it:

```bash
sips --resampleWidth 2400 sky.png --out sky-2400.png
sips -s format jpeg -s formatOptions 78 sky-2400.png --out ../../site/assets/sky.jpg
```
