using Editor;
using Grains.RazorDesigner.Canvas;
using Grains.RazorDesigner.Document;
using Grains.RazorDesigner.Hierarchy;
using Grains.RazorDesigner.Inspector;
using Grains.RazorDesigner.Palette;
using Grains.RazorDesigner.Selection;
using Grains.RazorDesigner.Serialization;
using Sandbox;
using Sandbox.UI;

namespace Grains.RazorDesigner;

[Dock( "Editor", "Razor Designer", "brush" )]
public class DesignerWindow : Widget
{
	private const string LogPrefix = "[Grains.RazorDesigner]";

	private Layout _canvasHost;
	private PalettePanel _palette;
	private HierarchyPanel _hierarchy;
	private InspectorPanel _inspector;
	private DesignerDocument _document;
	private SelectionController _selection;
	private string _trackedPath;
	// Cached CSS string from the last successful StyleSheet.Parse so we can
	// skip the engine-side reconcile when nothing about the document state
	// affects the stylesheet (e.g. Content edits, which are razor markup not
	// CSS). Measured: 3-second typing burst into Content fired 110 identical
	// 1479-char applies; the parse cost is the only meaningful work, and on
	// identical input the result is a no-op.
	private string _lastAppliedCss;
	// Reference to the sheet we last attached to root, kept so we can
	// Remove(sheet) by reference next Apply. We CANNOT use root.StyleSheet.Parse
	// here: Parse calls Remove("string") which filters by `x.FileName != null
	// && x.FileName.WildcardMatch(...)`, but StyleSheet.FromString never sets
	// FileName (only FromFile does — see engine StyleSheet.cs vs
	// StyleParser.Sheet.cs). So Parse's Remove is a no-op on FromString sheets,
	// and every Apply leaks the previous sheet. Five Applies on .button1 →
	// five .button1 blocks all matching, all with the same within-sheet
	// LoadOrder, and StyleOrderer's sort is unstable on tied scores — so the
	// "winning" rule for cascade is essentially random. Tracking by reference
	// + Remove(StyleSheet) skips the FileName check entirely.
	private StyleSheet _previewSheet;

	public DesignerWindow( Widget parent ) : base( parent )
	{
		Log.Info( $"{LogPrefix} DesignerWindow ctor" );

		Layout = Layout.Column();
		Layout.Margin = 0;
		Layout.Spacing = 0;

		var toolBar = new ToolBar( this, "RazorDesignerToolBar" );
		toolBar.SetIconSize( 16 );
		toolBar.AddOption( null, "add",     () => OnNew() ).ToolTip = "New design (clears canvas)";
		toolBar.AddOption( null, "save",    () => OnSave() ).ToolTip = "Save design (.razor + .razor.scss)";
		toolBar.AddOption( null, "refresh", () => Refresh() ).ToolTip = "Refresh preview (also fires automatically on code reload)";
		Layout.Add( toolBar );

		var splitter = new Splitter( this );
		splitter.IsHorizontal = true;
		Layout.Add( splitter, 1 );

		_palette = new PalettePanel( this );
		splitter.AddWidget( _palette );

		_document = new DesignerDocument();

		_hierarchy = new HierarchyPanel( this, _document );
		_hierarchy.MaximumWidth = 220;
		splitter.AddWidget( _hierarchy );

		var canvasContainer = new Widget( this );
		canvasContainer.Layout = Layout.Column();
		canvasContainer.SetSizeMode( SizeMode.CanGrow, SizeMode.CanGrow );
		canvasContainer.MinimumWidth = 320;
		_canvasHost = canvasContainer.Layout;
		splitter.AddWidget( canvasContainer );

		_inspector = new InspectorPanel( this, _document );
		_inspector.ValueChanged += () => ApplyPreviewStylesheet();
		_inspector.ClassNameChanged += OnInspectorClassNameChanged;
		_inspector.MaximumWidth = 280;
		splitter.AddWidget( _inspector );

		splitter.SetStretch( 0, 0 );
		splitter.SetStretch( 1, 1 );
		splitter.SetStretch( 2, 6 );
		splitter.SetStretch( 3, 1 );
		splitter.SetCollapsible( 2, false );

		_selection = new SelectionController( _document );
		_hierarchy.RecordSelected += OnHierarchyRowClicked;
		_hierarchy.RecordMoved += OnRecordMoved;
		_hierarchy.RecordDeleteRequested += OnHierarchyDeleteRequested;
		_hierarchy.RecordCreated += OnHierarchyRecordCreated;
		_hierarchy.RecordCutRequested += OnHierarchyCutRequested;
		_hierarchy.RecordCopyRequested += OnHierarchyCopyRequested;
		_hierarchy.RecordPasteRequested += OnHierarchyPasteRequested;

		// FocusMode enum has no StrongFocus; TabOrClickOrWheel is the equivalent
		// and is required for the dock to receive Delete keypresses.
		FocusMode = FocusMode.TabOrClickOrWheel;

		Build();
	}

