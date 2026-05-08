using System.Collections.Generic;
using Sandbox;

namespace Grains.RazorDesigner.Document;

public sealed class DesignerDocument
{
	private const string LogPrefix = "[Grains.RazorDesigner]";

	private readonly Dictionary<ControlType, int> _counters = new();

	// Cut detaches; Copy stores a fresh clone (snapshot); Paste re-clones.
	public ControlRecord Clipboard { get; set; }

	// isRoot sentinel. Serializer/Inspector branch on this.
	public const string RootClassName = "root";

	// Hidden; never yielded by WalkAll, never deletable.
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
			Log.Info( $"{LogPrefix} Document.Remove({record.ClassName}): was root child (now {RootRecord.Children.Count} root child(ren))" );
			return true;
		}

		var parent = FindParent( record );
		if ( parent is not null && parent.Children.Remove( record ) )
		{
			DeleteLivePanelsRecursive( record );
			Log.Info( $"{LogPrefix} Document.Remove({record.ClassName}): was child of {parent.ClassName}" );
			return true;
		}

		Log.Warning( $"{LogPrefix} Document.Remove({record.ClassName}): not found in tree" );
		return false;
	}

	// `index` is the user-visible target; this method handles the post-removal shift.
	// LivePanel is not touched; caller refreshes the mirror.
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
			Log.Warning( $"{LogPrefix} Document.MoveTo({record.ClassName}): cycle. newParent {newParent.ClassName} is record or its descendant" );
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

	// Depth-first, parent before children. RootRecord is not yielded.
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

	// RootRecord itself returns null (only record without a parent).
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

	// WalkAll yields parents-before-children, so the last hit is the deepest. Seeded with RootRecord.
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

	// Deep-clone subtree. LivePanel is not cloned; caller inserts and refreshes mirror.
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

	// Walk past in-use names: a user rename (panel1 -> panel5) can occupy a slot the counter will mint later.
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
