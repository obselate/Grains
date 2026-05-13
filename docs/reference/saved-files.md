# Saved Files

Each save writes two files side by side: `<Name>.razor` and `<Name>.razor.scss`.
`<Name>` is taken from the filename you choose in the save dialog — without
the extension. The dialog defaults to `MyMenu.razor` and appends `.razor`
automatically if you leave it off.

The `.scss` path is the `.razor` path with `.scss` appended (i.e.
`MyMenu.razor.scss`, not `MyMenu.scss`) — this matches the s&box convention
for auto-loaded stylesheets.

## `.razor` structure

The first three lines are fixed:

```razor
@* Grains.RazorDesigner schema=2 *@
@inherits Panel

<root class="root">
```

Followed by the document tree, indented in four-space steps. Each control
emits a tag drawn from the table below.

### Tag mapping

| Control | Tag |
|---|---|
| Panel | `div` |
| Label | `label` |
| Image | `image` |
| IconPanel | `i` |
| Button | `button` |
| ButtonGroup | `buttongroup` |
| Checkbox | `checkbox` |
| TextEntry | `textentry` |
| DropDown | `dropdown` |
| Form | `form` |
| Field | `field` |
| FieldControl | `control` |

### Element shapes

Containers (Panel, ButtonGroup, Form, Field, FieldControl) self-close when
empty, and use open/close pairs otherwise:

```razor
<div class="empty-container" />

<div class="parent">
    <label class="title">Title</label>
</div>
```

Leaves emit per type:

| Control | Shape |
|---|---|
| Label | `<label class="...">CONTENT</label>` |
| Button | `<button class="...">CONTENT</button>` |
| IconPanel | `<i class="...">ICON_NAME</i>` |
| Image | `<image class="..." src="..." />` |
| TextEntry | `<textentry class="..." placeholder="..." />` |
| Checkbox, DropDown | `<tag class="..." />` |

The text in `CONTENT`, `ICON_NAME`, `src`, and `placeholder` is HTML-escaped:
`&` → `&amp;`, `<` → `&lt;`, `>` → `&gt;`, `"` → `&quot;`.

## `.razor.scss` structure

The first line is a comment recording which preview theme the document was
designed against:

```scss
// designed against theme: github-dark
```

The theme itself is **not** included in the saved SCSS — only the per-record
overrides you've set in the inspector. The note exists so you can match the
theme at runtime if you choose.

The rest of the file wraps all rules in a top-level block whose selector is
the file's `<Name>` (the SCSS class wrapper matches the file basename):

```scss
MyMenu {
    .card { /* ... */ }
    .title { /* ... */ }
}
```

`.root` lives flat alongside (not nested inside) the other rules, because
the canvas wrapper class is compound with `.root`, not a descendant.

### What gets emitted

Each property is only written when it differs from its engine default, or
when its inspector **Override** toggle is on. The matrix below summarises
the rules.

#### Always-conditional

| Property | Emit when |
|---|---|
| `width` | size unit is not `Auto` |
| `height` | size unit is not `Auto` |
| `flex-grow` | value is not `0` |
| `flex-shrink` | value is not `1` |
| `flex-basis` | unit is not `Auto` |

`flex-grow`, `flex-shrink`, `flex-basis`, and `margin` are also skipped on
the `.root` record (no parent flex container).

#### Container-only

These only emit on records that can hold children (Panel, ButtonGroup, Form,
Field, FieldControl):

| Property | Emit when |
|---|---|
| `flex-direction` | not `Row` |
| `justify-content` | not `Start` |
| `align-items` | not `Stretch` |
| `flex-wrap` | not `NoWrap` |
| `gap` | value > 0 **and** the container has ≥ 2 children |
| `padding` | unit is not `Auto` and value is not `0px` |

#### Toggle-gated groups

The inspector exposes `Override*` toggles. Each group only emits when its
toggle is on:

| Toggle | Properties emitted |
|---|---|
| `OverrideTypography` | `font-family` (skip if empty), `font-size` (skip if Auto), `font-weight`, `color`; plus `text-align` on Label only |
| `OverrideBackground` | `background-color`; then either `background-image: <user value>` or `background-image: none` |
| `OverrideBorder` | `border-radius` (skip if Auto), `border-width` (skip if Auto), `border-color` |
| `OverrideEffects` | `box-shadow: <x> <y> <blur> <color> [inset]`, `opacity` |
| `OverrideConstraints` | `margin` (skip on root, Auto, or 0px), `min-width`, `max-width`, `min-height`, `max-height` (each skips if Auto) |
| `OverrideInteraction` | `cursor`, `overflow` |

`background-image: none` is emitted when you override the background but
don't supply your own image — this wipes any inherited theme gradient at
runtime, not just in the preview.

#### Checkbox glyph sizing

When `CheckboxSize` is not `Auto`, a nested block is emitted alongside the
checkbox rule:

```scss
.my-checkbox {
    > .checkmark {
        width:  <size>;
        height: <size>;
        font-size: <0.75 × size>;
    }
}
```

The glyph font-size is always `0.75 × CheckboxSize`, preserving the bundled
theme's 16/12 ratio at any size.

### Enum-to-CSS keyword mapping

The inspector uses C# enum values; the serializer maps them to CSS keywords:

| Enum | CSS values |
|---|---|
| `FlexDirection` | `row`, `column` |
| `JustifyContent` | `flex-start`, `center`, `flex-end`, `space-between`, `space-around` |
| `AlignItems` | `flex-start`, `center`, `flex-end`, `stretch` |
| `FlexWrap` | `nowrap`, `wrap`, `wrap-reverse` |
| `TextAlignment` | `left`, `center`, `right` |
| `CursorKind` | `auto`, `default`, `pointer`, `text`, `grab`, `grabbing`, `wait`, `crosshair`, `move`, `not-allowed`, `none` |
| `OverflowKind` | `visible`, `hidden`, `scroll`, `clip`, `clip-whole` |

### Empty rules

A record with no overridden properties and no children produces no rule at
all — the saved SCSS doesn't carry empty selectors.

## Image auto-import on save

If a record's image `src` points outside the loaded library's `Assets/`
directory, the file is copied into `Assets/ImageImports/<filename>` on save
and the record's `src` is rewritten to `ImageImports/<filename>`. This
makes the path resolvable at runtime — `Image.SetTexture` goes through the
`Assets/`-rooted mounted filesystem. In-`Assets/` paths are left untouched.

## Round-trip

A file saved by the designer can be reopened in the designer. The schema
comment on line 1 (`schema=2`) identifies the file as a Razor Designer
document on load.

## Example

A small saved document. The `.razor`:

```razor
@* Grains.RazorDesigner schema=2 *@
@inherits Panel

<root class="root">
    <div class="card">
        <label class="title">Welcome</label>
        <button class="action">Continue</button>
    </div>
</root>
```

The matching `Welcome.razor.scss` after a card-padding override and an
opted-in typography pass on the title:

```scss
// designed against theme: github-dark

Welcome {
    .card {
        flex-direction: column;
        gap: 8px;
        padding: 12px;
    }

    .title {
        font-size: 16px;
        font-weight: 600;
        color: #e6edf3;
    }
}
```
