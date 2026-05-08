using Editor;
using Grains.RazorDesigner.Document;
using Sandbox;

namespace Grains.RazorDesigner.Hierarchy;

public sealed class HierarchyPanel : Widget
{
	private const string LogPrefix = "[Grains.RazorDesigner]";

	public System.Action<ControlRecord> RecordSelected;
	public System.Action RecordMoved;
	public System.Action<ControlRecord> RecordDeleteRequested;
	public System.Action<ControlRecord> RecordCreated;
	public System.Action<ControlRecord> RecordCutRequested;
	public System.Action<ControlRecord> RecordCopyRequested;
	// Argument may be RootRecord. Window decides insertion point.
	public System.Action<ControlRecord> RecordPasteRequested;

	private readonly DesignerDocument _document;
	private readonly TreeView _tree;
	// Guards programmatic Highlight from re-emitting via ItemSelected.
	private bool _suppressItemSelected;

	public HierarchyPanel( Widget parent, DesignerDocument document ) : base( parent )
	{
		_document = document;
		Log.Info( $"{LogPrefix} HierarchyPanel ctor" );
		Layout = Layout.Column();
		Layout.Margin = 0;

		_tree = new TreeView( this );
		_tree.ItemSelected += OnTreeItemSelected;
		Layout.Add( _tree, 1 );
	}

	internal DesignerDocument Document => _document;
	internal void NotifyMoved() => RecordMoved?.Invoke();
	internal void NotifyDeleteRequested( ControlRecord record ) => RecordDeleteRequested?.Invoke( record );
	internal void NotifyCreated( ControlRecord record ) => RecordCreated?.Invoke( record );
	internal void NotifyCutRequested( ControlRecord record ) => RecordCutRequested?.Invoke( record );
	internal void NotifyCopyRequested( ControlRecord record ) => RecordCopyRequested?.Invoke( record );
	internal void NotifyPasteRequested( ControlRecord record ) => RecordPasteRequested?.Invoke( record );

	private void OnTreeItemSelected( object value )
	{
		if ( _suppressItemSelected ) return;
		if ( value is ControlRecord rec )
		{
			Log.Info( $"{LogPrefix} Hierarchy row -> {rec.ClassName}" );
			RecordSelected?.Invoke( rec );
		}
	}

	// Renders `root` as the single top-level node labelled "Canvas".
	public void SetRoot( ControlRecord root )
	{
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
		_suppressItemSelected = true;
		try
		{
			_tree.SelectItem( record, false );
		}
		finally
		{
			_suppressItemSelected = false;
		}
	}

	// Repaint after in-place mutation; OnPaint reads Value directly so no rebuild needed.
	public void NodeChanged( ControlRecord record )
	{
		if ( record is null ) return;
		_tree.Update();
	}
}
