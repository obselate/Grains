using System.Collections.Generic;
using Editor;
using Grains.RazorDesigner.Canvas;
using Grains.RazorDesigner.Document;
using Grains.RazorDesigner.Hierarchy;
using Grains.RazorDesigner.Inspector;
using Grains.RazorDesigner.Palette;
using Grains.RazorDesigner.Selection;
using Grains.RazorDesigner.Serialization;
using Grains.RazorDesigner.Templates;
using Sandbox;
using Sandbox.UI;

namespace Grains.RazorDesigner;

[Dock( "Editor", "Razor Designer", "brush" )]
public class DesignerWindow : Widget
{
	private const string LogPrefix = "[Grains.RazorDesigner]";
	private const string SplitterOuterCookie = "razordesigner.splitter.outer";
	private const string SplitterLeftCookie  = "razordesigner.splitter.left";
	private const string ThemePathCookie     = "razordesigner.theme.path";
	private const string CanvasSizeCookie    = "razordesigner.canvas.size";
	private const string ChromeHiddenCookie  = "razordesigner.chrome.hidden";
	// Class on canvas root drives the chrome-hidden CSS branch in PreviewMarkerRules.
	private const string ChromeHiddenClass   = "chrome-hidden";

	private Layout _canvasHost;
	private PalettePanel _palette;
	private HierarchyPanel _hierarchy;
	private InspectorPanel _inspector;
	private DesignerDocument _document;
	private SelectionController _selection;
	private Splitter _outerSplitter;
	private Splitter _leftStackSplitter;
	private Editor.Option _saveOption;
	private Editor.Option _themeOption;
	private Editor.Option _viewportOption;
	private Editor.Option _chromeHiddenOption;
	private bool _chromeHidden;
	private CanvasViewportFrame _viewportFrame;
	private Vector2? _viewport;        // null = Fit
	private string _viewportLabel = "Fit";
	private PreviewTheme _theme = PreviewTheme.Default;
	private string _trackedPath;
	private string _lastAppliedCss;
	// bd memory: stylesheet-leak. Track sheet by ref; Remove(string) is a no-op on FromString sheets.
	private StyleSheet _previewSheet;

	public DesignerWindow( Widget parent ) : base( parent )
	{
		Log.Info( $"{LogPrefix} DesignerWindow ctor" );

		Layout = Layout.Column();
		Layout.Margin = 0;
		Layout.Spacing = 0;

		var toolBar = new ToolBar( this, "RazorDesignerToolBar" );
		toolBar.SetIconSize( 16 );

		var newOption = toolBar.AddOption( null, "add", () => OnNew() );
		newOption.ToolTip = "New design (clears canvas)  [Ctrl+N]";
		newOption.StatusTip = "Discard the current design and start fresh.";

		_saveOption = toolBar.AddOption( null, "save", () => OnSave() );
		_saveOption.ToolTip = "Save design (.razor + .razor.scss)  [Ctrl+S]";
		_saveOption.StatusTip = "Write .razor markup and paired .razor.scss stylesheet.";

		var refreshOption = toolBar.AddOption( null, "refresh", () => Refresh() );
		refreshOption.ToolTip = "Refresh preview (also fires automatically on code reload)";
		refreshOption.StatusTip = "Tear down and rebuild the preview canvas.";

		toolBar.AddSeparator();

		_themeOption = toolBar.AddOption( null, "palette", OnThemeButton );
		_themeOption.StatusTip = "Choose preview theme — Default or load a custom .scss.";
		RestoreThemeFromCookie();
		UpdateThemeOptionTooltip();

		toolBar.AddSeparator();

		_viewportOption = toolBar.AddOption( null, "aspect_ratio", OnViewportButton );
		_viewportOption.StatusTip = "Choose canvas viewport — Fit (match pane) or a fixed size with letterboxing.";
		RestoreViewportFromCookie();
		UpdateViewportOptionTooltip();

		// Chrome toggle: flips between scaffold view (borders/labels/selection outline)
		// and a "real view" of the design as it would render outside the editor. Drives
		// the .chrome-hidden CSS branch in DocumentSerializer.PreviewMarkerRules.
		_chromeHiddenOption = toolBar.AddOption( null, "visibility", OnChromeToggle );
		_chromeHiddenOption.Checkable = true;
		_chromeHidden = EditorCookie.Get<bool>( ChromeHiddenCookie, false );
		_chromeHiddenOption.Checked = _chromeHidden;
		_chromeHiddenOption.ToolTip = "Toggle scaffold chrome (borders, empty-container labels, empty-image placeholder, selection outline).";
		_chromeHiddenOption.StatusTip = "When off, the canvas previews the design as it would render outside the editor.";

		Layout.Add( toolBar );

		_document = new DesignerDocument();

		// 3-column split: [leftStack | canvas | inspector]. Canvas dominates by stretch.
		// Left column is a vertical splitter — Hierarchy on top, Palette on bottom.
		_outerSplitter = new Splitter( this );
		_outerSplitter.IsHorizontal = true;
		StyleSplitter( _outerSplitter );
		Layout.Add( _outerSplitter, 1 );

		// IsHorizontal=false is a no-op in engine (setter always assigns Horizontal); IsVertical=true is the correct flip.
		_leftStackSplitter = new Splitter( this );
		_leftStackSplitter.IsVertical = true;
		_leftStackSplitter.MinimumWidth = 200;
		_leftStackSplitter.MaximumWidth = 280;
		StyleSplitter( _leftStackSplitter );

		_hierarchy = new HierarchyPanel( this, _document );
		_leftStackSplitter.AddWidget( _hierarchy );

		_palette = new PalettePanel( this );
		_palette.TypeAddRequested += OnPaletteTypeAddRequested;
		var paletteScroll = new ScrollArea( this );
		paletteScroll.Canvas = _palette;
		_leftStackSplitter.AddWidget( paletteScroll );

		_leftStackSplitter.SetStretch( 0, 1 );
		_leftStackSplitter.SetStretch( 1, 1 );
		_outerSplitter.AddWidget( _leftStackSplitter );

		var canvasContainer = new Widget( this );
		canvasContainer.Layout = Layout.Column();
		canvasContainer.SetSizeMode( SizeMode.CanGrow, SizeMode.CanGrow );
		canvasContainer.MinimumWidth = 320;
		_canvasHost = canvasContainer.Layout;
		_outerSplitter.AddWidget( canvasContainer );

		_inspector = new InspectorPanel( this, _document );
		_inspector.ValueChanged += OnInspectorValueChanged;
		_inspector.ClassNameChanged += OnInspectorClassNameChanged;
		_inspector.MinimumWidth = 280;
		_inspector.MaximumWidth = 380;
		_outerSplitter.AddWidget( _inspector );

		_outerSplitter.SetStretch( 0, 0 );
		_outerSplitter.SetStretch( 1, 10 );
		_outerSplitter.SetStretch( 2, 0 );
		// Qt QSplitter defaults SetCollapsible(true) on every cell — meaning the splitter is
		// free to size a stretch=0 child below its MinimumWidth (often to 0px) when no saved
		// state pins the size. That's what bit fresh-install: the left stack and inspector
		// rendered as 0-width panes on first open. Pinning collapsible=false makes Qt
		// respect MinimumWidth as a hard floor on initial layout. (grd-zw0.)
		_outerSplitter.SetCollapsible( 0, false );
		_outerSplitter.SetCollapsible( 1, false );
		_outerSplitter.SetCollapsible( 2, false );
		_leftStackSplitter.SetCollapsible( 0, false );
		_leftStackSplitter.SetCollapsible( 1, false );

		RestoreSplitterState();
		ApplyDefaultSplitterSizes();

		_selection = new SelectionController( _document );
		_hierarchy.RecordSelected += OnHierarchyRowClicked;
		_hierarchy.SelectionChanged += OnHierarchySelectionChanged;
		_hierarchy.RecordMoved += OnRecordMoved;
		_hierarchy.RecordsDeleteRequested += OnHierarchyDeleteRequested;
		_hierarchy.RecordCreated += OnHierarchyRecordCreated;
		_hierarchy.RecordsCutRequested += OnHierarchyCutRequested;
		_hierarchy.RecordsCopyRequested += OnHierarchyCopyRequested;
		_hierarchy.RecordPasteRequested += OnHierarchyPasteRequested;
		_hierarchy.RecordRenameRequested += OnHierarchyRenameRequested;
		_hierarchy.RecordAddRequested += OnHierarchyAddRequested;
		_hierarchy.RecordsSaveAsTemplateRequested += OnHierarchySaveAsTemplateRequested;
		_hierarchy.TemplateDropRequested += OnHierarchyTemplateDropRequested;
		_palette.TemplateAddRequested += OnPaletteTemplateAddRequested;

		// TabOrClickOrWheel is required for the dock to receive Delete keypresses.
		FocusMode = FocusMode.TabOrClickOrWheel;

		Build();
	}

