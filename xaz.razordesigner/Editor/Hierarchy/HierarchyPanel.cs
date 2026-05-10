using System;
using System.Collections.Generic;
using Editor;
using Grains.RazorDesigner.Document;
using Sandbox;

namespace Grains.RazorDesigner.Hierarchy;

public sealed class HierarchyPanel : Widget
{
	private const string LogPrefix = "[Grains.RazorDesigner]";

	// Primary (last-clicked) selection. Drives inspector + canvas highlight.
	public Action<ControlRecord> RecordSelected;
	// Full deduped multi-selection. Fires alongside RecordSelected for multi-aware consumers
	// (e.g. palette templates v1 save flow). RootRecord is filtered out.
	public Action<IReadOnlyList<ControlRecord>> SelectionChanged;
	// Carries the moved record so the window can re-parent its LivePanel surgically
	// instead of rebuilding the whole canvas mirror. (grd-1rd.)
	public Action<ControlRecord> RecordMoved;
	// Multi-aware. List is parent-deduped and excludes RootRecord. Single-row context-menu paths
	// pass a list of one.
	public Action<IReadOnlyList<ControlRecord>> RecordsDeleteRequested;
	public Action<ControlRecord> RecordCreated;
	public Action<IReadOnlyList<ControlRecord>> RecordsCutRequested;
	public Action<IReadOnlyList<ControlRecord>> RecordsCopyRequested;
	// Multi-aware. List is parent-deduped and excludes RootRecord. Window opens
	// the SaveTemplateDialog with this set as the template's roots.
	public Action<IReadOnlyList<ControlRecord>> RecordsSaveAsTemplateRequested;
	// (template, parent, index). Window calls Document.AddTemplate then RepopulateAndFocus.
	public Action<Grains.RazorDesigner.Templates.PaletteTemplate, ControlRecord, int> TemplateDropRequested;
	// Argument may be RootRecord. Window decides insertion point.
	public Action<ControlRecord> RecordPasteRequested;
	// (record, newName). Window validates + propagates.
	public Action<ControlRecord, string> RecordRenameRequested;
	// (type, parent or null = root). Window adds + selects.
	public Action<ControlType, ControlRecord> RecordAddRequested;

	private readonly DesignerDocument _document;
	private readonly TreeView _tree;
	private readonly LineEdit _filterEdit;
	private string _filterText = "";
	// Records the user-typed filter matches OR the ancestors of those matches; used by ShouldDisplayChild.
	private readonly HashSet<ControlRecord> _filterMatches = new();
	// Guards programmatic Highlight from re-emitting via ItemSelected / ItemsSelected.
	private bool _suppressSelectionEvents;

	public HierarchyPanel( Widget parent, DesignerDocument document ) : base( parent )
	{
		_document = document;
		Log.Info( $"{LogPrefix} HierarchyPanel ctor" );
		Layout = Layout.Column();
		Layout.Margin = 0;

		var toolbar = new ToolBar( this );
		toolbar.SetIconSize( 16 );
		_filterEdit = new LineEdit( toolbar ) { PlaceholderText = "Filter..." };
		_filterEdit.TextEdited += OnFilterEdited;
		toolbar.AddWidget( _filterEdit );
		toolbar.AddOption( null, "add", OpenAddMenu ).ToolTip = "Add control to the canvas";
		Layout.Add( toolbar );

		_tree = new TreeView( this );
		_tree.MultiSelect = true;
		_tree.ItemSelected += OnTreeItemSelected;
		_tree.ItemsSelected += OnTreeItemsSelected;
		_tree.ShouldDisplayChild = ShouldDisplayNode;
		Layout.Add( _tree, 1 );
	}

	internal DesignerDocument Document => _document;
	internal void NotifyMoved( ControlRecord record ) => RecordMoved?.Invoke( record );
	internal void NotifyDeleteRequested( IReadOnlyList<ControlRecord> records ) => RecordsDeleteRequested?.Invoke( records );
	internal void NotifyCreated( ControlRecord record ) => RecordCreated?.Invoke( record );
	internal void NotifyCutRequested( IReadOnlyList<ControlRecord> records ) => RecordsCutRequested?.Invoke( records );
	internal void NotifyCopyRequested( IReadOnlyList<ControlRecord> records ) => RecordsCopyRequested?.Invoke( records );
	internal void NotifySaveAsTemplateRequested( IReadOnlyList<ControlRecord> records ) => RecordsSaveAsTemplateRequested?.Invoke( records );
	internal void NotifyTemplateDropRequested( Grains.RazorDesigner.Templates.PaletteTemplate template, ControlRecord parent, int index )
		=> TemplateDropRequested?.Invoke( template, parent, index );
	internal void NotifyPasteRequested( ControlRecord record ) => RecordPasteRequested?.Invoke( record );
	internal void NotifyRenameRequested( ControlRecord record, string newName ) => RecordRenameRequested?.Invoke( record, newName );

