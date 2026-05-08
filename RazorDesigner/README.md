# Razor Designer

s&box editor dock for building `.razor` + `.razor.scss` UI layouts visually.
Open from **Editor → View → Razor Designer**.

`Type: tool`. Loads in the editor only, never ships with your game.

## Install

Drop `RazorDesigner/` into your s&box `addons/` folder.

## Per-frame perf

| State            | avg ms / frame | % of 60fps budget | gen0Δ / sec |
|------------------|----------------|-------------------|-------------|
| Idle             | 0.006-0.007    | ~0.04%            | 0-1         |
| 17 panels steady | 0.018-0.024    | ~0.13%            | 0-1         |
| Mutation spike   | 0.04-0.08      | ~0.3-0.5%         | 0-1         |