	public DesignerCanvas Canvas { get; private set; }

	// Splitter handles default to invisible; this paints them with a tinted divider so users can grab them.
	private static void StyleSplitter( Splitter splitter )
	{
		splitter.HandleWidth = 4;
		splitter.SetStyles(
			"QSplitter::handle { background-color: #1d1d1d; }" +
			"QSplitter::handle:hover { background-color: #4aa0ff; }" +
			"QSplitter::handle:pressed { background-color: #4aa0ff; }" );
	}

	[EditorEvent.Hotload]
	public void Refresh()
	{
		Log.Info( $"{LogPrefix} === Refresh ===" );
		Build();
	}

	// Frame-gated so we don't redundantly bash Enabled every paint; only flips when state changes.
	[EditorEvent.Frame]
	private void OnFrame()
	{
		if ( _saveOption is null || _document is null ) return;
		var canSave = _document.RootRecord.Children.Count > 0;
		if ( _saveOption.Enabled != canSave )
			_saveOption.Enabled = canSave;
	}

	private bool _outerSplitterRestored;
	private bool _leftSplitterRestored;

	private void RestoreSplitterState()
	{
		var outer = EditorCookie.Get<string>( SplitterOuterCookie, null );
		if ( !string.IsNullOrEmpty( outer ) )
		{
			_outerSplitter?.RestoreState( outer );
			_outerSplitterRestored = true;
			Log.Info( $"{LogPrefix} Splitter state restored (outer)" );
		}
		var left = EditorCookie.Get<string>( SplitterLeftCookie, null );
		if ( !string.IsNullOrEmpty( left ) )
		{
			_leftStackSplitter?.RestoreState( left );
			_leftSplitterRestored = true;
			Log.Info( $"{LogPrefix} Splitter state restored (left)" );
		}
	}

