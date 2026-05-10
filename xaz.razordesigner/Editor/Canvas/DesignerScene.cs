using System;
using System.Linq;
using System.Reflection;
using Sandbox;
using Sandbox.UI;

namespace Grains.RazorDesigner.Canvas;

// bd memories: screenpanel-force-lifecycle, stylesheet-restyle-bug. CLAUDE.md verified facts #1-#7.
public sealed class DesignerScene : IDisposable
{
	private const string LogPrefix = "[Grains.RazorDesigner]";

	public Scene Scene { get; }
	public CameraComponent Camera { get; }
	public ScreenPanel ScreenPanel { get; }
	public Panel Root => ScreenPanel.GetPanel();

	// null = match widget framebuffer (Fit). Set to a logical-pixel size (e.g. 1920x1080) and DriveLayout
	// will pick Scale so Length.Px(logical.X) fills the widget framebuffer width.
	public Vector2? ViewportLogical { get; set; }

	private bool _rootLogged;
	private bool _layoutIsolated;
	private bool _reflectionLogged;

	private MethodInfo _rootLayoutMethod;
	private MethodInfo _rootBuildDescriptorsMethod;
	private PropertyInfo _rootScaleProperty;
	private PropertyInfo _rootPanelBoundsProperty;

	public DesignerScene()
	{
		Log.Info( $"{LogPrefix} DesignerScene ctor (CreateEditorScene + EditorTick)" );

		Scene = Scene.CreateEditorScene();
		Scene.Name = "Razor Designer";

		using ( Scene.Push() )
		{
			var cameraGo = new GameObject( true, "camera" );
			Camera = cameraGo.AddComponent<CameraComponent>();
			Camera.BackgroundColor = new Color( 0.10f, 0.11f, 0.13f );
			Camera.IsMainCamera = false;

			var uiGo = new GameObject( true, "ui" );
			ScreenPanel = uiGo.AddComponent<ScreenPanel>();
			ScreenPanel.TargetCamera = Camera;

			ForceLifecycle( ScreenPanel );
		}
	}

	private static void ForceLifecycle( Component component )
	{
		var type = component.GetType();
		const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

		var awake = type.GetMethod( "OnAwake", flags );
		var enabled = type.GetMethod( "OnEnabled", flags );

		try
		{
			awake?.Invoke( component, null );
			enabled?.Invoke( component, null );
			Log.Info( $"{LogPrefix} ForceLifecycle({type.Name}) ok awake={awake != null} enabled={enabled != null}" );
		}
		catch ( System.Exception ex )
		{
			Log.Error( $"{LogPrefix} ForceLifecycle({type.Name}) threw: {ex.GetType().Name}: {ex.Message}" );
			throw;
		}
	}

	// Call BEFORE Scene.Destroy(); component needs a valid scene to tear down.
	private static void ForceTeardown( Component component )
	{
		if ( component is null ) return;

		var type = component.GetType();
		const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

		var disabled = type.GetMethod( "OnDisabled", flags );
		var destroy = type.GetMethod( "OnDestroy", flags );

		try
		{
			disabled?.Invoke( component, null );
			destroy?.Invoke( component, null );
			Log.Info( $"{LogPrefix} ForceTeardown({type.Name}) ok disabled={disabled != null} destroy={destroy != null}" );
		}
		catch ( System.Exception ex )
		{
			Log.Error( $"{LogPrefix} ForceTeardown({type.Name}) threw: {ex.GetType().Name}: {ex.Message}" );
		}
	}

	private static bool TryIsolateRootFromMenuPump( Panel root )
	{
		if ( root is null ) return false;

		try
		{
			var globalContextType = AppDomain.CurrentDomain.GetAssemblies()
				.Select( a => SafeGetType( a, "Sandbox.Engine.GlobalContext" ) )
				.FirstOrDefault( t => t is not null );

			if ( globalContextType is null )
			{
				Log.Warning( $"{LogPrefix} IsolateRoot: GlobalContext type not found" );
				return false;
			}

			var current = globalContextType
				.GetProperty( "Current", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static )
				?.GetValue( null );
			if ( current is null )
			{
				Log.Warning( $"{LogPrefix} IsolateRoot: GlobalContext.Current is null" );
				return false;
			}

			var uiSystem = current.GetType()
				.GetProperty( "UISystem", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance )
				?.GetValue( current );
			if ( uiSystem is null )
			{
				Log.Warning( $"{LogPrefix} IsolateRoot: GlobalContext.UISystem is null" );
				return false;
			}

			var removeRoot = uiSystem.GetType().GetMethod(
				"RemoveRoot", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance );
			if ( removeRoot is null )
			{
				Log.Warning( $"{LogPrefix} IsolateRoot: UISystem.RemoveRoot not found" );
				return false;
			}

			removeRoot.Invoke( uiSystem, new object[] { root } );
			Log.Info( $"{LogPrefix} IsolateRoot: removed from UISystem.RootPanels (engine layout pump will skip it)" );
			return true;
		}
		catch ( Exception ex )
		{
			Log.Warning( $"{LogPrefix} IsolateRoot threw: {ex.GetType().Name}: {ex.Message}" );
			return false;
		}
	}

