namespace Grains.RazorDesigner.Document;

// 12 [Library]-confirmed enums. Pseudo-controls (Layout/Grid/Progress/Toast) and the
// untested-class-name-match three (SwitchControl/NumberEntry/SliderControl) live in
// user-authored palette templates instead — see docs/plans/2026-05-08-palette-templates-design.md.
public enum ControlType
{
	// Layout
	Panel,

	// Display
	Label,
	Image,
	IconPanel,

	// Input
	Button,
	ButtonGroup,
	Checkbox,
	TextEntry,
	DropDown,

	// Form
	Form,
	Field,
	FieldControl,
}

public enum ControlCategory
{
	Layout,
	Display,
	Input,
	Form,
}

// IconName (the metadata field) is the inspector chip icon — separate from Tag: razor tag
// names (textentry, div) aren't registered icons. DefaultIcon is the IconPanel glyph default
// (only meaningful for IconPanel; ignored for everything else). DefaultDirection /
// DefaultWrap only meaningful for containers; serializer skips them otherwise.
public sealed record ControlMeta(
	string Tag,
	string IconName,
	Length DefaultWidth,
	Length DefaultHeight,
	string DefaultContent,
	string DefaultIcon,
	string ExtraStyle,
	float DefaultFlexGrow,
	FlexDirection DefaultDirection,
	FlexWrap DefaultWrap,
	bool IsContainer,
	ControlCategory Category );

public static class ControlMetadata
{
	// Defaults aim to minimize inspector tweaks after dropping a control. Auto sizing where
	// content drives the box; explicit px for leaves whose content is meaningless without it
	// (Image, TextEntry). FlexGrow=0 is the engine default — only FieldControl opts in (it's
	// the input slot in a Field row and eating remaining space is the whole point). Wrap=NoWrap
	// across the board — engine default; matches every menu-addon container. Form defaults to
	// Column (vertical field stacking is the dominant real-world usage).
	public static ControlMeta Get( ControlType type ) => type switch
	{
		// Layout
		ControlType.Panel         => new( "div",          "crop_square",       Length.Auto,         Length.Auto,    "",          "",     "", 0f, FlexDirection.Row,    FlexWrap.NoWrap, IsContainer: true,  Category: ControlCategory.Layout ),

		// Display
		ControlType.Label         => new( "label",        "text_fields",       Length.Auto,         Length.Auto,    "Label",     "",     "", 0f, FlexDirection.Row,    FlexWrap.NoWrap, IsContainer: false, Category: ControlCategory.Display ),
		ControlType.Image         => new( "image",        "image",             Length.Auto,         Length.Auto,    "",          "",     "", 0f, FlexDirection.Row,    FlexWrap.NoWrap, IsContainer: false, Category: ControlCategory.Display ),
		// IconPanel glyph lives in DefaultIcon (engine icon picker via [IconName]); DefaultContent stays empty.
		ControlType.IconPanel     => new( "i",            "star",              Length.Auto,         Length.Auto,    "",          "help", "", 0f, FlexDirection.Row,    FlexWrap.NoWrap, IsContainer: false, Category: ControlCategory.Display ),

		// Input
		ControlType.Button        => new( "button",       "smart_button",      Length.Auto,         Length.Auto,    "Button",    "",     "", 0f, FlexDirection.Row,    FlexWrap.NoWrap, IsContainer: false, Category: ControlCategory.Input ),
		ControlType.ButtonGroup   => new( "buttongroup",  "view_carousel",     Length.Auto,         Length.Px(32),  "",          "",     "", 0f, FlexDirection.Row,    FlexWrap.NoWrap, IsContainer: true,  Category: ControlCategory.Input ),
		ControlType.Checkbox      => new( "checkbox",     "check_box",         Length.Auto,         Length.Auto,    "Checkbox",  "",     "", 0f, FlexDirection.Row,    FlexWrap.NoWrap, IsContainer: false, Category: ControlCategory.Input ),
		ControlType.TextEntry     => new( "textentry",    "input",             Length.Px(200),      Length.Px(30),  "Enter text","",     "", 0f, FlexDirection.Row,    FlexWrap.NoWrap, IsContainer: false, Category: ControlCategory.Input ),
		ControlType.DropDown      => new( "dropdown",     "arrow_drop_down",   Length.Auto,         Length.Auto,    "",          "",     "", 0f, FlexDirection.Row,    FlexWrap.NoWrap, IsContainer: false, Category: ControlCategory.Input ),

		// Form
		ControlType.Form          => new( "form",         "list_alt",          Length.Auto,         Length.Auto,    "",          "",     "", 0f, FlexDirection.Column, FlexWrap.NoWrap, IsContainer: true,  Category: ControlCategory.Form ),
		ControlType.Field         => new( "field",        "label",             Length.Auto,         Length.Auto,    "",          "",     "", 0f, FlexDirection.Row,    FlexWrap.NoWrap, IsContainer: true,  Category: ControlCategory.Form ),
		ControlType.FieldControl  => new( "control",      "widgets",           Length.Auto,         Length.Auto,    "",          "",     "", 1f, FlexDirection.Row,    FlexWrap.NoWrap, IsContainer: true,  Category: ControlCategory.Form ),

		_ => throw new System.ArgumentOutOfRangeException( nameof(type), type, null ),
	};

	public static string ClassNamePrefix( ControlType type ) =>
		type.ToString().ToLowerInvariant();

	public static string CategoryDisplayName( ControlCategory cat ) => cat switch
	{
		ControlCategory.Layout   => "Layout",
		ControlCategory.Display  => "Display",
		ControlCategory.Input    => "Input",
		ControlCategory.Form     => "Form",
		_ => cat.ToString(),
	};
}

public enum FlexDirection
{
	Row,
	Column,
}

public enum JustifyContent
{
	Start,
	Center,
	End,
	SpaceBetween,
	SpaceAround,
}

public enum AlignItems
{
	Start,
	Center,
	End,
	Stretch,
}

public enum FlexWrap
{
	NoWrap,
	Wrap,
	WrapReverse,
}

// Curated cursor list — Sandbox.UI accepts any string at the engine layer (Styles.Set.cs:2702
// just stores the value), so we pick the common ones designers actually need. Names mirror
// CSS keywords; ToCss() on the enum lowercases + dashes them.
public enum CursorKind
{
	Auto,
	Default,
	Pointer,
	Text,
	Grab,
	Grabbing,
	Wait,
	Crosshair,
	Move,
	NotAllowed,
	None,
}

// Distinct from Sandbox.UI.OverflowMode (engine type) — matches the engine's accepted set
// from BaseStyles.cs SetOverflow (visible / hidden / scroll / clip / clip-whole). No auto.
public enum OverflowKind
{
	Visible,
	Hidden,
	Scroll,
	Clip,
	ClipWhole,
}

// Distinct from Sandbox.UI.TextAlign — engine SetTextAlign (Styles.Set.cs:1079-1095) accepts
// left / center / right only. No justify.
public enum TextAlignment
{
	Left,
	Center,
	Right,
}