	public DesignerCanvas Canvas { get; private set; }

	[EditorEvent.Hotload]
	public void Refresh()
	{
		Log.Info( $"{LogPrefix} === Refresh ===" );
		Build();
	}

	private void Build()
	{
		Log.Info( $"{LogPrefix} Build: rebuilding canvas" );
		// New canvas = new RootPanel = empty StyleSheet. The cached CSS
		// belongs to the old root, so the next ApplyPreviewStylesheet must
		// actually parse, not early-exit on string equality. Also drop the
		// tracked sheet — it was attached to the old root.
		_lastAppliedCss = null;
		_previewSheet = null;
		_canvasHost.Clear( true );

		Canvas = new DesignerCanvas( this );
		Canvas.CanvasClicked += OnCanvasClicked;
		Canvas.RecordDropped += OnCanvasRecordDropped;
		_canvasHost.Add( Canvas );

		RepopulateMirror();
	}

	// StyleSheet.Parse REPLACES the existing rule set, so re-parsing the full
	// document each time is correct — no clear step needed.
	private void ApplyPreviewStylesheet()
	{
		var canvas = Canvas;
		if ( canvas is null ) return;
		var root = canvas.DesignerScene?.Root;
		if ( root is null || !root.IsValid ) return;

		var css = DocumentSerializer.GeneratePreviewStylesheet( _document );
		if ( css == _lastAppliedCss )
		{
			Log.Info( $"{LogPrefix} ApplyPreviewStylesheet ({css.Length} chars) — unchanged, skipped" );
			return;
		}
		Log.Info( $"{LogPrefix} ApplyPreviewStylesheet ({css.Length} chars)" );

		if ( _previewSheet is not null )
		{
			root.StyleSheet.Remove( _previewSheet );
		}
		_previewSheet = StyleSheet.FromString( css, "preview" );
		root.StyleSheet.Add( _previewSheet );

		// Remove+Add only invalidate broadphase on descendants (null StyleBlocks)
		// and dirty the OWNER's PanelStyle. They do NOT enqueue descendants for
		// RootPanel.BuildStyleRules — without that, PanelStyle.activeRules on
		// each descendant never refreshes, so value-only rule changes (e.g.
		// width: 200px → width: 80px on .button1) never reach their YogaNode.
		// Visible symptom: edits don't apply until the user adds/removes a
		// class on the panel (e.g. .selected), because Panel.AddClass calls
		// StyleSelectorsChanged(true,true) which IS the missing enqueue.
		// Toggling a sentinel class on root reaches the same path for the
		// whole tree (StyleSelectorsChanged walks descendants when
		// descendants:true). The class is removed before next layout so it
		// never affects rule matching.
		root.AddClass( ForceRestyleClass );
		root.RemoveClass( ForceRestyleClass );

		_lastAppliedCss = css;
	}

	private const string ForceRestyleClass = "__rd_force_restyle__";

