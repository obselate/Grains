using System.Collections.Generic;
using Sandbox;
using Sandbox.UI;

namespace Grains.RazorDesigner.Document;

public sealed class ControlRecord
{
	[Hide] public ControlType Type { get; init; }

	[Group( "Identity" )]
	[Title( "Name" )]
	[Description( "CSS class name for this control. Used in the saved .razor and .razor.scss." )]
	public string ClassName { get; set; }

	[Hide] public List<ControlRecord> Children { get; init; } = new();

	[Group( "Layout" )] [Title( "Width" )]   [Description( "How wide this control is." )]                public Length Width  { get; set; } = Length.Auto;
	[Group( "Layout" )] [Title( "Height" )]  [Description( "How tall this control is." )]                public Length Height { get; set; } = Length.Auto;

	[Group( "Layout" )] [Title( "Direction" )] [Description( "Stack children left-to-right (Row) or top-to-bottom (Column)." )]   public FlexDirection  Direction { get; set; } = FlexDirection.Row;
	[Group( "Layout" )] [Title( "Justify" )]   [Description( "How children are spaced along the stacking direction." )]           public JustifyContent Justify   { get; set; } = JustifyContent.Start;
	[Group( "Layout" )] [Title( "Align" )]     [Description( "How children line up across the stacking direction." )]             public AlignItems     Align     { get; set; } = AlignItems.Stretch;
	[Group( "Layout" )] [Title( "Gap" )]       [Description( "Space between children." )]                                         public float          Gap       { get; set; } = 8f;
	[Group( "Layout" )] [Title( "Padding" )]   [Description( "Space between this control's edge and its contents." )]             public Length         Padding   { get; set; } = Length.Px( 0 );
	// [EnumDropdown] opts out of EnumControlWidget's <4-entries segmented branch (WrapReverse clips at our column width).
	[Group( "Layout" )] [Title( "Wrap" )]      [Description( "Whether children wrap to a new line when they run out of room." )] [EnumDropdown] public FlexWrap Wrap { get; set; } = FlexWrap.NoWrap;

	[Group( "Flex" )] [Title( "Grow" )]   [Description( "How much this control stretches to fill empty space." )]    public float  FlexGrow   { get; set; } = 0f;
	// Default 1 (web CSS), not 0 (Yoga); without shrink, percent + gap overflows.
	[Group( "Flex" )] [Title( "Shrink" )] [Description( "How much this control shrinks when space is tight." )]      public float  FlexShrink { get; set; } = 1f;
	[Group( "Flex" )] [Title( "Basis" )]  [Description( "Starting size before stretching or shrinking." )]           public Length FlexBasis  { get; set; } = Length.Auto;

	[Group( "Content" )] [Title( "Content" )] public string Content { get; set; } = "";

	// TextEntry-only. Sandbox.UI.TextEntry.Placeholder is the greyed hint shown when the
	// field's Text is empty (and the engine adds a .placeholder class so the theme's
	// .textentry.placeholder selector kicks in). Saved razor emits placeholder="...".
	// Inspector hides Content for TextEntry and shows this instead — Content was never
	// the right semantic for an input field. Migration: FromDto on a TextEntry with empty
	// Placeholder reads legacy Content into Placeholder and clears Content. (grd-3oa.)
	[Group( "Content" )] [Title( "Placeholder" )]
	[Description( "Greyed hint text shown inside the TextEntry when empty." )]
	public string Placeholder { get; set; } = "";

	// Checkbox-only. Sizes the .checkmark box independently of FontSize (which governs
	// the label text). Default 16px matches PreviewTheme's .checkbox > .checkmark
	// baseline so a record at default emits a noop-equivalent rule. Inner glyph
	// font-size is derived as 0.75 * CheckboxSize at emit time (16/12 default ratio,
	// unit-preserving so px/rem/em/percent all scale cleanly). (grd-4gq.)
	[Group( "Content" )] [Title( "Box Size" )]
	[Description( "Size of the checkbox box (independent of label font size). Auto = theme baseline. Note: % height needs a fixed parent height to resolve against; % width works on flex." )]
	public Length CheckboxSize { get; set; } = Length.Px( 16 );

	// Image src= path. [ImageAssetPath] (extends AssetPathAttribute, AssetTypeExtension="jpg")
	// routes to ResourceStringControlWidget restricted to AssetType.ImageFile — png/jpg/jpeg
	// only, no Texture/.vtex conflation. The other engine attribute, [TextureImagePath], routes
	// to TextureImageControlWidget which DOES conflate with compiled .vtex assets — common
	// s&box UI pitfall, do not use here. Original [FilePath] choice was a workaround for an
	// imagined .vtex collision that doesn't actually apply to [ImageAssetPath].
	[Group( "Image" )] [Title( "Source" )]
	[Description( "Image asset for <image src='...'/>. Drag from Assets/ or click to pick." )]
	[ImageAssetPath]
	public string Source { get; set; } = "";

