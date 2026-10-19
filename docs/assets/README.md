# Documentation assets

The PasteDock icon and adaptive wordmark use two overlapping slips of paper landing in one dock. The violet mark is shared by the Windows executable, tray and workspace. `scripts/generate-brand.ps1` regenerates the multi-resolution ICO and PNG from drawing primitives; no network access is involved.

The `badge-*.svg` files were rendered with the owner's **IamAngusU/Badges** tool at commit `3ce6d01e32c7422e74755a37f86ba9478e47aafe`, using the local `badge-catalog.json` definition and the metadata layout. English/German switches are generated neutral language presets from the same tool. Generated SVGs are committed so no private tooling is required to read the README or build PasteDock. The private generator source is not redistributed.

`icon-marquee.svg` is a self-contained SVG exported with [Icon Marquee](https://github.com/IamAngusU/icon-marquee). Its pinned lineup is C#, .NET, Windows, PowerShell, GitHub and Git. The initial source was the hosted SVG generator, with other detected languages excluded. It requires no live GitHub-language API or service at display time. Embedded stack icons retain their respective owners' trademarks; see Icon Marquee's [attribution](https://github.com/IamAngusU/icon-marquee/blob/main/ATTRIBUTION.md). Animation respects reduced motion.

The screenshots are real WPF renders produced by `--ui-smoke` on Windows x64 with synthetic data. They contain no user clipboard content. Native controls, labels and layout come from the application, rather than a visual mockup. The UI is English; the README is bilingual.

The test-count badge is a release snapshot. Its link points to dated executed evidence. The separate GitHub Actions badge reports current CI status; neither badge is an independent audit or certification.
