using Grains.RazorDesigner.Document;
using Sandbox;

namespace Grains.RazorDesigner.Selection;

// Tracks zero-or-one selected ControlRecord. Hit-test walks the document
// tree depth-first parent-before-children; the LAST hit wins, giving
// child-over-parent precedence on overlap (a Button inside a Layout is
// selected over the Layout). Selection visual is the ".selected" CSS class.
public sealed class SelectionController
{
	private const string LogPrefix = "[Grains.RazorDesigner]";
	private const string SelectedClass = "selected";

	private readonly DesignerDocument _document;

	public ControlRecord Selected { get; private set; }

	public SelectionController( DesignerDocument document )
	{
		_document = document;
	}

	// MouseEvent.LocalPosition is widget LOGICAL pixels; Panel.IsInside
	// expects framebuffer pixels (= logical * DpiScale).
	public bool TrySelectAt( float widgetX, float widgetY, float dpiScale )
	{
		if ( dpiScale < 0.01f ) dpiScale = 1f;
		var fbPos = new Vector2( widgetX * dpiScale, widgetY * dpiScale );

		ControlRecord deepest = null;
		var checkedCount = 0;
		foreach ( var r in _document.WalkAll() )
		{
			checkedCount++;
			if ( r.LivePanel is null || !r.LivePanel.IsValid ) continue;
			if ( r.LivePanel.IsInside( fbPos ) ) deepest = r;
		}

		if ( deepest is not null )
		{
			Log.Info( $"{LogPrefix} TrySelectAt widget=({widgetX:F0},{widgetY:F0}) fb=({fbPos.x:F0},{fbPos.y:F0}) hit {deepest.ClassName} (rect={deepest.LivePanel.Box.Rect})" );
			Select( deepest );
			return true;
		}

		Log.Info( $"{LogPrefix} TrySelectAt widget=({widgetX:F0},{widgetY:F0}) fb=({fbPos.x:F0},{fbPos.y:F0}) miss — {checkedCount} record(s) checked" );
		Deselect();
		return false;
	}

	public void Select( ControlRecord record )
	{
		if ( Selected == record ) return;

		if ( Selected?.LivePanel is { IsValid: true } prev )
			prev.RemoveClass( SelectedClass );

		Selected = record;

		if ( Selected?.LivePanel is { IsValid: true } cur )
			cur.AddClass( SelectedClass );

		Log.Info( $"{LogPrefix} Select({Selected?.ClassName ?? "<none>"})" );
	}

	public void Deselect()
	{
		if ( Selected is null ) return;
		if ( Selected.LivePanel is { IsValid: true } prev )
			prev.RemoveClass( SelectedClass );
		Log.Info( $"{LogPrefix} Deselect (was {Selected.ClassName})" );
		Selected = null;
	}

	public void DeleteSelected()
	{
		if ( Selected is null ) return;
		if ( Selected == _document.RootRecord )
		{
			Log.Warning( $"{LogPrefix} Delete: RootRecord is not deletable; ignored" );
			return;
		}
		var rec = Selected;
		Selected = null;
		_document.Remove( rec );
		Log.Info( $"{LogPrefix} DeleteSelected: removed {rec.ClassName}" );
	}
}
