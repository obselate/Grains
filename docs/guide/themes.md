# Themes

Razor Designer ships with three preview themes that style the **canvas**
(not the editor UI itself). Pick one to preview your panel against a specific
aesthetic.

## Bundled themes

| Theme | Source | Description |
| --- | --- | --- |
| **github-dark** | [primer/primitives](https://primer.style/foundations/primitives) | Default. The palette this docs site uses. |
| **gruvbox-dark** | morhetz / Pearofducks canonical | Warm retro, high contrast. |
| **solarized-dark** | Ethan Schoonover canonical | Cool, low-glare blue/cyan. |

## github-dark tokens

The default theme — and the tokens powering this very site:

| Token | Value |
| --- | --- |
| `canvas-default` | `#0d1117` |
| `canvas-subtle` | `#161b22` |
| `canvas-inset` | `#010409` |
| `border-default` | `#30363d` |
| `border-muted` | `#21262d` |
| `fg-default` | `#e6edf3` |
| `fg-muted` | `#7d8590` |
| `accent-fg` | `#2f81f7` |
| `accent-emphasis` | `#1f6feb` |

## Adding your own theme

!!! note "Stub"
    Document the `.scss` file format under `Assets/Themes/` and how the designer
    picks them up at runtime.
