using System;
using Editor;
using Grains.RazorDesigner.Document;
using Sandbox;

namespace Grains.RazorDesigner.Palette;

public class PalettePanel : Widget
{
	private const string LogPrefix = "[Grains.RazorDesigner]";

	public PalettePanel( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 4;
		FixedWidth = 120;

		foreach ( ControlType type in Enum.GetValues( typeof( ControlType ) ) )
		{
			Layout.Add( new PaletteButton( type, parent: this ) );
		}

		Layout.AddStretchCell();

		Log.Info( $"{LogPrefix} PalettePanel ctor (drag-only)" );
	}

	private sealed class PaletteButton : Button
	{
		private readonly ControlType _type;

		public PaletteButton( ControlType type, Widget parent )
			: base( type.ToString(), ControlMetadata.Get( type ).IconName, parent )
		{
			_type = type;
			// Required: without it, press is consumed as a click and OnDragStart never fires.
			IsDraggable = true;
		}

		protected override void OnDragStart()
		{
			base.OnDragStart();

			var drag = new Drag( this );
			drag.Data.Object = _type;
			drag.Data.Text = $"palette:{_type}";
			drag.Execute();

			Log.Info( $"{LogPrefix} PaletteButton.OnDragStart: {_type}" );
		}
	}
}
