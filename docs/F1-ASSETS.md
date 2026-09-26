# F1 asset register

**Clean background asset: MISSING.** Owner chose “continue with a disclosed temporary
background” during implementation. Visual Reference Gate stays **PENDING** for the
missing clean photographic mountain background; no percentage-match claim.

| Asset | Origin / ownership | Dimensions | Usage |
|---|---|---|---|
| `docs/design/reference/f1-approved-reference.png` | Owner-supplied flattened reference; rights not independently established | 1448×1086 | Documentation/comparison only, never included as runtime background |
| `Assets/F1/mark.svg` | Original vector geometry authored for this UI task; hexagonal technical mark, not Formula 1 logo | 72×72 | Sidebar/About; F1 remains selectable/live text |
| `Assets/F1/mountains-placeholder.svg` | Original procedural vector illustration authored for this UI task; explicitly temporary | 1600×1100 | Local static dark mountain background, UniformToFill center crop; no printed UI/data |

The temporary illustration is intentionally disclosed rather than pretending to be
the reference's photographic source. It contains only mountains/sky, no controls,
text or metrics. No network loading, external fonts, animated scenery or third-party
asset library. Opaque theme-aware surfaces protect text contrast and avoid desktop
transparency/blur. The static illustration remains opaque with system transparency
disabled (the actual test host setting). High contrast suppresses it. Light mode uses the same layout and
light semantic surfaces. User-supplied clean background can replace the temporary
asset in a subsequent targeted visual pass.
