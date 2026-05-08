using System.Collections.Generic;
using Sandbox;

namespace Grains.RazorDesigner.Document;

// Tree rooted at RootRecord — a hidden Layout-typed record that can never be
// deleted. Monotonic per-type counters never reuse class-name suffixes
// (deleting panel1 + adding a new Panel produces panel2).
//
// Mirror invariant: LivePanel is owned by DesignerWindow.MirrorRecord. Add
// does NOT create one; Remove and Clear DO destroy any existing live mirrors.
// Hotload-induced canvas rebuilds break LivePanel references transiently —
// the window must call RepopulateMirror after canvas reconstruction.
public sealed class DesignerDocument
{
	private const string LogPrefix = "[Grains.RazorDesigner]";

	private readonly Dictionary<ControlType, int> _counters = new();

	// Single-slot clipboard for hierarchy cut/copy/paste. Cut stores the
	// detached source; Copy stores a fresh clone (snapshot semantics — edits
	// to the source post-copy must not bleed into pastes). Paste re-clones.
	public ControlRecord Clipboard { get; set; }

	// isRoot sentinel: DocumentSerializer treats this ClassName as the
	// document root (flat .root rule, no flex-grow/shrink/basis emission);
	// InspectorPanel hides ClassName edit + flex-self props for it.
	public const string RootClassName = "root";

	// Hidden Layout-typed root; never yielded by WalkAll, never deletable,
	// never a hierarchy entry.
	public ControlRecord RootRecord { get; } = new ControlRecord
	{
		Type = ControlType.Layout,
		ClassName = RootClassName,
		Direction = FlexDirection.Column,
		Justify = JustifyContent.Start,
		Align = AlignItems.Stretch,
		Gap = 0f,
		Padding = Length.Px( 0 ),
		Wrap = FlexWrap.NoWrap,
		Width = Length.Percent( 100 ),
		Height = Length.Percent( 100 ),
		FlexGrow = 0f,
		FlexBasis = Length.Auto,
	};

	public ControlRecord Add( ControlType type, ControlRecord parent = null )
	{
		var meta = ControlMetadata.Get( type );

		var record = new ControlRecord
		{
			Type = type,
			ClassName = MintClassName( type ),
			Width = meta.DefaultWidth,
			Height = meta.DefaultHeight,
			Content = meta.DefaultContent,
			FlexGrow = meta.DefaultFlexGrow,
		};

		if ( parent is null )
		{
			RootRecord.Children.Add( record );
			Log.Info( $"{LogPrefix} Document.Add({type}) -> {record.ClassName} (under root; now {RootRecord.Children.Count} root child(ren))" );
		}
		else
		{
			parent.Children.Add( record );
			Log.Info( $"{LogPrefix} Document.Add({type}) -> {record.ClassName} child of {parent.ClassName} (now {parent.Children.Count} child(ren))" );
		}
		return record;
	}

	public bool Remove( ControlRecord record )
	{
		if ( record is null ) return false;

		if ( RootRecord.Children.Remove( record ) )
		{
			DeleteLivePanelsRecursive( record );
			Log.Info( $"{LogPrefix} Document.Remove({record.ClassName}) — was root child (now {RootRecord.Children.Count} root child(ren))" );
			return true;
		}

		var parent = FindParent( record );
		if ( parent is not null && parent.Children.Remove( record ) )
		{
			DeleteLivePanelsRecursive( record );
			Log.Info( $"{LogPrefix} Document.Remove({record.ClassName}) — was child of {parent.ClassName}" );
			return true;
		}

		Log.Warning( $"{LogPrefix} Document.Remove({record.ClassName}) — not found in tree" );
		return false;
	}

	// Same-parent reorder and cross-parent reparent are the same operation.
	// Index is interpreted in the AFTER-removal list, so passing the original
	// index of a later-positioned record yields a no-op move; callers should
	// pass the user-visible target index — this method handles the shift.
	// LivePanel is NOT touched; caller refreshes the live mirror.
	public bool MoveTo( ControlRecord record, ControlRecord newParent, int index )
	{
		if ( record is null )
		{
			Log.Warning( $"{LogPrefix} Document.MoveTo: record is null; ignored" );
			return false;
		}
		if ( record == RootRecord )
		{
			Log.Warning( $"{LogPrefix} Document.MoveTo: cannot move RootRecord" );
			return false;
		}
		if ( newParent is null )
		{
			Log.Warning( $"{LogPrefix} Document.MoveTo({record.ClassName}): newParent is null" );
			return false;
		}
		if ( !ControlMetadata.Get( newParent.Type ).IsContainer )
		{
			Log.Warning( $"{LogPrefix} Document.MoveTo({record.ClassName}): {newParent.ClassName} ({newParent.Type}) is not a container" );
			return false;
		}
		if ( newParent == record || IsDescendant( record, newParent ) )
		{
			Log.Warning( $"{LogPrefix} Document.MoveTo({record.ClassName}): cycle — newParent {newParent.ClassName} is record or its descendant" );
			return false;
		}

		var oldParent = FindParent( record );
		if ( oldParent is null )
		{
			Log.Warning( $"{LogPrefix} Document.MoveTo({record.ClassName}): not found in tree" );
			return false;
		}

		var oldIndex = oldParent.Children.IndexOf( record );
		oldParent.Children.RemoveAt( oldIndex );

		if ( oldParent == newParent && index > oldIndex ) index--;
		index = System.Math.Clamp( index, 0, newParent.Children.Count );

		newParent.Children.Insert( index, record );

		Log.Info( $"{LogPrefix} Document.MoveTo({record.ClassName}) {oldParent.ClassName}[{oldIndex}] -> {newParent.ClassName}[{index}]" );
		return true;
	}

