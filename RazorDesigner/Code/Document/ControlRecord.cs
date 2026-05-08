using System.Collections.Generic;
using Sandbox;
using Sandbox.UI;

namespace Grains.RazorDesigner.Document;

// One placed control. Width/Height use Length (Length.Auto = yoga auto-size
// from content). Flex container props (Direction/Justify/Align/Gap/Padding/Wrap)
// only apply to container records (Panel, Layout — see ControlMeta.IsContainer);
// FlexGrow/Shrink/Basis apply when this record is a child of a flex container.
// LivePanel mirrors the preview Panel; it's nulled across hotload-induced
// canvas rebuilds and re-wired by RepopulateMirror.
public sealed class ControlRecord
{
	[Hide] public ControlType Type { get; init; }

	// CSS class name. Drives saved .razor `class="..."` and saved scss selectors.
	// User-editable via the inspector (validated for CSS-identifier shape and
	// document-wide collision); RootRecord's "root" is the isRoot sentinel and
	// is locked — the inspector hides the field on RootRecord.
	[Title( "Name" )]
	[Description( "CSS class name for this control. Used in the saved .razor and .razor.scss." )]
	public string ClassName { get; set; }

	[Hide] public List<ControlRecord> Children { get; init; } = new();

	[Title( "Width" )]   [Description( "How wide this control is." )]                public Length Width  { get; set; } = Length.Auto;
	[Title( "Height" )]  [Description( "How tall this control is." )]                public Length Height { get; set; } = Length.Auto;

	[Title( "Content" )] public string Content { get; set; } = "";

	[Title( "Direction" )] [Description( "Stack children left-to-right (Row) or top-to-bottom (Column)." )]   public FlexDirection  Direction { get; set; } = FlexDirection.Row;
	[Title( "Justify" )]   [Description( "How children are spaced along the stacking direction." )]           public JustifyContent Justify   { get; set; } = JustifyContent.Start;
	[Title( "Align" )]     [Description( "How children line up across the stacking direction." )]             public AlignItems     Align     { get; set; } = AlignItems.Stretch;
	[Title( "Gap" )]       [Description( "Space between children." )]                                         public float          Gap       { get; set; } = 8f;
	[Title( "Padding" )]   [Description( "Space between this control's edge and its contents." )]             public Length         Padding   { get; set; } = Length.Px( 0 );
	// [EnumDropdown] opts out of EnumControlWidget's "<4 entries → segmented
	// GroupButtonControlWidget" branch (engine: EnumControlWidget.cs:37). FlexWrap
	// has 3 values and 'WrapReverse' clips at our inspector column width.
	[Title( "Wrap" )]      [Description( "Whether children wrap to a new line when they run out of room." )] [EnumDropdown] public FlexWrap Wrap { get; set; } = FlexWrap.NoWrap;

	[Title( "Grow" )]   [Description( "How much this control stretches to fill empty space." )]    public float  FlexGrow   { get; set; } = 0f;
	// Default 1 (web CSS), not 0 (Yoga). Without shrink, percent + gap overflows.
	[Title( "Shrink" )] [Description( "How much this control shrinks when space is tight." )]      public float  FlexShrink { get; set; } = 1f;
	[Title( "Basis" )]  [Description( "Starting size before stretching or shrinking." )]           public Length FlexBasis  { get; set; } = Length.Auto;

	[Hide] public Panel LivePanel { get; set; }
}
