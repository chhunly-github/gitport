# GitPort branding

The approved portable toolbox design is the active application identity. This folder contains the production artwork and exports.

- `gitport.svg`: editable vector master.
- `gitport-small.svg`: optical variant for 16–32 px, with heavier branch nodes and simplified detail.
- `gitport-mono-dark.svg` / `gitport-mono-light.svg`: single-ink variants for light/dark backgrounds.
- `icons/`: transparent PNGs from 16 to 1024 px.
- `GitPort.ico`: nine Windows icon resolutions from 16 to 256 px.
- `splashscreen.png`: 1280 × 720 splash artwork.
- `splashscreen-960x540.png`: application splash embedded in WPF and copied to Java resources.
- `splash-background.png`: matching background without the logo or text.

Regenerate active assets and Java resources from the repository root:

```powershell
java tools/AssetBuilder.java
```

The WPF project embeds the ICO and splash during compilation. Java selects the native icon resolution when available, preserving the small-size adjustments.
