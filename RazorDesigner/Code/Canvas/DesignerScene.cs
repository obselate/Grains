using System;
using System.Linq;
using System.Reflection;
using Sandbox;
using Sandbox.UI;

namespace Grains.RazorDesigner.Canvas;

// Editor-side preview scene hosting a CameraComponent + ScreenPanel inside an
// editor-flavoured Scene so we can render UI from a SceneRenderingWidget
// without going through a running game.
//
// CreateEditorScene + EditorTick (not new Scene + GameTick): every official
// sbox preview tool uses CreateEditorScene + EditorTick. GameTick on a
// non-editor scene runs SceneNetworkUpdate + game-only plumbing every frame.
//
// ScreenPanel is sealed and doesn't implement ExecuteInEditor, so under
// IsEditor=true the Component.ShouldExecute gate prevents OnAwake AND
// OnDestroy from being scheduled. ForceLifecycle bridges startup;
// ForceTeardown bridges cleanup (without it, the GameRootPanel stays in
// UISystem.RootPanels forever — one leak per dock open / refresh / hotload).
//
// Root-isolation (TryIsolateRootFromMenuPump): the menu pump's
// UISystem.PreLayout iterates ALL registered roots and calls PreLayout with
// the engine swap-chain rect, which makes our preview lay out at engine-screen
// dimensions, not widget dimensions. We can't override RootPanel.UpdateBounds
// / UpdateScale (GameRootPanel + ScreenPanel are sealed), so we reflectively
// remove our root from UISystem.RootPanels and drive layout ourselves each
// frame. Render still works because ScreenPanel.Render calls
// rootPanel.RenderManual() — public and doesn't require UISystem participation.
public sealed class DesignerScene : IDisposable
{
	private const string LogPrefix = "[Grains.RazorDesigner]";

	public Scene Scene { get; }
	public CameraComponent Camera { get; }
	public ScreenPanel ScreenPanel { get; }
	public Panel Root => ScreenPanel.GetPanel();

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

	// Call BEFORE Scene.Destroy() so the component still has a valid scene
	// context while it tears down.
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
			Log.Info( $"{LogPrefix} IsolateRoot: removed from UISystem.RootPanels — engine layout pump will skip it" );
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

	// Scale = DpiScale keeps CSS authoring in logical pixels — Length.Pixels(24)
	// renders at 24 logical px regardless of OS display scaling. PanelBounds is
	// in framebuffer space (camera presents to Size * DpiScale).
	//
	// BuildDescriptors must run after Layout: RenderManual calls BuildCommandList,
	// which consumes descriptors from BuildDescriptors. UISystem.Simulate
	// normally runs both; we mirror that since we opted out of Simulate.
	private void DriveLayout( Panel root, float widthPx, float heightPx, float dpiScale )
	{
		if ( widthPx < 1f || heightPx < 1f ) return;
		if ( dpiScale < 0.01f ) dpiScale = 1.0f;

		var rootType = root.GetType();
		EnsureReflectionResolved( rootType );

		_rootPanelBoundsProperty?.SetValue( root,
			new Rect( 0, 0, widthPx * dpiScale, heightPx * dpiScale ) );
		_rootScaleProperty?.SetValue( root, dpiScale );

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

	// Walk up the type hierarchy because the methods we want are internal to
	// RootPanel base, not declared on concrete GameRootPanel.
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

		// ForceTeardown calls rootPanel.Delete() which runs OnDeleted →
		// RemoveRoot. No-op if isolation already removed us; still needed
		// for the path where isolation failed.
		ForceTeardown( ScreenPanel );

		Scene?.Destroy();
	}
}