	// Wipe + re-create the live mirror under root. Document.MoveTo mutates
	// Children lists but doesn't touch LivePanel; without DeleteChildren here,
	// every reorder layers new panels on top of stale ones. Clears selection.
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
		RepopulateAndFocus( record );
	}

	private void OnHierarchyRowClicked( ControlRecord record )
	{
		_selection.Select( record );
		_inspector.SetTarget( record );
		// Tree row is already highlighted by the user click — calling
		// _hierarchy.Highlight here would feed back into ItemSelected.
	}

	// Surgical ClassName commit: swap the live panel's CSS class, refresh the
	// chrome label (which mirrors ClassName for empty containers), re-apply the
	// preview stylesheet (selector text changed → hash-guard won't early-exit),
	// and trigger a hierarchy repaint. Skips RepopulateMirror entirely — saves
	// 2 MirrorRecord + 3 SetTarget + 3 LengthControlWidget ctors per rename per
	// VALIDATION-PASS-2 measurement.
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

		// Container chrome label displays ClassName when the container is
		// empty — UpdateChromeLabel is idempotent and re-creates the label
		// with the new text.
		if ( ControlMetadata.Get( record.Type ).IsContainer )
		{
			RefreshChromeLabelText( record );
		}

		ApplyPreviewStylesheet();
		_hierarchy?.NodeChanged( record );
	}

	// UpdateChromeLabel only creates/removes the marker; it doesn't update the
	// text on an existing label. After a rename we need the existing label to
	// pick up the new ClassName.
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

	// RepopulateMirror clears selection. Re-establish focus on `record`.
	private void RepopulateAndFocus( ControlRecord record )
	{
		RepopulateMirror();
		if ( record is null ) return;
		_selection.Select( record );
		_inspector.SetTarget( record );
		_hierarchy.Highlight( record );
	}

	private void OnRecordMoved()
	{
		Log.Info( $"{LogPrefix} OnRecordMoved: rebuilding mirror + tree + stylesheet" );
		RepopulateMirror();
	}

	private void OnHierarchyCutRequested( ControlRecord record )
	{
		if ( record is null || record == _document.RootRecord ) return;
		_document.Clipboard = record;
		Log.Info( $"{LogPrefix} Cut {record.ClassName} -> clipboard" );
		DeleteRecordCascade( record );
	}

	// Snapshot at copy time: subsequent edits to the source must not bleed
	// into pastes, so store a fresh clone (not a live reference).
	private void OnHierarchyCopyRequested( ControlRecord record )
	{
		if ( record is null || record == _document.RootRecord ) return;
		_document.Clipboard = _document.Clone( record );
		Log.Info( $"{LogPrefix} Copy {record.ClassName} -> clipboard" );
	}

	// Container target -> append as last child; leaf target -> next sibling.
	// Re-clones each paste so each produces a distinct subtree.
	private void OnHierarchyPasteRequested( ControlRecord target )
	{
		if ( _document.Clipboard is null || target is null ) return;

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

		var pasted = _document.Clone( _document.Clipboard );
		parent.Children.Insert( index, pasted );
		Log.Info( $"{LogPrefix} Paste {pasted.ClassName} into {parent.ClassName}[{index}]" );
		RepopulateAndFocus( pasted );
	}

	private void OnHierarchyDeleteRequested( ControlRecord record )
	{
		if ( record is null ) return;
		DeleteRecordCascade( record );
	}

	// Capture parent BEFORE delete so we can re-chrome it if it becomes empty.
	// FindParent returns null only for RootRecord, which DeleteSelected rejects.
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
			if ( _selection?.Selected is not null )
			{
				DeleteRecordCascade( _selection.Selected );
				e.Accepted = true;
			}
		}
	}

	// Sandbox.UI.Button and Sandbox.UI.TextEntry live in the `base` addon, which
	// isn't loaded into the editor process where tool addons run. Saved .razor
	// emits real <button>/<textentry> tags via DocumentSerializer; preview
	// substitutes Label with a `.preview-*` marker class for visual identity.
	private void MirrorRecord( ControlRecord record, Sandbox.UI.Panel liveParent )
	{
		if ( liveParent is null || !liveParent.IsValid )
		{
			Log.Warning( $"{LogPrefix} MirrorRecord: liveParent not valid — record {record.ClassName} has no live mirror" );
			return;
		}

		Sandbox.UI.Panel live;
		switch ( record.Type )
		{
			case ControlType.Label:
				var label = liveParent.AddChild<Sandbox.UI.Label>( record.ClassName );
				label.Text = record.Content;
				label.AddClass( "preview-label" );
				live = label;
				break;

			case ControlType.Button:
				var btnLabel = liveParent.AddChild<Sandbox.UI.Label>( record.ClassName );
				btnLabel.Text = record.Content;
				btnLabel.AddClass( "preview-button" );
				live = btnLabel;
				break;

			case ControlType.Image:
				var img = liveParent.AddChild<Sandbox.UI.Image>( record.ClassName );
				if ( !string.IsNullOrEmpty( record.Content ) )
					img.SetTexture( record.Content );
				else
					img.AddClass( "preview-image-empty" );
				live = img;
				break;

			case ControlType.TextEntry:
				var entryLabel = liveParent.AddChild<Sandbox.UI.Label>( record.ClassName );
				entryLabel.Text = record.Content;
				entryLabel.AddClass( "preview-textentry" );
				live = entryLabel;
				break;

			case ControlType.Layout:
				var layoutPanel = liveParent.AddChild<Sandbox.UI.Panel>( record.ClassName );
				layoutPanel.AddClass( "preview-layout" );
				live = layoutPanel;
				break;

			case ControlType.Panel:
			default:
				var panel = liveParent.AddChild<Sandbox.UI.Panel>( record.ClassName );
				panel.AddClass( "preview-panel" );
				live = panel;
				break;
		}

		record.LivePanel = live;
		Log.Info( $"{LogPrefix} MirrorRecord({record.ClassName}) under {liveParent.ElementName ?? "root"} -> live {live.GetType().Name}" );

		foreach ( var child in record.Children )
			MirrorRecord( child, live );

		UpdateChromeLabel( record );
	}

	// Idempotent: ensures `record`'s LivePanel has exactly one
	// `.preview-chrome-label` child iff it is a non-root container with no
	// record children. Empty containers show their ClassName so designers can
	// match preview boxes to hierarchy entries.
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
			// SelectedFile is read-only; SelectFile() prefills the name field.
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
		var razor = DocumentSerializer.GenerateRazorMarkup( _document );
		var scss = DocumentSerializer.GenerateSavedScss( _document, className );

		var scssPath = _trackedPath + ".scss";

		System.IO.File.WriteAllText( _trackedPath, razor );
		System.IO.File.WriteAllText( scssPath, scss );

		Log.Info( $"{LogPrefix} Saved -> {_trackedPath}" );
		Log.Info( $"{LogPrefix} Saved -> {scssPath}" );
	}
}
