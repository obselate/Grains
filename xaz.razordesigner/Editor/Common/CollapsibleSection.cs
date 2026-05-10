using System;
using Editor;
using Sandbox;

namespace Grains.RazorDesigner.Common;

// Mimics the engine's InspectorHeader paint (chevron + icon + title, hover tint, top border).
// Source pattern: addons/tools/Code/Scene/GameObjectInspector/InspectorHeader.cs.
public sealed class CollapsibleSection : Widget
{
	public string Title { get; set; } = "";
	public string Icon { get; set; } = "";
	public Color HeaderColor { get; set; } = "#8E9199";
	public bool IsCollapsible { get; set; } = true;
	public Action<bool> ExpandedChanged;

	private readonly Header _header;
	private readonly Widget _body;
	private bool _expanded;

	public bool Expanded
	{
		get => _expanded;
		set
		{
			if ( _expanded == value ) return;
			_expanded = value;
			_body.Visible = _expanded;
			_header.Update();
			ExpandedChanged?.Invoke( _expanded );
		}
	}

	public Layout BodyLayout => _body.Layout;

	public CollapsibleSection( Widget parent, string title, string icon ) : base( parent )
	{
		Title = title;
		Icon = icon;

		Layout = Layout.Column();
		Layout.Margin = 0;
		Layout.Spacing = 0;

		_header = new Header( this );
		_header.OnToggled = () => { if ( IsCollapsible ) Expanded = !Expanded; };
		Layout.Add( _header );

		_body = new Widget( this );
		_body.Layout = Layout.Column();
		_body.Layout.Margin = new Sandbox.UI.Margin( 0 );
		Layout.Add( _body );

		_expanded = true;
	}

	private sealed class Header : Widget
	{
		public Action OnToggled;
		private readonly CollapsibleSection _owner;

		public Header( CollapsibleSection owner ) : base( owner )
		{
			_owner = owner;
			MouseTracking = true;
			FixedHeight = Theme.RowHeight + 4;
		}

		protected override void OnPaint()
		{
			var rect = LocalRect;
			Paint.Antialiasing = true;
			Paint.TextAntialiasing = true;

			// Top hairline + subtle shadow line, like InspectorHeader.
			{
				var top = rect;
				top.Bottom = top.Top + 1;
				Paint.SetBrushAndPen( Theme.ControlBackground );
				Paint.DrawRect( top );

				top.Position += new Vector2( 0, 1 );
				Paint.SetBrushAndPen( Theme.BorderLight );
				Paint.DrawRect( top );
			}

			// Hover highlight (only when interactive).
			if ( Paint.HasMouseOver && _owner.IsCollapsible )
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.Blue.WithAlpha( 0.10f ) );
				Paint.DrawRect( rect, 0 );
			}

			var color = _owner.HeaderColor;
			var opacity = _owner.Expanded ? 1f : 0.8f;

			float left = rect.Left + 4;
			if ( _owner.IsCollapsible )
			{
				var chevronRect = new Rect( left, rect.Top, 18, rect.Height );
				Paint.SetPen( color );
				Paint.DrawIcon(
					chevronRect,
					_owner.Expanded ? "arrow_drop_down" : "arrow_right",
					18,
					TextFlag.Center );
				left = chevronRect.Right + 4;
			}
			var iconText = string.IsNullOrEmpty( _owner.Icon ) ? "category" : _owner.Icon;
			var iconRect = new Rect( left, rect.Top, 20, rect.Height );
			Paint.SetPen( color.WithAlpha( opacity ) );
			Paint.DrawIcon( iconRect, iconText, 16, TextFlag.Center );

			// Title.
			var textRect = rect;
			textRect.Left = iconRect.Right + 6;
			textRect.Right -= 8;
			Paint.SetPen( Theme.Text.WithAlphaMultiplied( opacity ) );
			Paint.SetHeadingFont( 11, 440, sizeInPixels: true );
			Paint.DrawText( textRect, _owner.Title, TextFlag.LeftCenter );
		}

		protected override void OnMouseClick( MouseEvent e )
		{
			base.OnMouseClick( e );
			if ( e.LeftMouseButton )
				OnToggled?.Invoke();
		}
	}
}
