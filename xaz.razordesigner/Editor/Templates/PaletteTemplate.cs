using System.Collections.Generic;
using Grains.RazorDesigner.Document;

namespace Grains.RazorDesigner.Templates;

// Identity is FilePath (case-insensitive). Name is the display string and filename stem.
// IconName passes straight through to engine IconControlWidget at edit time and to
// PaletteTemplateButton.OnPaint at render time.
// WrappedInContainer is provenance only — drop logic ignores it (the saved Roots
// already contain the wrapper if the user ticked the box at save time).
// IsReadOnly is runtime-only — set by PaletteTemplateStore.Scan based on whether the
// template was loaded from the bundled <addon-root>/Assets/Templates/Included/ dir.
// Not serialized to JSON; Save/Delete refuse on read-only templates.
public sealed record PaletteTemplate(
	string Name,
	string IconName,
	bool WrappedInContainer,
	IReadOnlyList<ControlRecord> Roots,
	string FilePath,
	bool IsReadOnly = false );
