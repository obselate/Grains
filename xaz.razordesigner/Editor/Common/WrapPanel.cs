using System;
using System.Linq;
using Editor;
using Sandbox;

namespace Grains.RazorDesigner.Common;

// Flow layout for direct child widgets — wraps left-to-right, top-to-bottom based on available width.
// Engine has no public Layout.Flow(); DoLayout() is the canonical hook (see PerformanceDock, ThumbnailPreviewWidget).
public sealed class WrapPanel : Widget
{
	public int HSpacing { get; set; } = 4;
	public int VSpacing { get; set; } = 4;
	public int PaddingLeft { get; set; } = 4;
	public int PaddingTop { get; set; } = 4;
	public int PaddingRight { get; set; } = 4;
	public int PaddingBottom { get; set; } = 4;
	public int MinItemWidth { get; set; } = 96;
	public int ItemHeight { get; set; } = 26;

	// Convenience setter — applies the value to all four sides.
	public int Padding
	{
		set { PaddingLeft = PaddingTop = PaddingRight = PaddingBottom = value; }
	}

	public WrapPanel( Widget parent ) : base( parent )
	{
	}

	// Manual-layout panels never receive Qt LayoutRequest events when children mutate at
	// runtime (DestroyChildren + AddChild). Callers that mutate post-construction must
	// invoke Relayout() to re-position children and re-assert FixedHeight. Pattern is
	// engine-canonical: see addons/tools/Code/Editor/Group.SetWidget which calls DoLayout()
	// directly after re-parenting.
	public void Relayout()
	{
		DoLayout();
	}

	protected override void DoLayout()
	{
		base.DoLayout();

		var children = Children.Where( c => c.IsValid ).ToList();
		if ( children.Count == 0 )
		{
			FixedHeight = PaddingTop + PaddingBottom;
			return;
		}

		float availW = Math.Max( 0, Size.x - PaddingLeft - PaddingRight );
		int cols = Math.Max( 1, (int)Math.Floor( (availW + HSpacing) / (float)(MinItemWidth + HSpacing) ) );
		float itemW = cols == 1 ? availW : (availW - (cols - 1) * HSpacing) / cols;

		int col = 0, row = 0;
		foreach ( var child in children )
		{
			float x = PaddingLeft + col * (itemW + HSpacing);
			float y = PaddingTop + row * (ItemHeight + VSpacing);
			child.Position = new Vector2( x, y );
			child.Size = new Vector2( itemW, ItemHeight );

			col++;
			if ( col >= cols )
			{
				col = 0;
				row++;
			}
		}

		int rows = (children.Count + cols - 1) / cols;
		float totalH = PaddingTop + PaddingBottom + rows * ItemHeight + Math.Max( 0, rows - 1 ) * VSpacing;
		FixedHeight = totalH;
	}
}
