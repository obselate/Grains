using Editor;
using static Editor.BaseItemWidget;
using Grains.RazorDesigner.Document;
using Sandbox;

namespace Grains.RazorDesigner.Hierarchy;

// One row per ControlRecord. Container records recurse via BuildChildren so
// the tree mirrors document nesting. When IsCanvasRoot is true the node is
// the document's hidden RootRecord, rendered as "Canvas" with no Cut/Copy/
// Delete affordances and no drag source.
public sealed class ControlRecordTreeNode : TreeNode<ControlRecord>
{
	private const string LogPrefix = "[Grains.RazorDesigner]";

	public bool IsCanvasRoot { get; init; }

	private readonly HierarchyPanel _hierarchy;

	public ControlRecordTreeNode( ControlRecord record, HierarchyPanel hierarchy, bool isCanvasRoot = false )
	{
		Value = record;
		_hierarchy = hierarchy;
		IsCanvasRoot = isCanvasRoot;
	}

	// ClassName intentionally NOT included: the engine drives auto-rebuild via
	// hash-change in TreeNode.Think (every ~100ms on visible nodes), and our
	// BuildChildren only AddItems without clearing first — re-running it would
	// duplicate children. ClassName edits use the surgical HierarchyPanel
	// .NodeChanged path (just a TreeView.Update repaint). Type and Children
	// .Count both go through DesignerWindow.RepopulateAndFocus which calls
	// SetRoot + clears, so the auto-rebuild path is never hit there either.
	public override int ValueHash => System.HashCode.Combine( Value?.Type, Value?.Children.Count, IsCanvasRoot );

	protected override void BuildChildren()
	{
		if ( Value is null ) return;
		foreach ( var child in Value.Children )
			AddItem( new ControlRecordTreeNode( child, _hierarchy ) );
	}

	public override bool HasChildren => Value is not null && Value.Children.Count > 0;

	public override bool OnDragStart()
	{
		if ( IsCanvasRoot || Value is null ) return false;

		var drag = new Drag( TreeView );
		drag.Data.Object = Value;
		drag.Execute();
		Log.Info( $"{LogPrefix} TreeNode.OnDragStart: {Value.ClassName}" );
		return true;
	}

	public override bool OnContextMenu()
	{
		if ( Value is null ) return false;

		var doc = _hierarchy?.Document;
		var hasClipboard = doc?.Clipboard is not null;

		// Canvas row: only Paste makes sense, and only when there's a clipboard.
		if ( IsCanvasRoot )
		{
			if ( !hasClipboard ) return false;
			var rootMenu = new Menu( TreeView );
			rootMenu.AddOption( "Paste", "content_paste", () =>
			{
				Log.Info( $"{LogPrefix} TreeNode.ContextMenu.Paste: into Canvas" );
				_hierarchy?.NotifyPasteRequested( Value );
			} );
			rootMenu.OpenAtCursor( false );
			return true;
		}

		var m = new Menu( TreeView );

		m.AddOption( "Cut", "content_cut", () =>
		{
			Log.Info( $"{LogPrefix} TreeNode.ContextMenu.Cut: {Value.ClassName}" );
			_hierarchy?.NotifyCutRequested( Value );
		} );

		m.AddOption( "Copy", "content_copy", () =>
		{
			Log.Info( $"{LogPrefix} TreeNode.ContextMenu.Copy: {Value.ClassName}" );
			_hierarchy?.NotifyCopyRequested( Value );
		} );

		var pasteOpt = m.AddOption( "Paste", "content_paste", () =>
		{
			Log.Info( $"{LogPrefix} TreeNode.ContextMenu.Paste: onto {Value.ClassName}" );
			_hierarchy?.NotifyPasteRequested( Value );
		} );
		// Disable rather than hide so users learn paste exists.
		pasteOpt.Enabled = hasClipboard;

		m.AddSeparator();

		m.AddOption( "Delete", "delete", () =>
		{
			Log.Info( $"{LogPrefix} TreeNode.ContextMenu.Delete: {Value.ClassName}" );
			_hierarchy?.NotifyDeleteRequested( Value );
		} );

		m.OpenAtCursor( false );
		return true;
	}