	// Belt to SetCollapsible(false)'s braces: when no saved state existed, set explicit
	// preferred widths on the side widgets so Qt's initial layout pass has size hints
	// to work with (Qt QSplitter consults each child's current Size for initial layout
	// when stretch factors are zero). MinimumWidth alone is a floor, not a hint.
	// Defaults sit between the widgets' own MinimumWidth/MaximumWidth bounds. (grd-zw0.)
	private void ApplyDefaultSplitterSizes()
	{
		if ( !_outerSplitterRestored )
		{
			if ( _leftStackSplitter is not null ) _leftStackSplitter.Width = 240f;
			if ( _inspector is not null ) _inspector.Width = 320f;
			Log.Info( $"{LogPrefix} Outer splitter: no saved state, applied default widths (left=240, inspector=320)" );
		}
		// Left stack is hierarchy / palette stacked vertically — split evenly when fresh.
		if ( !_leftSplitterRestored && _hierarchy is not null )
		{
			Log.Info( $"{LogPrefix} Left splitter: no saved state, even split (Qt default with stretch 1/1 already correct)" );
		}
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();
		try
		{
			if ( _outerSplitter is not null )
				EditorCookie.Set( SplitterOuterCookie, _outerSplitter.SaveState() );
			if ( _leftStackSplitter is not null )
				EditorCookie.Set( SplitterLeftCookie, _leftStackSplitter.SaveState() );
			Log.Info( $"{LogPrefix} Splitter state saved on destroy" );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"{LogPrefix} Splitter state save failed: {ex.Message}" );
		}
	}

	private void OnThemeButton()
	{
		var menu = new Editor.Menu( this );

		// Defaults submenu: engine baseline + every .scss bundled in the addon's Themes/.
		var defaults = menu.AddMenu( "Default", "auto_awesome" );
		defaults.AddOption( new Editor.Option
		{
			Text = "Default theme",
			Icon = "auto_awesome",
			Checkable = true,
			Checked = _theme.IsDefault,
			Triggered = () => SetTheme( PreviewTheme.Default ),
		} );
		foreach ( var bundled in EnumerateBundledThemes() )
		{
			// Capture loop locals — Triggered is a closure.
			var path = bundled.Path;
			var name = bundled.Name;
			defaults.AddOption( new Editor.Option
			{
				Text = name,
				Icon = "palette",
				Checkable = true,
				Checked = string.Equals( _theme.Source, path, System.StringComparison.OrdinalIgnoreCase ),
				Triggered = () => SetTheme( PreviewTheme.FromFile( path ) ),
			} );
		}

		// Custom submenu: user-loaded .scss off disk.
		var custom = menu.AddMenu( "Custom", "folder_open" );
		custom.AddOption( "Load custom .scss…", "folder_open", LoadCustomThemeViaDialog );

		// Reload at the root, applies to whichever non-default theme is active (bundled or custom).
		if ( !_theme.IsDefault )
		{
			menu.AddSeparator();
			menu.AddOption( $"Reload {_theme.Name}", "refresh", () => SetTheme( PreviewTheme.FromFile( _theme.Source ) ) );
		}

		menu.OpenAtCursor();
	}

	// Bundled themes ship under the publishable addon at Libraries/xaz.razordesigner/Assets/Themes/.
	// Two project contexts to cover:
	//   - Published consumer:   Ident "razordesigner"           → <root>/Assets/Themes/
	//   - Dev workspace shell:  Ident "grains_razordesigner"    → <root>/Libraries/xaz.razordesigner/Assets/Themes/
	// Walked in priority order via EditorUtility.Projects.GetAll(); first existing directory wins.
	// Returns null when neither resolves — caller treats as "no bundled themes" and the Default
	// submenu shows only the engine baseline.
	private static readonly (string Ident, string Subpath)[] BundledThemeRoots = new[]
	{
		( "razordesigner",        "Assets/Themes" ),
		( "grains_razordesigner", "Libraries/xaz.razordesigner/Assets/Themes" ),
	};

	private static string BundledThemesDirectoryPath()
	{
		foreach ( var (ident, subpath) in BundledThemeRoots )
		{
			var root = System.Linq.Enumerable
				.FirstOrDefault( EditorUtility.Projects.GetAll(), p => string.Equals( p.Config?.Ident, ident, System.StringComparison.OrdinalIgnoreCase ) )
				?.GetRootPath();
			if ( string.IsNullOrEmpty( root ) ) continue;
			var dir = System.IO.Path.Combine( root, subpath );
			if ( System.IO.Directory.Exists( dir ) ) return dir;
		}
		return null;
	}

	private static IEnumerable<(string Name, string Path)> EnumerateBundledThemes()
	{
		var dir = BundledThemesDirectoryPath();
		if ( string.IsNullOrEmpty( dir ) || !System.IO.Directory.Exists( dir ) )
			yield break;
		foreach ( var f in System.IO.Directory.EnumerateFiles( dir, "*.scss" ) )
			yield return ( DisplayNameFromFile( f ), f );
	}

	// "github-dark.scss" -> "Github Dark". Cheap title-case on - and _ separators; .scss stripped.
	private static string DisplayNameFromFile( string path )
	{
		var name = System.IO.Path.GetFileNameWithoutExtension( path );
		var parts = name.Split( '-', '_' );
		for ( int i = 0; i < parts.Length; i++ )
		{
			if ( parts[i].Length == 0 ) continue;
			parts[i] = char.ToUpperInvariant( parts[i][0] ) + parts[i].Substring( 1 );
		}
		return string.Join( ' ', parts );
	}

	private void LoadCustomThemeViaDialog()
	{
		var dialog = new FileDialog( null )
		{
			Title = "Load preview theme (.scss)",
			DefaultSuffix = ".scss",
		};
		dialog.SetFindFile();
		dialog.SetModeOpen();
		dialog.SetNameFilter( "Stylesheet (*.scss)" );

		if ( !dialog.Execute() )
		{
			Log.Info( $"{LogPrefix} Theme load cancelled" );
			return;
		}

		SetTheme( PreviewTheme.FromFile( dialog.SelectedFile ) );
	}

	private void SetTheme( PreviewTheme theme )
	{
		_theme = theme ?? PreviewTheme.Default;
		EditorCookie.Set( ThemePathCookie, _theme.IsDefault ? "" : _theme.Source );
		UpdateThemeOptionTooltip();
		// Force a re-apply on the next frame even if document CSS hasn't changed.
		_lastAppliedCss = null;
		WarnOnScssOnlySyntax( _theme );
		ApplyPreviewStylesheet();
		Log.Info( $"{LogPrefix} Theme set: {_theme.Name} ({_theme.Source})" );
	}

	// We feed theme CSS to StyleSheet.FromString, which is the engine's RAW CSS parser.
	// SCSS-only constructs (nesting via &, $variables, @mixin, @include, @import) get
	// silently dropped — explaining "imported theme, nothing changed". Sniff and warn
	// so the user knows to flatten their .scss to plain CSS before loading. (grd-xq4)
	private static void WarnOnScssOnlySyntax( PreviewTheme theme )
	{
		if ( theme is null || theme.IsDefault || string.IsNullOrEmpty( theme.Css ) ) return;
		var css = theme.Css;
		var hits = new List<string>();
		// Bare `&` outside a string/comment is hard to detect cheaply; substring is good enough
		// to flag the common nesting forms.
		if ( css.Contains( "&:" ) || css.Contains( "& " ) || css.Contains( "&." ) || css.Contains( "&#" ) )
			hits.Add( "nested selectors via `&`" );
		if ( css.Contains( "@mixin" ) )    hits.Add( "@mixin" );
		if ( css.Contains( "@include" ) )  hits.Add( "@include" );
		if ( css.Contains( "@import" ) )   hits.Add( "@import" );
		if ( css.Contains( "@function" ) ) hits.Add( "@function" );
		if ( css.Contains( "@if" ) )       hits.Add( "@if" );
		if ( css.Contains( "@each" ) )     hits.Add( "@each" );
		// `$ident:` SCSS variable assignment heuristic — common pattern at file top.
		if ( System.Text.RegularExpressions.Regex.IsMatch( css, @"\$[A-Za-z_][\w-]*\s*:" ) )
			hits.Add( "$variables" );
		if ( hits.Count == 0 ) return;
		Log.Warning( $"{LogPrefix} Theme \"{theme.Name}\" contains SCSS-only syntax that will not parse: {string.Join( ", ", hits )}. Flatten to plain CSS." );
	}

	private void RestoreThemeFromCookie()
	{
		var path = EditorCookie.Get<string>( ThemePathCookie, null );
		if ( string.IsNullOrEmpty( path ) ) return;
		if ( !System.IO.File.Exists( path ) )
		{
			Log.Warning( $"{LogPrefix} Saved theme path no longer exists: {path}; staying on Default" );
			return;
		}
		_theme = PreviewTheme.FromFile( path );
	}

	private void UpdateThemeOptionTooltip()
	{
		if ( _themeOption is null ) return;
		_themeOption.ToolTip = _theme.IsDefault
			? "Theme: Default (click to change)"
			: $"Theme: {_theme.Name} (click to change)";
	}

	private void OnViewportButton()
	{
		var menu = new Editor.Menu( this );

		void AddPreset( string label, Vector2? size )
		{
			var current = _viewportLabel == label;
			menu.AddOption( new Editor.Option
			{
				Text = label,
				Icon = current ? "check" : "",
				Checkable = true,
				Checked = current,
				Triggered = () => SetViewport( size, label ),
			} );
		}

		AddPreset( "Fit", null );
		menu.AddSeparator();
		AddPreset( "1920 × 1080  (16:9)", new Vector2( 1920, 1080 ) );
		AddPreset( "1280 × 720  (16:9)", new Vector2( 1280, 720 ) );
		AddPreset( "1080 × 1920  (9:16)", new Vector2( 1080, 1920 ) );
		menu.AddSeparator();
		menu.AddOption( "Custom…", "tune", OpenCustomViewportDialog );

		menu.OpenAtCursor();
	}

	private void OpenCustomViewportDialog()
	{
		var dialog = new Editor.Dialog( this );
		dialog.Window.WindowTitle = "Custom canvas size";
		dialog.Window.SetWindowIcon( "aspect_ratio" );
		dialog.Window.SetModal( true, true );
		dialog.Window.MinimumWidth = 320;

		dialog.Layout = Layout.Column();
		dialog.Layout.Margin = 16;
		dialog.Layout.Spacing = 10;

		var inputRow = dialog.Layout.Add( Layout.Row() );
		inputRow.Spacing = 8;

		var widthEdit = new LineEdit( dialog ) { Text = ((int)(_viewport?.x ?? 1920)).ToString() };
		var heightEdit = new LineEdit( dialog ) { Text = ((int)(_viewport?.y ?? 1080)).ToString() };
		widthEdit.MinimumWidth = 80;
		heightEdit.MinimumWidth = 80;

		inputRow.Add( widthEdit, 1 );
		inputRow.Add( new Editor.Label( dialog ) { Text = "×" } );
		inputRow.Add( heightEdit, 1 );

		var hint = new Editor.Label( dialog ) { Text = "Logical pixels. Letterboxed inside the canvas pane." };
		hint.SetStyles( "color: #888; font-size: 11px;" );
		dialog.Layout.Add( hint );

		var buttonRow = dialog.Layout.Add( Layout.Row() );
		buttonRow.Spacing = 6;
		buttonRow.AddStretchCell();

		var cancel = new Editor.Button( dialog ) { Text = "Cancel", MinimumWidth = 72 };
		cancel.MouseLeftPress += () => dialog.Close();
		buttonRow.Add( cancel );

		var ok = new Editor.Button( dialog ) { Text = "Apply", MinimumWidth = 72 };
		ok.MouseLeftPress += () =>
		{
			if ( !int.TryParse( widthEdit.Text, out var w ) || w < 16 || w > 16384 )
			{
				Log.Warning( $"{LogPrefix} Custom viewport: invalid width \"{widthEdit.Text}\"" );
				return;
			}
			if ( !int.TryParse( heightEdit.Text, out var h ) || h < 16 || h > 16384 )
			{
				Log.Warning( $"{LogPrefix} Custom viewport: invalid height \"{heightEdit.Text}\"" );
				return;
			}
			SetViewport( new Vector2( w, h ), $"{w} × {h}" );
			dialog.Close();
		};
		buttonRow.Add( ok );

		dialog.Window.AdjustSize();
		dialog.Show();
	}

	// Editor.Option with Checkable=true updates Checked before firing the callback.
	private void OnChromeToggle()
	{
		_chromeHidden = _chromeHiddenOption.Checked;
		EditorCookie.Set( ChromeHiddenCookie, _chromeHidden );
		ApplyChromeVisibility();
		Log.Info( $"{LogPrefix} Chrome {(_chromeHidden ? "hidden (real view)" : "shown (scaffold)")}" );
	}

	// Toggles the .chrome-hidden class on the canvas root. The class drives a small
	// override branch in DocumentSerializer.PreviewMarkerRules that hides scaffolding
	// chrome (chrome labels, empty-image placeholder, selection outline, and the
	// stand-in baselines on .preview-buttongroup / .preview-dropdown). Real Sandbox.UI
	// controls (Panel, Form, Field, ...) carry no .preview-* class so chrome-hidden
	// leaves their user-set overrides untouched (grd-1en). Re-applied from
	// RepopulateMirror so it survives canvas rebuilds.
	private void ApplyChromeVisibility()
	{
		var root = Canvas?.DesignerScene?.Root;
		if ( root is null || !root.IsValid ) return;
		root.SetClass( ChromeHiddenClass, _chromeHidden );
	}

	private void SetViewport( Vector2? size, string label )
	{
		_viewport = size;
		_viewportLabel = label;
		EditorCookie.Set( CanvasSizeCookie,
			size.HasValue ? $"{(int)size.Value.x}x{(int)size.Value.y}" : "" );
		if ( _viewportFrame is not null )
			_viewportFrame.Viewport = size;
		UpdateViewportOptionTooltip();
		Log.Info( $"{LogPrefix} Viewport set: {label}" );
	}

	private void RestoreViewportFromCookie()
	{
		var raw = EditorCookie.Get<string>( CanvasSizeCookie, null );
		if ( string.IsNullOrEmpty( raw ) )
		{
			_viewport = null;
			_viewportLabel = "Fit";
			return;
		}
		var parts = raw.Split( 'x' );
		if ( parts.Length == 2
			&& int.TryParse( parts[0], out var w )
			&& int.TryParse( parts[1], out var h )
			&& w >= 16 && h >= 16 )
		{
			_viewport = new Vector2( w, h );
			_viewportLabel = LabelForSize( w, h );
			Log.Info( $"{LogPrefix} Viewport restored: {_viewportLabel}" );
		}
		else
		{
			Log.Warning( $"{LogPrefix} Saved viewport unparseable: \"{raw}\"; falling back to Fit" );
			_viewport = null;
			_viewportLabel = "Fit";
		}
	}

	private static string LabelForSize( int w, int h ) => (w, h) switch
	{
		(1920, 1080) => "1920 × 1080  (16:9)",
		(1280, 720) => "1280 × 720  (16:9)",
		(1080, 1920) => "1080 × 1920  (9:16)",
		_ => $"{w} × {h}",
	};

	private void UpdateViewportOptionTooltip()
	{
		if ( _viewportOption is null ) return;
		_viewportOption.ToolTip = $"Canvas: {_viewportLabel} (click to change)";
	}

	[Shortcut( "razordesigner.new", "CTRL+N", ShortcutType.Window )]
	private void ShortcutNew() => OnNew();

	[Shortcut( "razordesigner.save", "CTRL+S", ShortcutType.Window )]
	private void ShortcutSave() => OnSave();

	private void OnPaletteTypeAddRequested( ControlType type )
	{
		// Click-to-add: append under the active selection if it's a container, else under root.
		var sel = _selection?.Selected;
		ControlRecord parent = null;
		if ( sel is not null && ControlMetadata.Get( sel.Type ).IsContainer )
			parent = sel;
		Log.Info( $"{LogPrefix} Palette click-to-add {type} under {parent?.ClassName ?? "<root>"}" );
		OnHierarchyAddRequested( type, parent );
	}

	private void OnPaletteTemplateAddRequested( PaletteTemplate template )
	{
		// Click-to-add: append under the active selection if it's a container, else under root.
		var sel = _selection?.Selected;
		ControlRecord parent = null;
		if ( sel is not null && ControlMetadata.Get( sel.Type ).IsContainer )
			parent = sel;
		Log.Info( $"{LogPrefix} Palette click-to-add template \"{template.Name}\" under {parent?.ClassName ?? "<root>"}" );
		InsertTemplate( template, parent ?? _document.RootRecord );
	}

	private void OnCanvasTemplateDropped( PaletteTemplate template, Vector2 widgetPx )
	{
		Log.Info( $"{LogPrefix} OnCanvasTemplateDropped: \"{template.Name}\" at widget ({widgetPx.x:F0}, {widgetPx.y:F0})" );
		var dpi = Canvas.DpiScale;
		var fbPos = new Vector2( widgetPx.x * dpi, widgetPx.y * dpi );
		var parent = _document.FindDeepestContainerAt( fbPos );
		InsertTemplate( template, parent );
	}

	// Tree-node drop. The hierarchy already computed parent + index; we honour the index by
	// shifting clones into place after the (always-appending) AddTemplate insert.
	private void OnHierarchyTemplateDropRequested( PaletteTemplate template, ControlRecord parent, int index )
	{
		if ( parent is null ) parent = _document.RootRecord;
		Log.Info( $"{LogPrefix} OnHierarchyTemplateDropRequested: \"{template.Name}\" -> {parent.ClassName}[{index}]" );

		var clones = _document.AddTemplate( template, parent );
		if ( clones.Count == 0 ) return;

		// AddTemplate appends; shift the contiguous block of clones to the requested slot.
		var firstAppendedAt = parent.Children.Count - clones.Count;
		if ( index < firstAppendedAt )
		{
			for ( int i = 0; i < clones.Count; i++ )
				_document.MoveTo( clones[i], parent, index + i );
		}

		var focus = clones[clones.Count - 1];
		// Surgical mirror after document order is final — MirrorInsertedRecord
		// uses the post-MoveTo docIndex when re-slotting, so non-tail inserts
		// don't trigger a full canvas rebuild.
		foreach ( var clone in clones )
		{
			if ( !MirrorInsertedRecord( clone, parent ) )
			{
				RepopulateAndFocus( focus );
				return;
			}
		}
		ApplyPreviewStylesheet();
		_selection.Select( focus );
		_inspector.SetTarget( focus );
		_hierarchy.Highlight( focus );
		RefreshHierarchy();
	}

	private void OnHierarchySaveAsTemplateRequested( IReadOnlyList<ControlRecord> records )
	{
		if ( records is null || records.Count == 0 )
		{
			Log.Warning( $"{LogPrefix} OnHierarchySaveAsTemplateRequested: empty selection; ignored" );
			return;
		}

		var lca = ComputeLowestCommonAncestor( records );
		var dialog = new SaveTemplateDialog( this, _palette.TemplateStore, records, lca );
		dialog.Show( onConfirm: template =>
		{
			Log.Info( $"{LogPrefix} OnHierarchySaveAsTemplateRequested: saving \"{template.Name}\"" );
			_palette.TemplateStore.Save( template );
		} );
	}

	// Common AddTemplate path used by click-to-add and canvas drop.
	private void InsertTemplate( PaletteTemplate template, ControlRecord parent )
	{
		var actualParent = parent ?? _document.RootRecord;
		var clones = _document.AddTemplate( template, actualParent );
		if ( clones.Count == 0 ) return;

		var focus = clones[clones.Count - 1];
		// AddTemplate appends — surgical mirror keeps existing siblings' panel
		// identity. Single fallback if any parent's LivePanel went stale.
		foreach ( var clone in clones )
		{
			if ( !MirrorInsertedRecord( clone, actualParent ) )
			{
				RepopulateAndFocus( focus );
				return;
			}
		}
		ApplyPreviewStylesheet();
		_selection.Select( focus );
		_inspector.SetTarget( focus );
		_hierarchy.Highlight( focus );
		RefreshHierarchy();
	}

	// Returns the lowest common ancestor of the given records, or null when the records
	// span multiple parents (in which case wrap-in-container is meaningless and the dialog
	// disables the checkbox).
	private ControlRecord ComputeLowestCommonAncestor( IReadOnlyList<ControlRecord> records )
	{
		if ( records is null || records.Count == 0 ) return null;
		if ( records.Count == 1 ) return null; // wrap meaningless for single root

		ControlRecord shared = _document.FindParent( records[0] );
		for ( int i = 1; i < records.Count; i++ )
		{
			var p = _document.FindParent( records[i] );
			if ( p != shared ) return null; // not all siblings -> wrap not meaningful
		}
		return shared;
	}

	private void Build()
	{
		Log.Info( $"{LogPrefix} Build: rebuilding canvas" );
		// New canvas = new RootPanel = empty StyleSheet; drop caches tied to old root.
		_lastAppliedCss = null;
		_previewSheet = null;
		_canvasHost.Clear( true );

		_viewportFrame = new CanvasViewportFrame( this );
		Canvas = new DesignerCanvas( _viewportFrame );
		Canvas.CanvasClicked += OnCanvasClicked;
		Canvas.RecordDropped += OnCanvasRecordDropped;
		Canvas.TemplateDropped += OnCanvasTemplateDropped;
		_viewportFrame.Canvas = Canvas;
		_viewportFrame.Viewport = _viewport;
		_canvasHost.Add( _viewportFrame );

		RepopulateMirror();
	}

	private static bool _stylesheetParseProbeRan;
	private static void RunStylesheetParseProbe()
	{
		if ( _stylesheetParseProbeRan ) return;
		_stylesheetParseProbeRan = true;
		try
		{
			var p = new Panel();
			p.StyleSheet.Parse( ".x { width: 100px; }" );
			p.StyleSheet.Parse( ".x { width: 200px; }" );
			var sheets = System.Linq.Enumerable.ToList( p.AllStyleSheets );
			Log.Info( $"[stylesheet-parse-probe] sheets={sheets.Count} (expect 1 if fixed, 2 if leak)" );
			foreach ( var s in sheets )
				Log.Info( $"[stylesheet-parse-probe]   FileName=\"{s.FileName ?? "<null>"}\"" );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[stylesheet-parse-probe] failed: {ex.Message}" );
		}
	}

	private void ApplyPreviewStylesheet()
	{
		RunStylesheetParseProbe();

		var canvas = Canvas;
		if ( canvas is null ) return;
		var root = canvas.DesignerScene?.Root;
		if ( root is null || !root.IsValid ) return;

		var css = DocumentSerializer.GeneratePreviewStylesheet( _document, _theme );
		if ( css == _lastAppliedCss )
		{
			Log.Info( $"{LogPrefix} ApplyPreviewStylesheet ({css.Length} chars): unchanged, skipped" );
			return;
		}
		Log.Info( $"{LogPrefix} ApplyPreviewStylesheet ({css.Length} chars)" );

		if ( _previewSheet is not null )
		{
			root.StyleSheet.Remove( _previewSheet );
		}
		try
		{
			_previewSheet = StyleSheet.FromString( css, "preview" );
		}
		catch ( System.Exception ex )
		{
			// FromString throwing means at least one rule is malformed. Without this
			// catch, the whole apply call unwinds and the canvas silently keeps the
			// previous stylesheet — exactly the "imported a theme, nothing changed"
			// symptom the user reported on theme import. Re-applying the previous
			// sheet so the canvas isn't left bare; logs surface the parse error +
			// theme source for triage. (grd-xq4)
			Log.Warning( ex, $"{LogPrefix} StyleSheet.FromString failed for theme \"{_theme.Name}\" ({_theme.Source}): {ex.Message}" );
			Log.Warning( $"{LogPrefix} CSS first 400 chars: {css.Substring( 0, System.Math.Min( 400, css.Length ) )}" );
			if ( _previewSheet is not null )
				root.StyleSheet.Add( _previewSheet );
			return;
		}
		root.StyleSheet.Add( _previewSheet );

		// bd memory: stylesheet-restyle-bug. Sentinel class toggle forces BuildStyleRules walk.
		root.AddClass( ForceRestyleClass );
		root.RemoveClass( ForceRestyleClass );

		_lastAppliedCss = css;
	}

	private const string ForceRestyleClass = "__rd_force_restyle__";

	// Wipe + re-create the live mirror under root. Clears selection.
	private void RepopulateMirror()
	{
		if ( _document is null ) return;

		var root = Canvas?.DesignerScene?.Root;
		if ( root is null || !root.IsValid )
		{
			Log.Warning( $"{LogPrefix} RepopulateMirror: root not valid; skipping {_document.RootRecord.Children.Count} root child(ren)" );
			return;
		}

		_document.RootRecord.LivePanel = root;
		root.AddClass( "root" );
		root.SetClass( ChromeHiddenClass, _chromeHidden );
		root.DeleteChildren( immediate: true );

		Log.Info( $"{LogPrefix} RepopulateMirror: wiped subtree, wired RootRecord.LivePanel; re-creating {_document.RootRecord.Children.Count} root child(ren)" );

		foreach ( var r in _document.RootRecord.Children )
		{
			MirrorRecord( r, root );
		}

		_selection?.Deselect();
		_inspector?.SetTarget( null );
		ApplyPreviewStylesheet();
		RefreshHierarchy();
	}

	private void OnCanvasClicked( Vector2 widgetPx )
	{
		_selection.TrySelectAt( widgetPx.x, widgetPx.y, Canvas.DpiScale );
		_inspector.SetTarget( _selection.Selected );
		_hierarchy.Highlight( _selection.Selected );
	}

	private void OnCanvasRecordDropped( ControlType type, Vector2 widgetPx )
	{
		Log.Info( $"{LogPrefix} OnCanvasRecordDropped: {type} at widget ({widgetPx.x:F0}, {widgetPx.y:F0})" );
		var dpi = Canvas.DpiScale;
		var fbPos = new Vector2( widgetPx.x * dpi, widgetPx.y * dpi );
		var parent = _document.FindDeepestContainerAt( fbPos );
		var record = _document.Add( type, parent );
		var liveParent = parent.LivePanel ?? Canvas.DesignerScene.Root;
		MirrorRecord( record, liveParent );
		UpdateChromeLabel( parent );
		ApplyPreviewStylesheet();
		_selection.Select( record );
		_inspector.SetTarget( record );
		RefreshHierarchy();
	}

	private void OnHierarchyRecordCreated( ControlRecord record )
	{
		Log.Info( $"{LogPrefix} OnHierarchyRecordCreated: {record.ClassName}" );
		var parent = _document.FindParent( record );
		if ( !MirrorInsertedRecord( record, parent ) )
		{
			RepopulateAndFocus( record );
			return;
		}
		ApplyPreviewStylesheet();
		_selection.Select( record );
		_inspector.SetTarget( record );
		_hierarchy.Highlight( record );
		RefreshHierarchy();
	}

	private void OnHierarchyRowClicked( ControlRecord record )
	{
		_selection.Select( record );
		_inspector.SetTarget( record );
		// Don't Highlight: row is already selected by the click; would feed back via ItemSelected.
	}

	// Multi-selection passthrough. Inspector + canvas highlight stay bound to the primary
	// (RecordSelected) record. Templates v1 save flow is the first real consumer of the list form.
	private void OnHierarchySelectionChanged( IReadOnlyList<ControlRecord> records )
	{
		Log.Info( $"{LogPrefix} OnHierarchySelectionChanged: {records?.Count ?? 0} record(s)" );
	}

	private void OnInspectorClassNameChanged( ControlRecord record, string oldClassName )
	{
		if ( record is null ) return;

		Log.Info( $"{LogPrefix} OnInspectorClassNameChanged: '{oldClassName}' -> '{record.ClassName}' (surgical)" );

		var live = record.LivePanel;
		if ( live is not null && live.IsValid )
		{
			if ( !string.IsNullOrEmpty( oldClassName ) )
				live.RemoveClass( oldClassName );
			if ( !string.IsNullOrEmpty( record.ClassName ) )
				live.AddClass( record.ClassName );
		}

		if ( ControlMetadata.Get( record.Type ).IsContainer )
		{
			RefreshChromeLabelText( record );
		}

		ApplyPreviewStylesheet();
		_hierarchy?.NodeChanged( record );
	}

	// UpdateChromeLabel creates/removes; this updates the existing label's text after rename.
	private void RefreshChromeLabelText( ControlRecord record )
	{
		if ( record?.LivePanel is null || !record.LivePanel.IsValid ) return;
		foreach ( var child in record.LivePanel.Children )
		{
			if ( child.HasClass( "preview-chrome-label" ) && child is Sandbox.UI.Label lbl )
			{
				lbl.Text = record.ClassName;
				return;
			}
		}
	}

	// RepopulateMirror clears selection; re-establish focus.
	private void RepopulateAndFocus( ControlRecord record )
	{
		RepopulateMirror();
		if ( record is null ) return;
		_selection.Select( record );
		_inspector.SetTarget( record );
		_hierarchy.Highlight( record );
	}

	// Surgical seam for record insertion (grd-1rd). Mirror just `record` under its
	// document parent's LivePanel and SetChildIndex to match the document order;
	// every other live panel keeps its identity (and its transient state — Checkbox
	// is-checked, TextEntry mid-typed text, scroll positions). Returns false when
	// the parent's LivePanel isn't usable yet (e.g. parent itself isn't mirrored —
	// shouldn't happen in steady-state, but the fallback to RepopulateAndFocus
	// handles the edge case). Caller is responsible for ApplyPreviewStylesheet,
	// focus reassignment, and RefreshHierarchy after a batch of these.
	private bool MirrorInsertedRecord( ControlRecord record, ControlRecord parent )
	{
		if ( record is null || parent is null ) return false;
		var liveParent = parent.LivePanel;
		if ( liveParent is null || !liveParent.IsValid ) return false;

		MirrorRecord( record, liveParent );

		// MirrorRecord appends; re-slot when the document index isn't the last.
		// Skipping the SetChildIndex call when docIndex == last avoids a no-op
		// reorder (which would still mark the YogaNode dirty for one frame).
		var docIndex = parent.Children.IndexOf( record );
		if ( docIndex >= 0 && docIndex < parent.Children.Count - 1 && record.LivePanel is { IsValid: true } live )
			liveParent.SetChildIndex( live, docIndex );

		UpdateChromeLabel( parent );
		return true;
	}

	private void OnRecordMoved( ControlRecord moved )
	{
		Log.Info( $"{LogPrefix} OnRecordMoved: {moved?.ClassName ?? "<null>"} (surgical)" );
		if ( moved is null ) { RepopulateMirror(); return; }

		// MoveTo already mutated the document tree. Mirror state lags: moved.LivePanel
		// is still attached under the OLD parent's LivePanel at the OLD index. Re-parent
		// the live panel surgically; siblings outside the moved subtree keep their identity
		// (and thus their transient state). (grd-1rd.)
		var newParent = _document.FindParent( moved );
		var newLiveParent = newParent?.LivePanel;
		var live = moved.LivePanel;

		if ( newParent is null || newLiveParent is null || !newLiveParent.IsValid
			|| live is null || !live.IsValid )
		{
			Log.Warning( $"{LogPrefix} OnRecordMoved: surgical re-parent unavailable; falling back to RepopulateMirror" );
			RepopulateMirror();
			return;
		}

		// Sandbox.UI.Panel.Parent setter detaches from the old parent and attaches under
		// the new (engine-managed). SetChildIndex then matches the document order. The
		// no-parent-change case (sibling reorder) works the same — Parent setter is
		// idempotent when already correct, then SetChildIndex re-slots.
		live.Parent = newLiveParent;
		var docIndex = newParent.Children.IndexOf( moved );
		if ( docIndex >= 0 && docIndex < newParent.Children.Count - 1 )
			newLiveParent.SetChildIndex( live, docIndex );

		// Chrome label shows on empty containers only — both old + new parent change shape
		// when the move is across containers, so refresh both. WalkAll is cheap relative
		// to the avoided full DeleteChildren.
		foreach ( var r in _document.WalkAll() )
			UpdateChromeLabel( r );

		ApplyPreviewStylesheet();
		RefreshHierarchy();
	}

	private void OnHierarchyCutRequested( IReadOnlyList<ControlRecord> records )
	{
		if ( records is null || records.Count == 0 ) return;
		var operable = new List<ControlRecord>( records.Count );
		foreach ( var r in records )
			if ( r is not null && r != _document.RootRecord ) operable.Add( r );
		if ( operable.Count == 0 ) return;

		// Stash references first; delete after so the records still exist when stored.
		_document.Clipboard = operable;
		Log.Info( $"{LogPrefix} Cut {operable.Count} record(s) -> clipboard" );
		foreach ( var r in operable )
			DeleteRecordCascade( r );
	}

	// Clone at copy time so subsequent edits to source don't bleed into pastes.
	private void OnHierarchyCopyRequested( IReadOnlyList<ControlRecord> records )
	{
		if ( records is null || records.Count == 0 ) return;
		var snapshots = new List<ControlRecord>( records.Count );
		foreach ( var r in records )
		{
			if ( r is null || r == _document.RootRecord ) continue;
			snapshots.Add( _document.Clone( r ) );
		}
		if ( snapshots.Count == 0 ) return;
		_document.Clipboard = snapshots;
		Log.Info( $"{LogPrefix} Copy {snapshots.Count} record(s) -> clipboard" );
	}

	// Container target appends each clone as a last child; leaf target inserts as next siblings.
	// Clones land in source order, contiguous. Last clone receives focus.
	private void OnHierarchyPasteRequested( ControlRecord target )
	{
		if ( _document.Clipboard is null || _document.Clipboard.Count == 0 || target is null ) return;

		ControlRecord parent;
		int index;
		if ( ControlMetadata.Get( target.Type ).IsContainer )
		{
			parent = target;
			index = target.Children.Count;
		}
		else
		{
			parent = _document.FindParent( target ) ?? _document.RootRecord;
			index = parent.Children.IndexOf( target ) + 1;
		}

		var pastedRoots = new List<ControlRecord>( _document.Clipboard.Count );
		foreach ( var entry in _document.Clipboard )
		{
			if ( entry is null ) continue;
			var pasted = _document.Clone( entry );
			parent.Children.Insert( index, pasted );
			Log.Info( $"{LogPrefix} Paste {pasted.ClassName} into {parent.ClassName}[{index}]" );
			index++;
			pastedRoots.Add( pasted );
		}
		if ( pastedRoots.Count == 0 ) return;

		var lastPasted = pastedRoots[pastedRoots.Count - 1];
		// Surgical mirror per pasted root — siblings keep their panel identity, so
		// scroll positions / Checkbox toggles / mid-typed TextEntry text outside
		// the paste subtree don't reset.
		foreach ( var pasted in pastedRoots )
		{
			if ( !MirrorInsertedRecord( pasted, parent ) )
			{
				RepopulateAndFocus( lastPasted );
				return;
			}
		}
		ApplyPreviewStylesheet();
		_selection.Select( lastPasted );
		_inspector.SetTarget( lastPasted );
		_hierarchy.Highlight( lastPasted );
		RefreshHierarchy();
	}

	private void OnHierarchyRenameRequested( ControlRecord record, string newName )
	{
		if ( record is null ) return;
		newName = newName?.Trim() ?? "";
		if ( newName == record.ClassName ) return;

		var error = _document.ValidateClassName( newName, record );
		if ( error is not null )
		{
			Log.Warning( $"{LogPrefix} Hierarchy rename '{newName}' rejected: {error}" );
			return;
		}

		var oldName = record.ClassName;
		record.ClassName = newName;
		Log.Info( $"{LogPrefix} Hierarchy rename '{oldName}' -> '{newName}'" );

		var live = record.LivePanel;
		if ( live is not null && live.IsValid )
		{
			if ( !string.IsNullOrEmpty( oldName ) )
				live.RemoveClass( oldName );
			if ( !string.IsNullOrEmpty( newName ) )
				live.AddClass( newName );
		}

		if ( ControlMetadata.Get( record.Type ).IsContainer )
			RefreshChromeLabelText( record );

		ApplyPreviewStylesheet();
		_hierarchy.NodeChanged( record );

		// Inspector caches _priorClassName + binds a LineEdit to the property; re-target syncs both.
		if ( _selection.Selected == record )
			_inspector.SetTarget( record );
	}

	private void OnHierarchyAddRequested( ControlType type, ControlRecord parent )
	{
		var actualParent = parent ?? _document.RootRecord;
		Log.Info( $"{LogPrefix} OnHierarchyAddRequested: {type} under {actualParent.ClassName}" );

		var record = _document.Add( type, actualParent );
		var liveParent = actualParent.LivePanel ?? Canvas?.DesignerScene?.Root;
		if ( liveParent is null || !liveParent.IsValid )
		{
			RepopulateAndFocus( record );
			return;
		}

		MirrorRecord( record, liveParent );
		UpdateChromeLabel( actualParent );
		ApplyPreviewStylesheet();
		_selection.Select( record );
		_inspector.SetTarget( record );
		RefreshHierarchy();
	}

	private void OnHierarchyDeleteRequested( IReadOnlyList<ControlRecord> records )
	{
		if ( records is null || records.Count == 0 ) return;
		Log.Info( $"{LogPrefix} OnHierarchyDeleteRequested: {records.Count} record(s)" );
		foreach ( var r in records )
		{
			if ( r is null || r == _document.RootRecord ) continue;
			DeleteRecordCascade( r );
		}
	}

	// Capture parent before delete so we can re-chrome it if it becomes empty.
	private void DeleteRecordCascade( ControlRecord record )
	{
		var parent = _document.FindParent( record );
		_selection.Select( record );
		_selection.DeleteSelected();
		UpdateChromeLabel( parent );
		_inspector.SetTarget( null );
		ApplyPreviewStylesheet();
		RefreshHierarchy();
	}

	private void RefreshHierarchy()
	{
		if ( _hierarchy is null || _document is null ) return;
		_hierarchy.SetRoot( _document.RootRecord );
		_hierarchy.Highlight( _selection?.Selected );
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		base.OnKeyPress( e );
		if ( e.Key == KeyCode.Delete )
		{
			// Prefer hierarchy multi-selection (explicit) over canvas single-selection. Hierarchy
			// stays in sync via Highlight after canvas clicks, so the single-record case still
			// hits the same path.
			var multi = _hierarchy?.SelectedRecords;
			if ( multi is { Count: > 0 } )
			{
				Log.Info( $"{LogPrefix} OnKeyPress Delete: {multi.Count} record(s) from hierarchy" );
				foreach ( var r in multi )
				{
					if ( r is null || r == _document.RootRecord ) continue;
					DeleteRecordCascade( r );
				}
				e.Accepted = true;
			}
			else if ( _selection?.Selected is not null )
			{
				DeleteRecordCascade( _selection.Selected );
				e.Accepted = true;
			}
		}
	}

	// Image.SetTexture(string) is hard-wired to FileSystem.Mounted (Assets/-rooted), which rejects
	// "..". The picker (FilePathStringControlWidget) emits Path.GetRelativePath(Assets, abs) so any
	// file outside Assets/ becomes "../foo.png". We bypass Mounted: resolve to absolute via
	// Project.Current.GetAssetsPath() anchor, read bytes, build a Texture in-memory.
	private static Texture LoadImageTexture( string source )
	{
		if ( string.IsNullOrEmpty( source ) ) return null;
		try
		{
			var assetsRoot = Project.Current?.GetAssetsPath();
			if ( string.IsNullOrEmpty( assetsRoot ) )
			{
				Log.Warning( $"{LogPrefix} LoadImageTexture: Project.Current has no assets path" );
				return null;
			}

			var abs = System.IO.Path.GetFullPath( source, assetsRoot );
			if ( !System.IO.File.Exists( abs ) )
			{
				Log.Warning( $"{LogPrefix} Image source not found on disk: {abs} (from \"{source}\")" );
				return null;
			}

			using var bm = Bitmap.CreateFromBytes( System.IO.File.ReadAllBytes( abs ) );
			return bm?.ToTexture();
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"{LogPrefix} LoadImageTexture failed for \"{source}\": {e.Message}" );
			return null;
		}
	}

	// `local.base` is a PackageReference, so Button/TextEntry/NumberEntry/Checkbox/IconPanel
	// MirrorRecord runs once per panel creation; per-record content (Label.Text, Image.Source, etc.)
	// would otherwise stay frozen at creation values. ValueChanged fires reapply against the existing
	// LivePanel without rebuilding the subtree (cheap; just attribute writes). CSS reapply still needed.
	private void OnInspectorValueChanged()
	{
		var sel = _selection?.Selected;
		if ( sel is not null )
			ReapplyLiveContent( sel );
		ApplyPreviewStylesheet();
	}

	private void ReapplyLiveContent( ControlRecord record )
	{
		if ( record?.LivePanel is null || !record.LivePanel.IsValid ) return;

		switch ( record.Type )
		{
			case ControlType.Label:
				if ( record.LivePanel is Sandbox.UI.Label label ) label.Text = record.Content;
				break;
			case ControlType.Button:
				if ( record.LivePanel is Sandbox.UI.Button btn ) btn.Text = record.Content;
				break;
			case ControlType.Image:
				if ( record.LivePanel is Sandbox.UI.Image img )
				{
					var tex = LoadImageTexture( record.Source );
					img.SetClass( "preview-image-empty", tex is null );
					img.Texture = tex;
					// Sandbox.UI.Image.Texture is an auto-property — direct assignment does
					// NOT mark the panel dirty (only the engine's SetTexture(string) path
					// flips IsRenderDirty + YogaNode.MarkDirty). On replacement of an existing
					// texture the visual stays stale until some other event invalidates the
					// panel (selection class change on hierarchy reselect was the workaround).
					// MarkRenderDirty is the public mirror of IsRenderDirty=true. (grd-i70.)
					img.MarkRenderDirty();
					Log.Info( $"{LogPrefix} ReapplyLiveContent.Image '{record.ClassName}' -> Source=\"{record.Source}\" tex={(tex is null ? "null" : "ok")}" );
				}
				break;
			case ControlType.TextEntry:
				// Leave Text empty so the engine engages the .placeholder class and renders
				// the hint via .textentry.placeholder. Setting Text would suppress the hint
				// and show the value as filled-in entered text — semantic mismatch with the
				// saved razor's `placeholder="..."` attribute.
				if ( record.LivePanel is Sandbox.UI.TextEntry entry ) entry.Placeholder = record.Placeholder;
				break;
			case ControlType.Checkbox:
				if ( record.LivePanel is Sandbox.UI.Checkbox check ) check.LabelText = record.Content;
				break;
			case ControlType.IconPanel:
				if ( record.LivePanel is Sandbox.UI.IconPanel icon ) icon.Text = record.IconName;
				break;
		}
	}

	// resolve as the real Sandbox.UI types. ButtonGroup/DropDown/Form/Field/FieldControl don't
	// have wired backing types (no editor-side instantiation, or full API needs adapters), so
	// they stay as .preview-panel + .preview-{type} mirrors handled by the catch-all default.
	private void MirrorRecord( ControlRecord record, Sandbox.UI.Panel liveParent )
	{
		if ( liveParent is null || !liveParent.IsValid )
		{
			Log.Warning( $"{LogPrefix} MirrorRecord: liveParent not valid; record {record.ClassName} has no live mirror" );
			return;
		}

		Sandbox.UI.Panel live;
		switch ( record.Type )
		{
			case ControlType.Label:
				var label = liveParent.AddChild<Sandbox.UI.Label>( record.ClassName );
				label.Text = record.Content;
				live = label;
				break;

			case ControlType.Button:
				var btn = liveParent.AddChild<Sandbox.UI.Button>( record.ClassName );
				btn.Text = record.Content;
				live = btn;
				break;

			case ControlType.Image:
				var img = liveParent.AddChild<Sandbox.UI.Image>( record.ClassName );
				var tex = LoadImageTexture( record.Source );
				if ( tex is not null )
					img.Texture = tex;
				else
					img.AddClass( "preview-image-empty" );
				live = img;
				break;

			case ControlType.TextEntry:
				var entry = liveParent.AddChild<Sandbox.UI.TextEntry>( record.ClassName );
				entry.Placeholder = record.Placeholder;
				live = entry;
				break;

			case ControlType.Checkbox:
				var check = liveParent.AddChild<Sandbox.UI.Checkbox>( record.ClassName );
				check.LabelText = record.Content;
				live = check;
				break;

			case ControlType.IconPanel:
				var icon = liveParent.AddChild<Sandbox.UI.IconPanel>( record.ClassName );
				icon.Text = record.IconName;
				live = icon;
				break;

			case ControlType.Panel:
				// Real Sandbox.UI.Panel — no .preview-panel class. The PreviewTheme baseline
				// (border + dim translucent bg + min-height) is for catch-all stand-ins where
				// the engine can't render the real control; on a real Panel that baseline
				// leaks into chrome-hidden view (grd-1en) and obstructs OverrideBackground /
				// OverrideBorder. Real Panels render with the user's overrides only.
				live = liveParent.AddChild<Sandbox.UI.Panel>( record.ClassName );
				break;

			// Form/Field/FieldControl are bare Panel subclasses in the base addon — each
			// constructor just AddClass()es its canonical class (.form / .field /
			// .field-control). The base addon ships zero scss for them, so the preview
			// theme carries the visual baseline targeting those real classes (not
			// synthetic .preview-{type} stand-ins).
			case ControlType.Form:
				live = liveParent.AddChild<Sandbox.UI.Form>( record.ClassName );
				break;

			case ControlType.Field:
				live = liveParent.AddChild<Sandbox.UI.Field>( record.ClassName );
				break;

			case ControlType.FieldControl:
				live = liveParent.AddChild<Sandbox.UI.FieldControl>( record.ClassName );
				break;

			default:
				// Catch-all for types without a wired Sandbox.UI implementation. Mirror as Panel
				// with .preview-panel + a type-specific .preview-{type} class so the theme can
				// style them. Non-container leaves (e.g. DropDown) get a single child <Panel
				// class="inner"> so the theme can place a thumb / caret / fill that needs its
				// own positioned element — sbox CSS has no pseudo-elements or radial-gradient,
				// so an extra DOM node is the only way to draw circular / inset detail.
				var fallback = liveParent.AddChild<Sandbox.UI.Panel>( record.ClassName );
				fallback.AddClass( "preview-panel" );
				fallback.AddClass( $"preview-{record.Type.ToString().ToLowerInvariant()}" );
				if ( !ControlMetadata.Get( record.Type ).IsContainer )
				{
					var inner = fallback.AddChild<Sandbox.UI.Panel>();
					inner.AddClass( "inner" );
					Log.Info( $"{LogPrefix} MirrorRecord({record.ClassName}) added .inner child for non-container stand-in" );
				}
				live = fallback;
				break;
		}

		record.LivePanel = live;
		Log.Info( $"{LogPrefix} MirrorRecord({record.ClassName}) under {liveParent.ElementName ?? "root"} -> live {live.GetType().Name}" );

		foreach ( var child in record.Children )
			MirrorRecord( child, live );

		UpdateChromeLabel( record );
	}

	// Idempotent: empty non-root containers get exactly one .preview-chrome-label child.
	private void UpdateChromeLabel( ControlRecord record )
	{
		if ( record is null ) return;
		if ( record == _document.RootRecord ) return;
		if ( record.LivePanel is null || !record.LivePanel.IsValid ) return;
		if ( !ControlMetadata.Get( record.Type ).IsContainer ) return;

		Sandbox.UI.Panel existing = null;
		foreach ( var child in record.LivePanel.Children )
		{
			if ( child.HasClass( "preview-chrome-label" ) ) { existing = child; break; }
		}

		var shouldShow = record.Children.Count == 0;

		if ( shouldShow && existing is null )
		{
			var label = record.LivePanel.AddChild<Sandbox.UI.Label>();
			label.Text = record.ClassName;
			label.AddClass( "preview-chrome-label" );
		}
		else if ( !shouldShow && existing is not null )
		{
			existing.Delete();
		}
	}

	private void OnNew()
	{
		Log.Info( $"{LogPrefix} New" );
		_selection?.Deselect();
		_inspector?.SetTarget( null );
		_document.Clear();
		_trackedPath = null;
		ApplyPreviewStylesheet();
		RefreshHierarchy();
	}

	private void ImportOutOfAssetsImages()
	{
		var assetsRoot = Project.Current?.GetAssetsPath();
		if ( string.IsNullOrEmpty( assetsRoot ) )
		{
			Log.Warning( $"{LogPrefix} Auto-import skipped: Project.Current has no assets path" );
			return;
		}

		var importsDir = System.IO.Path.Combine( assetsRoot, "ImageImports" );

		foreach ( var r in _document.WalkAll() )
		{
			if ( r.Type != ControlType.Image ) continue;
			if ( string.IsNullOrEmpty( r.Source ) ) continue;

			// Inside-Assets paths don't escape; nothing to do.
			var rel = r.Source.Replace( '\\', '/' );
			if ( !rel.StartsWith( "../" ) ) continue;

			var sourceAbs = System.IO.Path.GetFullPath( r.Source, assetsRoot );
			if ( !System.IO.File.Exists( sourceAbs ) )
			{
				Log.Warning( $"{LogPrefix} Auto-import skipped (file missing): {sourceAbs}" );
				continue;
			}

			System.IO.Directory.CreateDirectory( importsDir );

			var fileName = System.IO.Path.GetFileName( sourceAbs );
			var stem = System.IO.Path.GetFileNameWithoutExtension( fileName );
			var ext = System.IO.Path.GetExtension( fileName );

			// Reuse on byte-equal collision so repeat saves of the same source don't pile up copies;
			// pick next numeric suffix when names collide but contents differ.
			var dest = System.IO.Path.Combine( importsDir, fileName );
			var n = 1;
			while ( System.IO.File.Exists( dest ) && !FilesEqual( sourceAbs, dest ) )
			{
				dest = System.IO.Path.Combine( importsDir, $"{stem}_{n}{ext}" );
				n++;
			}

			if ( !System.IO.File.Exists( dest ) )
			{
				System.IO.File.Copy( sourceAbs, dest );
				Log.Info( $"{LogPrefix} Auto-imported image: {sourceAbs} -> {dest}" );
			}
			else
			{
				Log.Info( $"{LogPrefix} Auto-import: existing identical file reused at {dest}" );
			}

			r.Source = $"ImageImports/{System.IO.Path.GetFileName( dest )}";
		}
	}

	private static bool FilesEqual( string a, string b )
	{
		try
		{
			var ia = new System.IO.FileInfo( a );
			var ib = new System.IO.FileInfo( b );
			if ( ia.Length != ib.Length ) return false;

			using var sa = ia.OpenRead();
			using var sb = ib.OpenRead();
			var bufA = new byte[8192];
			var bufB = new byte[8192];
			int read;
			while ( ( read = sa.Read( bufA, 0, bufA.Length ) ) > 0 )
			{
				var got = sb.Read( bufB, 0, read );
				if ( got != read ) return false;
				for ( int i = 0; i < read; i++ )
					if ( bufA[i] != bufB[i] ) return false;
			}
			return true;
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"{LogPrefix} FilesEqual({a}, {b}) failed: {e.Message}" );
			return false;
		}
	}

	private void OnSave()
	{
		if ( string.IsNullOrEmpty( _trackedPath ) )
		{
			var dialog = new FileDialog( null )
			{
				Title = "Save Razor Designer Output",
				DefaultSuffix = ".razor",
			};
			dialog.SetFindFile();
			dialog.SetModeSave();
			dialog.SetNameFilter( "Razor (*.razor)" );
			// SelectedFile is read-only; SelectFile prefills the name field.
			dialog.SelectFile( "MyMenu.razor" );

			if ( !dialog.Execute() )
			{
				Log.Info( $"{LogPrefix} Save cancelled" );
				return;
			}

			_trackedPath = dialog.SelectedFile;
			if ( !_trackedPath.EndsWith( ".razor", System.StringComparison.OrdinalIgnoreCase ) )
				_trackedPath += ".razor";
		}

		var className = System.IO.Path.GetFileNameWithoutExtension( _trackedPath );

		// Out-of-Assets Image sources (../foo.png) won't resolve at runtime — Image.SetTexture
		// goes through FileSystem.Mounted which is Assets/-rooted. Auto-import to Assets/ImageImports/
		// and rewrite record.Source. Inside-Assets sources are left alone.
		ImportOutOfAssetsImages();

		var razor = DocumentSerializer.GenerateRazorMarkup( _document );
		var scss = DocumentSerializer.GenerateSavedScss( _document, className, _theme );

		var scssPath = _trackedPath + ".scss";

		System.IO.File.WriteAllText( _trackedPath, razor );
		System.IO.File.WriteAllText( scssPath, scss );

		Log.Info( $"{LogPrefix} Saved -> {_trackedPath}" );
		Log.Info( $"{LogPrefix} Saved -> {scssPath}" );
	}
}
