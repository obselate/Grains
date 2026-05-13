# Troubleshooting

Common issues and how to fix them.

!!! note "Stub"
    Populate from real user reports — empty canvas, image-not-found, theme
    swapping not applying, etc.

## Designer window doesn't appear

Confirm the library is referenced in your `.sbproj` and the editor was fully
reloaded after adding it.

## Images don't load in the canvas

Razor Designer walks your loaded library's `Assets/` directory when resolving
image paths. Make sure your image lives somewhere under `Assets/` and the
reference is relative to that root.

## Got a bug?

Open an issue at
[github.com/obselate/Grains/issues](https://github.com/obselate/Grains/issues).
