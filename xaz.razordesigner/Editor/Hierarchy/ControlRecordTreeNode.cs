using System.Collections.Generic;
using System.Linq;
using Editor;
using static Editor.BaseItemWidget;
using Grains.RazorDesigner.Common;
using Grains.RazorDesigner.Document;
using Sandbox;

namespace Grains.RazorDesigner.Hierarchy;

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

	// ClassName excluded: engine TreeNode.Think auto-rebuilds on hash change without clearing children first; BuildChildren would duplicate. Repaint via HierarchyPanel.NodeChanged instead.
	public override int ValueHash => System.HashCode.Combine( Value?.Type, Value?.Children.Count, IsCanvasRoot );

	public override bool CanEdit => !IsCanvasRoot && Value is not null;

	// Engine TreeView reads this to seed the F2 rename popup. Setter is unused; OnRename owns the write path.
	public override string Name
	{
		get => Value?.ClassName ?? "";
		set { /* see OnRename */ }
	}

	public override void OnRename( VirtualWidget item, string text, List<TreeNode> selection = null )
	{
		if ( Value is null ) return;
		Log.Info( $"{LogPrefix} TreeNode.OnRename: {Value.ClassName} -> {text}" );
		_hierarchy?.NotifyRenameRequested( Value, text );
	}

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

		// Right-click on a row that's part of the multi-selection operates on the whole
		// selection (parent-deduped). Right-click on an unselected row operates on Value alone —
		// matches standard file-manager / IDE conventions.
		var multi = _hierarchy?.SelectedRecords;
		IReadOnlyList<ControlRecord> targets;
		if ( multi is { Count: > 1 } && multi.Contains( Value ) )
			targets = multi;
		else
			targets = new[] { Value };

		var suffix = targets.Count > 1 ? $" ({targets.Count} items)" : "";

		var m = new Menu( TreeView );

		m.AddOption( $"Save as Template{suffix}…", "bookmark_add", () =>
		{
			Log.Info( $"{LogPrefix} TreeNode.ContextMenu.SaveAsTemplate: {targets.Count} record(s)" );
			_hierarchy?.NotifySaveAsTemplateRequested( targets );
		} );
		m.AddSeparator();

		m.AddOption( $"Cut{suffix}", "content_cut", () =>
		{
			Log.Info( $"{LogPrefix} TreeNode.ContextMenu.Cut: {targets.Count} record(s)" );
			_hierarchy?.NotifyCutRequested( targets );
		} );

		m.AddOption( $"Copy{suffix}", "content_copy", () =>
		{
			Log.Info( $"{LogPrefix} TreeNode.ContextMenu.Copy: {targets.Count} record(s)" );
			_hierarchy?.NotifyCopyRequested( targets );
		} );

		var pasteOpt = m.AddOption( "Paste", "content_paste", () =>
		{
			Log.Info( $"{LogPrefix} TreeNode.ContextMenu.Paste: onto {Value.ClassName}" );
			_hierarchy?.NotifyPasteRequested( Value );
		} );
		// Disable rather than hide so users learn paste exists.
		pasteOpt.Enabled = hasClipboard;

		m.AddSeparator();

		m.AddOption( $"Delete{suffix}", "delete", () =>
		{
			Log.Info( $"{LogPrefix} TreeNode.ContextMenu.Delete: {targets.Count} record(s)" );
			_hierarchy?.NotifyDeleteRequested( targets );
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

			if ( !TryComputeDropTarget( doc, e, out var newParent, out var index, out _ ) )
				return DropAction.Ignore;

			if ( !e.IsDrop ) return DropAction.Move;

			var ok = doc.MoveTo( dragged, newParent, index );
			if ( !ok ) return DropAction.Ignore;

			_hierarchy?.NotifyMoved( dragged );
			return DropAction.Move;
		}
		else if ( e.Data.Object is ControlType paletteType )
		{
			if ( !TryComputeDropTarget( doc, e, out var newParent, out var index, out var dropOnto ) )
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
		else if ( e.Data.Object is Grains.RazorDesigner.Templates.PaletteTemplate template )
		{
			if ( !TryComputeDropTarget( doc, e, out var newParent, out var index, out _ ) )
				return DropAction.Ignore;

			if ( !e.IsDrop ) return DropAction.Copy;

			Log.Info( $"{LogPrefix} TreeNode.OnDragDrop template: \"{template.Name}\" -> parent={newParent.ClassName}, index={index}" );
			_hierarchy?.NotifyTemplateDropRequested( template, newParent, index );
			return DropAction.Copy;
		}
		else
		{
			return DropAction.Ignore;
		}
	}

	// Drop-onto container appends; drop above/below targets sibling slot. For non-container
	// rows we widen the reorder hitbox: engine reports only top/bottom 5px as Edge.Top/Bottom,
	// leaving a tiny zone — instead, treat the row's whole upper half as Top and lower half as
	// Bottom when dropping on a leaf. Containers keep their middle as drop-into.
	private bool TryComputeDropTarget( DesignerDocument doc, ItemDragEvent e,
		out ControlRecord newParent, out int index, out bool dropOnto )
	{
		newParent = null;
		index = 0;

		var edge = e.DropEdge;
		var isMiddle = !edge.HasFlag( ItemEdge.Top ) && !edge.HasFlag( ItemEdge.Bottom );
		var isContainer = Value is not null && ControlMetadata.Get( Value.Type ).IsContainer;

		// Non-container leaf with cursor in middle: synthesize Top/Bottom from cursor Y so
		// the entire row counts as a reorder zone. Canvas root is a container so it's skipped.
		if ( isMiddle && !isContainer && !IsCanvasRoot )
		{
			var halfH = e.Item.Rect.Height * 0.5f;
			edge = e.LocalPosition.y < halfH ? ItemEdge.Top : ItemEdge.Bottom;
			isMiddle = false;
		}

		dropOnto = isMiddle;

		if ( dropOnto )
		{
			if ( !isContainer ) return false;
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

		var iconType = IsCanvasRoot ? ControlType.Panel : Value.Type;
		var icon = ControlMetadata.Get( iconType ).IconName;

		// Canvas root falls back to the container tint regardless of whether the seed type
		// (Panel here) ends up classified differently in the future.
		Paint.SetPen( IsCanvasRoot ? ControlPresentation.ContainerTint : ControlPresentation.IconTint( iconType ) );
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