	// IconPanel glyph name. [IconName] auto-binds to IconControlWidget (the engine's grid
	// picker) — same widget SaveTemplateDialog uses for template icons. Mirrors how Image
	// owns Source instead of using Content; ShouldShow gates this field to IconPanel only
	// and hides Content for IconPanel.
	[Group( "Icon" )] [Title( "Icon" )]
	[Description( "Material Icons glyph name (e.g. star, help, settings). Click to open picker." )]
	[IconName]
	public string IconName { get; set; } = "";

	// Typography — Label only. `OverrideTypography` gates emission of the whole group so the
	// remaining four fields can carry sensible visible defaults (white #FFFFFF, 14px, 400)
	// without polluting the saved scss when the user hasn't opted in. Inspector hides the
	// four detail fields until Override is checked.
	[Group( "Typography" )] [Title( "Override" )]
	[Description( "Emit per-control typography rules. Off = inherit from theme." )]
	public bool OverrideTypography { get; set; } = false;

	[Group( "Typography" )] [Title( "Font" )]
	[Description( "Font family (e.g. Poppins, Roboto Mono). Empty = inherit family from theme." )]
	public string FontFamily { get; set; } = "";

	[Group( "Typography" )] [Title( "Size" )]
	[Description( "Font size." )]
	public Length FontSize { get; set; } = Length.Px( 14 );

	[Group( "Typography" )] [Title( "Weight" )]
	[Description( "CSS font-weight: 100-900 (common: 400 normal, 600 semibold, 700 bold)." )]
	public int FontWeight { get; set; } = 400;

	[Group( "Typography" )] [Title( "Color" )]
	[Description( "Color of the control's content text. On TextEntry this is the entered text only — placeholder hint stays muted via the theme's .textentry .placeholder rule." )]
	public Color Color { get; set; } = Color.White;

	// TextAlign extends the Typography group (gated by OverrideTypography). Inspector
	// further restricts to Label only — Button/TextEntry/IconPanel render text but
	// text-align on a flex container has CSS-level impact only on Label's content layout.
	// (grd-ire CSS Tier 2.)
	[Group( "Typography" )] [Title( "Align" )]
	[Description( "Horizontal text alignment within the label." )]
	public TextAlignment TextAlign { get; set; } = TextAlignment.Left;

	// Constraints — margin + size floors/ceilings. Universal except margin on root (root
	// has no parent flex container, same skip as FlexGrow/Shrink/Basis). All defaults are
	// Length.Auto so toggling Override on with no edits emits nothing visible. (grd-ire.)
	[Group( "Constraints" )] [Title( "Override" )]
	[Description( "Emit per-control margin / size constraint rules." )]
	public bool OverrideConstraints { get; set; } = false;

	[Group( "Constraints" )] [Title( "Margin" )]
	[Description( "Space between this control's edge and its siblings." )]
	public Length Margin { get; set; } = Length.Px( 0 );

	[Group( "Constraints" )] [Title( "Min Width" )]
	[Description( "Floor on this control's width." )]
	public Length MinWidth { get; set; } = Length.Auto;

	[Group( "Constraints" )] [Title( "Max Width" )]
	[Description( "Ceiling on this control's width." )]
	public Length MaxWidth { get; set; } = Length.Auto;

	[Group( "Constraints" )] [Title( "Min Height" )]
	[Description( "Floor on this control's height." )]
	public Length MinHeight { get; set; } = Length.Auto;

	[Group( "Constraints" )] [Title( "Max Height" )]
	[Description( "Ceiling on this control's height." )]
	public Length MaxHeight { get; set; } = Length.Auto;

	// Background — gated by OverrideBackground so unmodified records emit nothing into
	// saved scss. Mirrors typography group pattern (grd-fgs CSS Tier 1). All controls.
	[Group( "Background" )] [Title( "Override" )]
	[Description( "Emit per-control background rules. Off = inherit from theme." )]
	public bool OverrideBackground { get; set; } = false;

	[Group( "Background" )] [Title( "Color" )]
	[Description( "Background color." )]
	public Color BackgroundColor { get; set; } = Color.White;

	[Group( "Background" )] [Title( "Image" )]
	[Description( "Background image: url('asset.png'), linear-gradient(to bottom, #fff, #000), or empty." )]
	public string BackgroundImage { get; set; } = "";

	// Border — engine accepts longhand border-color/border-width directly (Sandbox.UI
	// Styles.Set.cs:73-81). border-radius uses single Length shorthand (all four corners).
	[Group( "Border" )] [Title( "Override" )]
	[Description( "Emit per-control border rules. Off = inherit from theme." )]
	public bool OverrideBorder { get; set; } = false;

	[Group( "Border" )] [Title( "Radius" )]
	[Description( "Corner radius (applies to all four corners)." )]
	public Length BorderRadius { get; set; } = Length.Px( 0 );

	[Group( "Border" )] [Title( "Color" )]
	[Description( "Border color." )]
	public Color BorderColor { get; set; } = Color.White;

	[Group( "Border" )] [Title( "Width" )]
	[Description( "Border thickness." )]
	public Length BorderWidth { get; set; } = Length.Px( 1 );