	public override DropAction OnDragDrop( ItemDragEvent e )
	{
		if ( Value is null ) return DropAction.Ignore;

		var doc = _hierarchy?.Document;
		if ( doc is null ) return DropAction.Ignore;

		if ( e.Data.Object is ControlRecord dragged )
		{
			if ( dragged == Value ) return DropAction.Ignore;
			if ( doc.IsDescendant( dragged, Value ) ) return DropAction.Ignore;

			if ( !TryComputeDropTarget( doc, e.DropEdge, out var newParent, out var index, out _ ) )
				return DropAction.Ignore;

			if ( !e.IsDrop ) return DropAction.Move;

			var ok = doc.MoveTo( dragged, newParent, index );
			if ( !ok ) return DropAction.Ignore;

			_hierarchy?.NotifyMoved();
			return DropAction.Move;
		}
		else if ( e.Data.Object is ControlType paletteType )
		{
			if ( !TryComputeDropTarget( doc, e.DropEdge, out var newParent, out var index, out var dropOnto ) )
				return DropAction.Ignore;

			if ( !e.IsDrop ) return DropAction.Copy;

			Log.Info( $"{LogPrefix} TreeNode.OnDragDrop create: {paletteType} -> parent={newParent.ClassName}, index={index}" );
			var newRecord = doc.Add( paletteType, newParent );
			if ( !dropOnto )
			{
				// Add always appends; shift to the intended sibling slot.
				doc.MoveTo( newRecord, newParent, index );
			}
			_hierarchy?.NotifyCreated( newRecord );
			return DropAction.Copy;
		}
		else
		{
			return DropAction.Ignore;
		}
	}

	// Drop-onto a container appends as last child; drop above/below targets
	// the sibling slot. Rejects drop-onto non-containers and sibling-of-Root.
	private bool TryComputeDropTarget( DesignerDocument doc, ItemEdge edge,
		out ControlRecord newParent, out int index, out bool dropOnto )
	{
		newParent = null;
		index = 0;
		dropOnto = !edge.HasFlag( ItemEdge.Top ) && !edge.HasFlag( ItemEdge.Bottom );

		if ( dropOnto )
		{
			if ( !ControlMetadata.Get( Value.Type ).IsContainer ) return false;
			newParent = Value;
			index = Value.Children.Count;
			return true;
		}

		if ( IsCanvasRoot ) return false;
		newParent = doc.FindParent( Value );
		if ( newParent is null ) return false;
		var targetIdx = newParent.Children.IndexOf( Value );
		index = edge.HasFlag( ItemEdge.Top ) ? targetIdx : targetIdx + 1;
		return true;
	}

	public override void OnPaint( VirtualWidget item )
	{
		PaintSelection( item );

		var r = item.Rect;
		var iconRect = new Rect( r.Left + 4, r.Top, r.Height, r.Height );
		var textRect = r;
		textRect.Left += r.Height + 8;

		var icon = ControlMetadata.Get( IsCanvasRoot ? ControlType.Layout : Value.Type ).IconName;

		Paint.SetPen( Theme.TextControl );
		Paint.DrawIcon( iconRect, icon, r.Height - 6, TextFlag.Center );

		Paint.SetDefaultFont();
		if ( IsCanvasRoot )
		{
			Paint.SetPen( Theme.TextControl );
			Paint.DrawText( textRect, "Canvas", TextFlag.LeftCenter );
		}
		else
		{
			Paint.SetPen( Theme.TextControl );
			Paint.DrawText( textRect, $"{Value.Type}  ", TextFlag.LeftCenter );

			var typeWidth = Paint.MeasureText( $"{Value.Type}  " ).x;
			textRect.Left += typeWidth;
			Paint.SetPen( Theme.TextControl.WithAlpha( 0.6f ) );
			Paint.DrawText( textRect, Value.ClassName, TextFlag.LeftCenter );
		}
	}
}
