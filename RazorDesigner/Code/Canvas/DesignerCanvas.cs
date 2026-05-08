using Editor;
using Grains.RazorDesigner.Document;
using Sandbox;

namespace Grains.RazorDesigner.Canvas;

// Inherits SceneRenderingWidget directly (same shape as ShaderGraph's
// PreviewPanel) so we can override PreFrame to advance the scene.
public class DesignerCanvas : SceneRenderingWidget
{
	private const string LogPrefix = "[Grains.RazorDesigner]";

	private readonly DesignerScene _designerScene;

	public DesignerCanvas( Widget parent ) : base( parent )
	{
		Log.Info( $"{LogPrefix} DesignerCanvas ctor" );

		_designerScene = new DesignerScene();

		Scene = _designerScene.Scene;
		Camera = _designerScene.Camera;

		HorizontalSizeMode = SizeMode.Default | SizeMode.Expand;
		VerticalSizeMode = SizeMode.Default | SizeMode.Expand;

		// Without AcceptDrops, OnDragHover/OnDragDrop are never invoked.
		AcceptDrops = true;
	}

	public DesignerScene DesignerScene => _designerScene;

	// Position is widget pixels (top-left origin).
	public event System.Action<Vector2> CanvasClicked;

	public event System.Action<ControlType, Vector2> RecordDropped;

	public override void OnDragHover( DragEvent ev )
	{
		base.OnDragHover( ev );
		if ( ev.Data.Object is ControlType )
		{
			ev.Action = DropAction.Copy;
		}
	}

	public override void OnDragDrop( DragEvent ev )
	{
		base.OnDragDrop( ev );
		if ( ev.Data.Object is ControlType type )
		{
			Log.Info( $"{LogPrefix} DesignerCanvas drop: {type} at widget ({ev.LocalPosition.x:F0}, {ev.LocalPosition.y:F0})" );
			RecordDropped?.Invoke( type, ev.LocalPosition );
		}
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );
		if ( e.LeftMouseButton )
		{
			var pos = e.LocalPosition;
			Log.Info( $"{LogPrefix} DesignerCanvas click at widget ({pos.x:F0}, {pos.y:F0})" );
			CanvasClicked?.Invoke( pos );
		}
	}

	protected override void PreFrame()
	{
		base.PreFrame();

		if ( !_designerScene.Scene.IsValid() )
			return;

		using ( _designerScene.Scene.Push() )
		{
			// EditorTick is the canonical preview-scene tick. GameTick would
			// run SceneNetworkUpdate + game-only plumbing every frame.
			_designerScene.Scene.EditorTick( RealTime.Now, RealTime.Delta );
		}

		// SceneRenderingWidget.RenderScene presents to a framebuffer of
		// Size * DpiScale, so layout has to happen in framebuffer-pixel space.
		// We set Scale = DpiScale in DesignerScene so authoring stays in
		// logical px (font-size: 24 reads as 24 logical px regardless of OS).
		_designerScene.Update( Size.x, Size.y, DpiScale );
	}

	public override void OnDestroyed()
	{
		Log.Info( $"{LogPrefix} DesignerCanvas.OnDestroyed" );
		base.OnDestroyed();
		_designerScene.Dispose();
	}
}
