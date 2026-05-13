---
hide:
  - navigation
---

# Razor Designer

## What it is

Razor Designer is an s&box editor library that adds a dockable visual
designer for `.razor` UI panels. You build a panel by dragging controls
onto a canvas, arranging them in a hierarchy, and editing their properties
in an inspector. The designer writes a paired `.razor` and `.razor.scss`
into your project.

## Why use it for s&box

- **Native to the editor.** The designer docks alongside your other editor
  tools — no separate app, no export step.
- **Real preview, not a mockup.** The canvas renders with actual
  `Sandbox.UI` panels and the same flexbox layout engine s&box uses at
  runtime. What you see in the designer is what you ship.
- **Plain output.** Saved files are normal `.razor` and `.razor.scss` you
  can read, diff, version, and edit by hand. There's no proprietary
  asset format and no build-time codegen.
- **Reopens what it saves.** Documents saved by the designer can be
  reopened in the designer, so handing off between visual and code
  workflows is round-trippable.
- **Reusable templates.** Any subtree can be saved as a palette template
  and reused across documents.

## Where next

- [Installation](getting-started/installation.md)
- [Your First Panel](getting-started/first-panel.md)
- [Guide](guide/index.md)
- [Reference](reference/index.md)
- [Troubleshooting](troubleshooting.md)
