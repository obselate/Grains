---
hide:
  - navigation
---

# Razor Designer

Razor Designer adds a dockable visual designer for `.razor` UI panels to the
s&box editor. It appears in the editor's **Editor** menu group as
**Razor Designer**.

[Get Started](getting-started/installation.md){ .md-button .md-button--primary }
[View on GitHub](https://github.com/obselate/Grains){ .md-button }

## Scope

- **Input:** drag from a palette of built-in controls or saved templates;
  edit the selected node in a property inspector.
- **Output:** a paired `.razor` and `.razor.scss` written into your project.
- **Round-trip:** saved files can be reopened in the designer.

## Panels

The designer window hosts four panels, separated by splitters whose positions
are remembered between sessions:

| Panel | Purpose |
|---|---|
| Palette | Drag source for built-in controls and saved templates. |
| Hierarchy | Tree view of the document; supports reparent and reorder. |
| Canvas | Live preview rendered with real `Sandbox.UI` panels and a selectable theme. |
| Inspector | Edit properties of the selected node, grouped into collapsible sections. |

## Built-in controls

| Control | Razor tag | Category | Can hold children |
|---|---|---|---|
| Panel | `div` | Layout | yes |
| Label | `label` | Display | no |
| Image | `image` | Display | no |
| IconPanel | `i` | Display | no |
| Button | `button` | Input | no |
| ButtonGroup | `buttongroup` | Input | yes |
| Checkbox | `checkbox` | Input | no |
| TextEntry | `textentry` | Input | no |
| DropDown | `dropdown` | Input | no |
| Form | `form` | Form | yes |
| Field | `field` | Form | yes |
| FieldControl | `control` | Form | yes |

## Bundled assets

| Asset | Where it shows up |
|---|---|
| `ExampleCard` template | In the palette, alongside built-in controls. |
| `github-dark` theme | Selectable on the canvas (default). |
| `gruvbox-dark` theme | Selectable on the canvas. |
| `solarized-dark` theme | Selectable on the canvas. |

## Output format

Each save writes two files. The `.razor` carries a schema-version comment and
inherits `Panel`; children use the tag from the table above:

```razor
@* Grains.RazorDesigner schema=2 *@
@inherits Panel

<root class="root">
    <div class="card">
        <label class="title">Title</label>
        <button class="action">Click me</button>
    </div>
</root>
```

The `.scss` records which theme the document was designed against:

```scss
// designed against theme: github-dark

.card {
    flex-direction: column;
    gap: 8px;
    padding: 12px;
}

.title {
    font-size: 16px;
    font-weight: 600;
}

.action {
    align-self: flex-start;
}
```

See [Saved Files](reference/saved-files.md) for the full output specification.

## Persistent settings

The designer remembers the following between sessions:

- Splitter positions (outer split, and the left-column split).
- The last-selected preview theme.
- Canvas viewport size.
- Whether the canvas chrome is hidden.

## Where next

- [Installation](getting-started/installation.md)
- [Your First Panel](getting-started/first-panel.md)
- [Guide](guide/index.md)
- [Reference](reference/index.md)
- [Troubleshooting](troubleshooting.md)