	private static Type SafeGetType( Assembly a, string fullName )
	{
		try { return a.GetType( fullName, throwOnError: false ); }
		catch { return null; }
	}

	// BuildDescriptors must run after Layout (RenderManual consumes its output).
	// widthPx/heightPx are widget pixels; framebuffer space is widget * dpiScale. When ViewportLogical
	// is set, Scale is chosen so the logical viewport maps onto the widget framebuffer (aspect-correct
	// letterboxing is the host widget's job — see CanvasViewportFrame).
	private void DriveLayout( Panel root, float widthPx, float heightPx, float dpiScale )
	{
		if ( widthPx < 1f || heightPx < 1f ) return;
		if ( dpiScale < 0.01f ) dpiScale = 1.0f;

		var fbW = widthPx * dpiScale;
		var fbH = heightPx * dpiScale;

		var logical = ViewportLogical ?? new Vector2( widthPx, heightPx );
		if ( logical.x < 1f || logical.y < 1f ) logical = new Vector2( widthPx, heightPx );

		// Scale-to-fit on the smaller axis. With letterboxed widget the two ratios are equal; with a
		// raw mismatched widget (no letterbox host) we pick the dimension that fits.
		var scale = MathF.Min( fbW / logical.x, fbH / logical.y );
		if ( scale < 0.01f ) scale = dpiScale;

		var rootType = root.GetType();
		EnsureReflectionResolved( rootType );

		_rootPanelBoundsProperty?.SetValue( root, new Rect( 0, 0, fbW, fbH ) );
		_rootScaleProperty?.SetValue( root, scale );

		try
		{
			_rootLayoutMethod?.Invoke( root, null );
			_rootBuildDescriptorsMethod?.Invoke( root, new object[] { 1.0f } );
		}
		catch ( Exception ex )
		{
			Log.Error( $"{LogPrefix} DriveLayout threw: {ex.GetType().Name}: {ex.Message}" );
		}
	}

	private void EnsureReflectionResolved( Type rootType )
	{
		_rootLayoutMethod ??= ResolveMethod( rootType, "Layout", Type.EmptyTypes );
		_rootBuildDescriptorsMethod ??= ResolveMethod( rootType, "BuildDescriptors", new[] { typeof( float ) } );
		_rootScaleProperty ??= ResolveProperty( rootType, "Scale", typeof( float ) );
		_rootPanelBoundsProperty ??= ResolveProperty( rootType, "PanelBounds", typeof( Rect ) );

		if ( !_reflectionLogged )
		{
			Log.Info( $"{LogPrefix} DriveLayout reflection resolved: " +
				$"Layout={_rootLayoutMethod is not null} " +
				$"BuildDescriptors={_rootBuildDescriptorsMethod is not null} " +
				$"Scale={_rootScaleProperty is not null} " +
				$"PanelBounds={_rootPanelBoundsProperty is not null}" );
			_reflectionLogged = true;
		}
	}

	// Walk up: methods we want are on RootPanel base, not concrete GameRootPanel.
	private static MethodInfo ResolveMethod( Type startType, string name, Type[] paramTypes )
	{
		for ( var t = startType; t is not null; t = t.BaseType )
		{
			var method = t.GetMethod( name,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
				binder: null,
				types: paramTypes,
				modifiers: null );
			if ( method is not null ) return method;
		}
		return null;
	}

	private static PropertyInfo ResolveProperty( Type startType, string name, Type expectedType )
	{
		for ( var t = startType; t is not null; t = t.BaseType )
		{
			var prop = t.GetProperty( name,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance );
			if ( prop is not null && prop.PropertyType == expectedType )
				return prop;
		}
		return null;
	}

	public void Update( float widthPx, float heightPx, float dpiScale )
	{
		var root = Root;
		if ( !root.IsValid() )
			return;

		if ( !_layoutIsolated )
		{
			_layoutIsolated = TryIsolateRootFromMenuPump( root );
		}

		if ( !_rootLogged )
		{
			Log.Info( $"{LogPrefix} root valid (width={widthPx} height={heightPx} dpiScale={dpiScale})" );
			_rootLogged = true;
		}

		DriveLayout( root, widthPx, heightPx, dpiScale );
	}

	public void Dispose()
	{
		Log.Info( $"{LogPrefix} DesignerScene.Dispose" );

		ForceTeardown( ScreenPanel );

		Scene?.Destroy();
	}
}
