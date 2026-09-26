# F1 design tokens

`DesignSystem/F1.Palette.xaml`: opaque dark navy surfaces, subtle blue borders,
near-white/blue-grey type, cyan CPU/actions, purple Memory and teal system-volume.
Default (Dark), Light and HighContrast dictionaries select semantic colors.
`DesignSystem/F1.Controls.xaml`: 16px cards, 10–14px controls, 24px card padding,
32px content edge, consistent type/input/list/dialog treatments. Native control
templates retain focus, disabled, selection and automation semantics.

Hero 44px at reference width, page titles 32px, gauge values 34px, navigation 16px,
body 14–16px, caption 12–13px. Windows-installed Segoe UI Variable, with normal
system Arabic fallback; no redistributed font. No continuously animated decoration,
blur graph, per-card timer or fabricated chart data. High contrast uses system colors
and suppresses decoration. Existing System/Light/Dark preferences are respected.

The local mountain illustration is static. Reduced-transparency mode keeps it and
the content surfaces opaque, rather than compositing desktop content. There is no
Mica/Acrylic dependency or continuously animated effect. The gauge owns separate
reusable native geometries for its arc and glow; history draws use at most 60 points.
Localized version labels use a separate left-to-right text element for the technical
assembly value, with explicit reading order.