	public bool IsDescendant( ControlRecord ancestor, ControlRecord candidate )
	{
		if ( ancestor is null || candidate is null ) return false;
		if ( ancestor == candidate ) return true;
		foreach ( var c in ancestor.Children )
		{
			if ( IsDescendant( c, candidate ) ) return true;
		}
		return false;
	}

	public void Clear()
	{
		foreach ( var r in WalkAll() )
		{
			r.LivePanel?.Delete();
			r.LivePanel = null;
		}
		RootRecord.Children.Clear();
		_counters.Clear();
		Log.Info( $"{LogPrefix} Document.Clear (counters reset; RootRecord retained)" );
	}

	// Depth-first, parent before children. RootRecord is NOT yielded.
	public IEnumerable<ControlRecord> WalkAll()
	{
		foreach ( var r in RootRecord.Children )
		{
			yield return r;
			foreach ( var nested in WalkSubtree( r ) )
				yield return nested;
		}
	}

	private static IEnumerable<ControlRecord> WalkSubtree( ControlRecord parent )
	{
		foreach ( var c in parent.Children )
		{
			yield return c;
			foreach ( var nested in WalkSubtree( c ) )
				yield return nested;
		}
	}

	// Direct children of RootRecord return RootRecord. RootRecord itself
	// returns null — it's the only record without a parent.
	public ControlRecord FindParent( ControlRecord child )
	{
		if ( child is null || child == RootRecord ) return null;
		if ( RootRecord.Children.Contains( child ) )
			return RootRecord;
		foreach ( var top in RootRecord.Children )
		{
			if ( top.Children.Contains( child ) ) return top;
			var nested = FindParentInSubtree( top, child );
			if ( nested is not null ) return nested;
		}
		return null;
	}

	private static ControlRecord FindParentInSubtree( ControlRecord parent, ControlRecord target )
	{
		foreach ( var c in parent.Children )
		{
			if ( c.Children.Contains( target ) ) return c;
			var nested = FindParentInSubtree( c, target );
			if ( nested is not null ) return nested;
		}
		return null;
	}

	// Returns the deepest container whose LivePanel contains fbPos. Seeded
	// with RootRecord so the result is always non-null even when no inner
	// container matches. WalkAll yields parents before children, so the
	// last hit is the deepest.
	public ControlRecord FindDeepestContainerAt( Vector2 fbPos )
	{
		ControlRecord deepest = RootRecord;
		foreach ( var r in WalkAll() )
		{
			if ( !ControlMetadata.Get( r.Type ).IsContainer ) continue;
			if ( r.LivePanel is null || !r.LivePanel.IsValid ) continue;
			if ( !r.LivePanel.IsInside( fbPos ) ) continue;
			deepest = r;
		}
		return deepest;
	}

	// Deep-clone the subtree. LivePanel is NOT cloned. Caller inserts into
	// the tree and refreshes the mirror.
	public ControlRecord Clone( ControlRecord source )
	{
		if ( source is null ) return null;

		var clone = new ControlRecord
		{
			Type       = source.Type,
			ClassName  = MintClassName( source.Type ),
			Width      = source.Width,
			Height     = source.Height,
			Content    = source.Content,
			Direction  = source.Direction,
			Justify    = source.Justify,
			Align      = source.Align,
			Gap        = source.Gap,
			Padding    = source.Padding,
			Wrap       = source.Wrap,
			FlexGrow   = source.FlexGrow,
			FlexShrink = source.FlexShrink,
			FlexBasis  = source.FlexBasis,
		};

		foreach ( var c in source.Children )
			clone.Children.Add( Clone( c ) );

		return clone;
	}

	// User-renamed records can occupy slots the monotonic counter would
	// otherwise mint (rename panel1 -> panel5 with no other panels; counter
	// eventually hits 5 and collides). Walk past in-use names.
	private string MintClassName( ControlType type )
	{
		var prefix = ControlMetadata.ClassNamePrefix( type );
		_counters.TryGetValue( type, out var count );
		string candidate;
		do
		{
			count++;
			candidate = $"{prefix}{count}";
		} while ( IsClassNameInUse( candidate ) );
		_counters[type] = count;
		return candidate;
	}

	private bool IsClassNameInUse( string className )
	{
		if ( RootRecord.ClassName == className ) return true;
		foreach ( var r in WalkAll() )
		{
			if ( r.ClassName == className ) return true;
		}
		return false;
	}

	private static void DeleteLivePanelsRecursive( ControlRecord r )
	{
		foreach ( var c in r.Children )
			DeleteLivePanelsRecursive( c );
		r.LivePanel?.Delete();
		r.LivePanel = null;
	}
}
