using System;
using Editor;
using Sandbox;

namespace Grains.RazorDesigner.Canvas;

// Hosts a single DesignerCanvas child and sizes/centers it to a chosen viewport aspect ratio.
// Viewport=null -> child fills the frame (Fit). Viewport=(W,H) -> child sized to scale-fit and centered;
// the surrounding letterbox area paints the dock background so it reads as bars, not whitespace.
//
// Manual layout pattern: override DoLayout, set child.Position/child.Size directly. This is the
// canonical sbox preview-widget approach (see CLAUDE.md verified fact #12).
public sealed class CanvasViewportFrame : Widget
{
	private const string LogPrefix = "[Grains.RazorDesigner]";

	private DesignerCanvas _canvas;
	private Vector2? _viewport;

	public CanvasViewportFrame( Widget parent ) : base( parent )
	{
		HorizontalSizeMode = SizeMode.Default | SizeMode.Expand;
		VerticalSizeMode = SizeMode.Default | SizeMode.Expand;
	}

	public DesignerCanvas Canvas
	{
		get => _canvas;
		set
		{
			_canvas = value;
			ApplyChildLayout();
		}
	}

	public Vector2? Viewport
	{
		get => _viewport;
		set
		{
			if ( _viewport == value ) return;
			_viewport = value;
			Log.Info( $"{LogPrefix} CanvasViewportFrame.Viewport={(value.HasValue ? $"{value.Value.x}x{value.Value.y}" : "Fit")}" );
			if ( _canvas is not null )
				_canvas.DesignerScene.ViewportLogical = value;
			ApplyChildLayout();
		}
	}

	protected override void DoLayout()
	{
		base.DoLayout();
		ApplyChildLayout();
	}

	private void ApplyChildLayout()
	{
		if ( _canvas is null ) return;

		var size = Size;
		if ( size.x < 1f || size.y < 1f ) return;

		if ( _viewport is null || _viewport.Value.x < 1f || _viewport.Value.y < 1f )
		{
			_canvas.Position = Vector2.Zero;
			_canvas.Size = size;
			return;
		}

		var vp = _viewport.Value;
		var scale = MathF.Min( size.x / vp.x, size.y / vp.y );
		var w = MathF.Floor( vp.x * scale );
		var h = MathF.Floor( vp.y * scale );
		var x = MathF.Floor( (size.x - w) * 0.5f );
		var y = MathF.Floor( (size.y - h) * 0.5f );
		_canvas.Position = new Vector2( x, y );
		_canvas.Size = new Vector2( w, h );
	}

	protected override void OnPaint()
	{
		// Frame background = dock chrome color. Visible as letterbox bars when child is smaller than frame.
		Paint.ClearPen();
		Paint.SetBrush( new Color( 0.06f, 0.07f, 0.08f ) );
		Paint.DrawRect( LocalRect, 0f );
	}
}
