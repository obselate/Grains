namespace Grains.RazorDesigner.Document;

public enum ControlType
{
	Panel,
	Label,
	Button,
	Image,
	TextEntry,
	Layout,
}

// IconName is separate from Tag: razor tag names (textentry, div) aren't registered icons.
public sealed record ControlMeta(
	string Tag,
	string IconName,
	Length DefaultWidth,
	Length DefaultHeight,
	string DefaultContent,
	string ExtraStyle,
	float DefaultFlexGrow,
	bool IsContainer );

public static class ControlMetadata
{
	// Auto-sized except leaves needing explicit px (Image, TextEntry). Auto values are silent in saved scss.
	public static ControlMeta Get( ControlType type ) => type switch
	{
		ControlType.Panel     => new( "div",       "crop_square",  Length.Auto,    Length.Auto,    "",            "", 1f, IsContainer: true ),
		ControlType.Label     => new( "label",     "text_fields",  Length.Auto,    Length.Auto,    "Label",       "", 0f, IsContainer: false ),
		ControlType.Button    => new( "button",    "smart_button", Length.Auto,    Length.Auto,    "Button",      "", 0f, IsContainer: false ),
		ControlType.Image     => new( "image",     "image",        Length.Px(100), Length.Px(100), "",            "", 0f, IsContainer: false ),
		ControlType.TextEntry => new( "textentry", "input",        Length.Px(200), Length.Px(30),  "Enter text",  "", 0f, IsContainer: false ),
		ControlType.Layout    => new( "div",       "view_module",  Length.Auto,    Length.Auto,    "",            "", 1f, IsContainer: true ),
		_ => throw new System.ArgumentOutOfRangeException( nameof(type), type, null ),
	};

	public static string ClassNamePrefix( ControlType type ) =>
		type.ToString().ToLowerInvariant();
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