	// Current parent-deduped selection (RootRecord excluded). Source order matches the tree's
	// internal Selection enumeration. Right-click handlers and templates v1 query this directly.
	public IReadOnlyList<ControlRecord> SelectedRecords
	{
		get
		{
			var raw = new List<ControlRecord>();
			foreach ( var item in _tree.SelectedItems )
			{
				if ( item is ControlRecord rec )
					raw.Add( rec );
			}
			return _document.ParentDedupe( raw );
		}
	}

	private void OnTreeItemSelected( object value )
	{
		if ( _suppressSelectionEvents ) return;
		if ( value is ControlRecord rec )
		{
			Log.Info( $"{LogPrefix} Hierarchy row -> {rec.ClassName}" );
			RecordSelected?.Invoke( rec );
		}
	}

	private void OnTreeItemsSelected( object[] items )
	{
		if ( _suppressSelectionEvents ) return;
		var deduped = SelectedRecords;
		Log.Info( $"{LogPrefix} Hierarchy multi-selection: {deduped.Count} record(s)" );
		SelectionChanged?.Invoke( deduped );
	}

	// Renders `root` as the single top-level node labelled "Canvas".
	public void SetRoot( ControlRecord root )
	{
		RecomputeFilterMatches();
		_tree.Clear();
		if ( root is null )
		{
			Log.Warning( $"{LogPrefix} HierarchyPanel.SetRoot: root is null; tree is empty" );
			return;
		}
		Log.Info( $"{LogPrefix} HierarchyPanel.SetRoot: root={root.ClassName}, children={root.Children.Count}" );
		_tree.AddItem( new ControlRecordTreeNode( root, this, isCanvasRoot: true ) );
	}

	public void Highlight( ControlRecord record )
	{
		_suppressSelectionEvents = true;
		try
		{
			_tree.SelectItem( record, false );
		}
		finally
		{
			_suppressSelectionEvents = false;
		}
	}

	// Repaint after in-place mutation; OnPaint reads Value directly so no rebuild needed.
	public void NodeChanged( ControlRecord record )
	{
		if ( record is null ) return;
		// Keep filter match-set in sync with the new ClassName.
		RecomputeFilterMatches();
		_tree.Update();
	}

	private void OnFilterEdited( string text )
	{
		_filterText = text ?? "";
		RecomputeFilterMatches();
		_tree.Update();
	}

	// Build the set of records to display: each match plus its ancestor chain so they remain visible.
	private void RecomputeFilterMatches()
	{
		_filterMatches.Clear();
		if ( string.IsNullOrEmpty( _filterText ) ) return;

		var ft = _filterText.ToLowerInvariant();
		foreach ( var r in _document.WalkAll() )
		{
			var name = r.ClassName?.ToLowerInvariant() ?? "";
			var typeName = r.Type.ToString().ToLowerInvariant();
			if ( !name.Contains( ft ) && !typeName.Contains( ft ) ) continue;

			for ( var node = r; node is not null && node != _document.RootRecord; node = _document.FindParent( node ) )
				_filterMatches.Add( node );
		}
	}

	private bool ShouldDisplayNode( object item )
	{
		// TreeView passes the TreeNode here; canvas root is always visible.
		if ( item is ControlRecordTreeNode node )
		{
			if ( node.IsCanvasRoot ) return true;
			if ( string.IsNullOrEmpty( _filterText ) ) return true;
			return node.Value is ControlRecord rec && _filterMatches.Contains( rec );
		}
		return true;
	}

	private void OpenAddMenu()
	{
		var menu = new Menu( this );
		foreach ( var type in System.Enum.GetValues<ControlType>() )
		{
			var meta = ControlMetadata.Get( type );
			var captured = type;
			menu.AddOption( type.ToString(), meta.IconName, () =>
			{
				Log.Info( $"{LogPrefix} Hierarchy Add menu -> {captured}" );
				RecordAddRequested?.Invoke( captured, null );
			} );
		}
		menu.OpenAtCursor( false );
	}
}