	// Effects — single-layer box-shadow. Engine supports comma-separated multi-layer
	// (Styles.Set.cs SetShadow), but the inspector exposes one layer for now; users can
	// hand-edit saved scss for multi-layer. Inset toggle reflects the keyword form.
	[Group( "Effects" )] [Title( "Override" )]
	[Description( "Emit per-control box-shadow. Off = inherit from theme." )]
	public bool OverrideEffects { get; set; } = false;

	[Group( "Effects" )] [Title( "Shadow X" )]
	[Description( "Horizontal offset of the box-shadow." )]
	public Length BoxShadowX { get; set; } = Length.Px( 0 );

	[Group( "Effects" )] [Title( "Shadow Y" )]
	[Description( "Vertical offset of the box-shadow." )]
	public Length BoxShadowY { get; set; } = Length.Px( 2 );

	[Group( "Effects" )] [Title( "Shadow Blur" )]
	[Description( "Blur radius of the box-shadow." )]
	public Length BoxShadowBlur { get; set; } = Length.Px( 4 );

	[Group( "Effects" )] [Title( "Shadow Color" )]
	[Description( "Box-shadow color." )]
	public Color BoxShadowColor { get; set; } = Color.Black;

	[Group( "Effects" )] [Title( "Shadow Inset" )]
	[Description( "Render the shadow inside the element instead of outside." )]
	public bool BoxShadowInset { get; set; } = false;

	// Opacity extends the Effects group (gated by OverrideEffects). 1 = fully opaque,
	// 0 = invisible. (grd-ire CSS Tier 2.)
	[Group( "Effects" )] [Title( "Opacity" )]
	[Description( "0 = transparent, 1 = fully opaque." )] [Range( 0, 1 )] [Step( 0.01f )]
	public float Opacity { get; set; } = 1f;

	// Interaction — cursor + overflow. Universal. Cursor is a curated enum (engine
	// accepts any string but designers want a fixed list). Overflow uses Visible default
	// matching the engine's FillDefaults. (grd-ire CSS Tier 2.)
	[Group( "Interaction" )] [Title( "Override" )]
	[Description( "Emit per-control cursor / overflow rules." )]
	public bool OverrideInteraction { get; set; } = false;

	[Group( "Interaction" )] [Title( "Cursor" )]
	[Description( "Mouse cursor when hovering this control." )]
	public CursorKind Cursor { get; set; } = CursorKind.Auto;

	[Group( "Interaction" )] [Title( "Overflow" )]
	[Description( "How to handle children that overflow this control's box." )]
	public OverflowKind Overflow { get; set; } = OverflowKind.Visible;

	[Hide] public Panel LivePanel { get; set; }

	// Copy all serialisable fields onto `target`. Excludes Type (init-only, set at
	// construction), ClassName (caller decides — mint vs preserve), Children (caller
	// recurses), LivePanel (runtime, not part of the document). Single source of truth
	// for the field list — adding a field here updates both clone paths and the JSON
	// DTO becomes the only other place that needs touching. (grd-11d.)
	public void CopyFieldsTo( ControlRecord target )
	{
		target.Width      = Width;
		target.Height     = Height;
		target.Direction  = Direction;
		target.Justify    = Justify;
		target.Align      = Align;
		target.Gap        = Gap;
		target.Padding    = Padding;
		target.Wrap       = Wrap;
		target.FlexGrow   = FlexGrow;
		target.FlexShrink = FlexShrink;
		target.FlexBasis  = FlexBasis;
		target.Content      = Content;
		target.Placeholder  = Placeholder;
		target.CheckboxSize = CheckboxSize;
		target.Source       = Source;
		target.IconName     = IconName;
		target.OverrideTypography = OverrideTypography;
		target.FontFamily = FontFamily;
		target.FontSize   = FontSize;
		target.FontWeight = FontWeight;
		target.Color      = Color;
		target.OverrideBackground = OverrideBackground;
		target.BackgroundColor    = BackgroundColor;
		target.BackgroundImage    = BackgroundImage;
		target.OverrideBorder     = OverrideBorder;
		target.BorderRadius       = BorderRadius;
		target.BorderColor        = BorderColor;
		target.BorderWidth        = BorderWidth;
		target.OverrideEffects    = OverrideEffects;
		target.BoxShadowX         = BoxShadowX;
		target.BoxShadowY         = BoxShadowY;
		target.BoxShadowBlur      = BoxShadowBlur;
		target.BoxShadowColor     = BoxShadowColor;
		target.BoxShadowInset     = BoxShadowInset;
		target.Opacity            = Opacity;
		target.TextAlign          = TextAlign;
		target.OverrideConstraints = OverrideConstraints;
		target.Margin             = Margin;
		target.MinWidth           = MinWidth;
		target.MaxWidth           = MaxWidth;
		target.MinHeight          = MinHeight;
		target.MaxHeight          = MaxHeight;
		target.OverrideInteraction = OverrideInteraction;
		target.Cursor             = Cursor;
		target.Overflow           = Overflow;
	}
}
