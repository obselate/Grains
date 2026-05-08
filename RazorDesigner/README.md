# Razor Designer

An Editor-facing dock for s&box that lets you build `.razor` UI layouts
visually — palette, hierarchy, live canvas, and inspector — and save the
result as a paired `.razor` + `.razor.scss` file you drop into your own
addon.

Open from **Editor → View → Razor Designer**.

## Install

s&box loads any subdirectory of `addons/` that contains a `.sbproj`. To
use Razor Designer:

```sh
# Option A — clone the whole Grains monorepo, then copy or symlink:
git clone https://github.com/obselate/Grains.git
cp -r Grains/RazorDesigner /path/to/your/s&box-install/addons/

# Option B — if you only want this one addon, sparse-checkout works too:
git clone --filter=blob:none --no-checkout https://github.com/obselate/Grains.git
cd Grains
git sparse-checkout set RazorDesigner
git checkout
```

The addon is `Type: tool`, so it loads in the editor process and never
ships with your game build.

## What it produces

Saved files follow the official menu addon's pairing convention:
`MyPanel.razor` (markup) + `MyPanel.razor.scss` (styles). Drop both into
any addon that builds UI with `Sandbox.UI` and reference `MyPanel` like
any hand-written panel.

The in-editor preview substitutes `Sandbox.UI.Label` for `Button` and
`TextEntry` (those types live in the `base` addon and don't load into
the editor process), with marker classes giving them a visually distinct
look. Saved output uses the real types — the substitution is preview-only.

## Per-frame perf

Yoga layout cost in the live preview is negligible across every doc
size measured. Methodology: 60-frame sample window wrapping the layout
+ descriptor build inside the canvas tick; `gen0Δ` is the
`GC.CollectionCount(0)` delta over that window. DPI scale 1.5,
preview canvas 320×192.

| State              | avg ms / frame | % of 60fps budget | gen0Δ / sec |
|--------------------|----------------|-------------------|-------------|
| Idle (empty doc)   | 0.006–0.007    | ~0.04%            | 0–1         |
| 17 panels steady   | 0.018–0.024    | ~0.13%            | 0–1         |
| Mutation spike     | 0.04–0.08      | ~0.3–0.5%         | 0–1         |

Mutation spikes (paste / move / delete) align with full-subtree mirror
rebuilds, not with Yoga layout. Surgical mirror updates are tracked but
deferred — at 17 panels the spike is well under one frame at 60fps.

## Known limitations

- `Type: tool` addons can't host their own paired `.razor` + `.razor.scss`
  files (markup parses, scss pairing doesn't apply). The designer never
  authors razor for its own chrome — every panel in the dock is built
  with `Editor.Widget` C# — so this is irrelevant for *using* the
  designer. It only matters if you fork and try to author the designer's
  own UI in razor; don't.
- The `.sbproj` `Org` field is set to `local`; change it to your own org
  ident before publishing anything that depends on this addon.

## License

MIT — see [`../LICENSE`](../LICENSE) at the monorepo root.
