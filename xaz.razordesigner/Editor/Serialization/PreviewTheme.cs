using System.IO;
using Sandbox;

namespace Grains.RazorDesigner.Serialization;

// Preview-only theme. The default mimics what consuming addons (menu, individual games)
// would set up for Sandbox.UI controls — addons/base ships only structural scss.
//
// Pipeline: SelectionRule + Theme.Css + PreviewMarkerRules + DocumentRules.
// Saved .razor.scss never contains theme rules — saved output is only the user's edits.
public sealed class PreviewTheme
{
	private const string LogPrefix = "[Grains.RazorDesigner]";

	public string Name { get; }
	public string Source { get; }   // "default" or absolute file path
	public string Css { get; }

	private PreviewTheme( string name, string source, string css )
	{
		Name = name;
		Source = source;
		Css = css;
	}

	public bool IsDefault => Source == "default";

	public static PreviewTheme Default { get; } = new( "Default", "default", DefaultCss );

	// On read failure returns Default and logs. Caller handles missing-file gracefully on cookie restore.
	public static PreviewTheme FromFile( string path )
	{
		try
		{
			var css = File.ReadAllText( path );
			Log.Info( $"{LogPrefix} PreviewTheme.FromFile: {path} ({css.Length} chars)" );
			return new PreviewTheme( Path.GetFileName( path ), path, css );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"{LogPrefix} PreviewTheme.FromFile failed for '{path}': {ex.Message}; falling back to Default" );
			return Default;
		}
	}

	// Palette + tokens grounded in sbox-public/menu/Code/Styles/_theme.scss:
	//   surfaces (default-XXX scale): #11141D / #191D2A / #283043 / #37425D / #465477 / #687AA6 / #C2C9DB
	//   text: #fafaff (primary) / #C2C9DB (light) / #687AA6 (muted)
	//   borders: rgba(194, 201, 219, 0.1) — blue-tinted alpha that picks up surface tint
	//   rounding scale: 2 / 4 / 6 / 8 px (xsmall / small / default / large)
	//   accent: #3FA9F5 (matches the SelectionRule outline; defer #3273eb alignment)
	//
	// Pipeline: SelectionRule + Theme.Css + PreviewMarkerRules + DocumentRules. Stand-ins
	// must live HERE (not PreviewMarkerRules) so a user-loaded .scss theme can replace the
	// look entirely. .preview-{type} rules layer on top of .preview-panel; equal class
	// specificity, source-order wins.
	private const string DefaultCss =
		".root { color: #fafaff; font-family: Inter; }\n" +
		// No global `label { color }` rule. Sandbox.UI.Button/TextEntry/Checkbox each
		// render their text in a child <label>; an opaque type-selector color on label
		// would block inheritance from per-record overrides like `.button1 { color: red }`.
		// Each typed parent (.root/.button/.textentry/.checkbox) sets color explicitly,
		// so inner labels inherit the right tint without the global rule.
		// Button — menu/_button.scss canonical: 8px radius, vertical gradient, drop shadow.
		".button { " +
			"background-image: linear-gradient(to bottom, #37425D 0%, #283043 100%); " +
			"border: 1px solid rgba(194, 201, 219, 0.1); " +
			"border-radius: 8px; " +
			"padding: 6px 16px; " +
			"color: #fafaff; " +
			"align-items: center; " +
			"justify-content: center; " +
			"min-height: 28px; " +
			"font-weight: 500; " +
			// sbox CSS box-shadow: single layer only, 'inset' is a trailing keyword.
			"box-shadow: 0 1px 2px rgba(0, 0, 0, 0.4); " +
		"}\n" +
		".button:hover { " +
			"background-image: linear-gradient(to bottom, #465477 0%, #37425D 100%); " +
			"border-color: rgba(63, 169, 245, 1); " +
			"box-shadow: 0 2px 4px rgba(0, 0, 0, 0.5); " +
		"}\n" +
		// TextEntry (and NumberEntry which inherits the .textentry class) — recessed dark.
		".textentry { " +
			"background-color: #11141D; " +
			"border: 1px solid rgba(194, 201, 219, 0.1); " +
			"border-radius: 4px; " +
			"padding: 4px 10px; " +
			"color: #fafaff; " +
			"min-height: 26px; " +
			"align-items: center; " +
			"box-shadow: 0 1px 2px rgba(0, 0, 0, 0.5) inset; " +
		"}\n" +
		// `.placeholder` lives on TextEntry's inner Label child, not on TextEntry itself
		// (TextEntry.cs:670 does Label.SetClass("placeholder", ...)). The descendant
		// selector with two-class specificity (0,2,0) wins against any user
		// `.textentry1 { color }` override (0,1,0) so the placeholder stays muted even
		// when the user sets a typography color override on the TextEntry itself.
		".textentry .placeholder { color: #687AA6; }\n" +
		// Checkbox — square checkmark + accent fill when checked.
		".checkbox { " +
			"flex-direction: row; " +
			"align-items: center; " +
			"color: #fafaff; " +
			"cursor: pointer; " +
		"}\n" +
		".checkbox > .checkmark { " +
			"width: 16px; " +
			"height: 16px; " +
			"border: 1px solid rgba(194, 201, 219, 0.2); " +
			"border-radius: 4px; " +
			"margin-right: 6px; " +
			"color: transparent; " +
			"align-items: center; " +
			"justify-content: center; " +
			"font-size: 12px; " +
			"background-color: #11141D; " +
			"box-shadow: 0 1px 1px rgba(0, 0, 0, 0.4) inset; " +
		"}\n" +
		".checkbox.is-checked > .checkmark { " +
			"color: white; " +
			"background-image: linear-gradient(to bottom, #5BB8F8 0%, #3FA9F5 100%); " +
			"border-color: #2E8FD9; " +
			"box-shadow: 0 1px 2px rgba(63, 169, 245, 0.25); " +
		"}\n" +
		// IconPanel — Material Icons font (the [Library('icon')] alias 'i' too).
		".iconpanel, i { " +
			"font-family: Material Icons; " +
			"font-size: 18px; " +
			"color: #fafaff; " +
			"align-items: center; " +
			"justify-content: center; " +
		"}\n" +
		// Container baseline. MirrorRecord catch-all applies .preview-panel to every stand-in;
		// type-specific rules below override on top.
		".preview-panel { " +
			"border: 1px solid rgba(194, 201, 219, 0.1); " +
			"background-color: rgba(40, 48, 67, 0.3); " +
			"min-height: 28px; " +
		"}\n" +
		// ButtonGroup — minimal container slot. Earlier segmented-gradient stand-in blurred
		// when stretched (percentage stops spread out at large widths) and violated invariant
		// #4 (containers should communicate via children + chrome label, not decorative chrome).
		// Container vs. leaf reads from the palette/hierarchy icon tint, not preview chrome.
		".preview-buttongroup { " +
			"background-color: rgba(40, 48, 67, 0.4); " +
			"border: 1px solid rgba(194, 201, 219, 0.1); " +
			"border-radius: 8px; " +
			"min-height: 32px; " +
		"}\n" +
		// DropDown — raised button surface + .inner caret zone (right-anchored).
		".preview-dropdown { " +
			"background-image: linear-gradient(to bottom, #37425D 0%, #283043 100%); " +
			"background-color: rgba(0, 0, 0, 0); " +
			"border: 1px solid rgba(194, 201, 219, 0.1); " +
			"border-radius: 8px; " +
			"color: #fafaff; " +
			"min-height: 32px; " +
			"position: relative; " +
			"box-shadow: 0 1px 2px rgba(0, 0, 0, 0.4); " +
		"}\n" +
		".preview-dropdown > .inner { " +
			"position: absolute; " +
			"top: 0; right: 0; bottom: 0; " +
			"width: 32px; " +
			"background-color: rgba(17, 20, 29, 0.4); " +
			"border-left: 1px solid rgba(194, 201, 219, 0.1); " +
			"align-items: center; " +
			"justify-content: center; " +
		"}\n" +
		// Form/Field/FieldControl target their REAL Sandbox.UI classes (Form.cs:12 AddClass("form"),
		// Field.cs:12 AddClass("field"), Field.cs:24 AddClass("field-control control")). Base
		// addon ships zero scss for them so the preview theme owns the visual baseline. A
		// user-loaded .scss theme can replace the look entirely. (grd-a57.)
		// Form — slightly raised slate panel with vertical gradient.
		".form { " +
			"background-image: linear-gradient(to bottom, #283043 0%, #1F2535 100%); " +
			"background-color: rgba(0, 0, 0, 0); " +
			"border: 1px solid rgba(194, 201, 219, 0.1); " +
			"border-radius: 6px; " +
			"min-height: 60px; " +
			"padding: 8px; " +
			"box-shadow: 0 1px 2px rgba(0, 0, 0, 0.3); " +
		"}\n" +
		// Field — honest form-row separator: thin bottom border, no decorative adornment
		// that would read as parasitic chrome on the parent Form surface.
		".field { " +
			"background-color: rgba(0, 0, 0, 0); " +
			"border: none; " +
			"border-bottom: 1px solid rgba(194, 201, 219, 0.06); " +
			"min-height: 28px; " +
			"padding: 4px 0; " +
		"}\n" +
		// FieldControl — recessed slot inside a Field row. Targets .field-control rather
		// than the more-generic .control class FieldControl also carries (collision risk).
		".field-control { " +
			"background-color: #191D2A; " +
			"border: 1px solid rgba(194, 201, 219, 0.08); " +
			"border-radius: 4px; " +
			"min-height: 28px; " +
			"flex-grow: 1; " +
			"box-shadow: 0 1px 2px rgba(0, 0, 0, 0.3) inset; " +
		"}\n";
}
