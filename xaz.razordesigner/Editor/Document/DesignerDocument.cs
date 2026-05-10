using System.Collections.Generic;
using Grains.RazorDesigner.Templates;
using Sandbox;

namespace Grains.RazorDesigner.Document;

public sealed class DesignerDocument
{
	private const string LogPrefix = "[Grains.RazorDesigner]";

	private readonly Dictionary<ControlType, int> _counters = new();

	// Cut detaches; Copy stores fresh clones (snapshot); Paste re-clones each entry.
	// Multi-selection support: the list contains 1+ parent-deduped roots in source order.
	public IReadOnlyList<ControlRecord> Clipboard { get; set; }

	// isRoot sentinel. Serializer/Inspector branch on this.
	public const string RootClassName = "root";

	// Hidden; never yielded by WalkAll, never deletable. Width/Height = 100% matches the
	// idiom in sbox's own menu addon (MainMenu.razor.scss:8-9 — `.mainmenu { width: 100%;
	// height: 100% }`). Sandbox.UI is flex-only, so a screen-filling root needs explicit
	// 100%/100% to occupy the ScreenPanel; engine `auto` collapses to content-size and
	// breaks the most-natural one-control centering case (Justify+Align with no free space).
	public ControlRecord RootRecord { get; } = new ControlRecord
	{
		Type = ControlType.Panel,
		ClassName = RootClassName,
		Direction = FlexDirection.Row,
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
			IconName = meta.DefaultIcon,
			FlexGrow = meta.DefaultFlexGrow,
			Direction = meta.DefaultDirection,
			Wrap = meta.DefaultWrap,
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

	// Deep-clones each template root via the existing Clone path (counter-mints class names,
	// auto-suffixes collisions). Inserts the clones contiguously under `parent` (or RootRecord
	// when parent is null) in source order. Returns the cloned roots so callers can pick a
	// focus target.
	public IReadOnlyList<ControlRecord> AddTemplate( PaletteTemplate template, ControlRecord parent = null )
	{
		if ( template is null )
		{
			Log.Warning( $"{LogPrefix} Document.AddTemplate: template is null; ignored" );
			return System.Array.Empty<ControlRecord>();
		}
		if ( template.Roots is null || template.Roots.Count == 0 )
		{
			Log.Warning( $"{LogPrefix} Document.AddTemplate(\"{template.Name}\"): template has no roots; ignored" );
			return System.Array.Empty<ControlRecord>();
		}

		var actualParent = parent ?? RootRecord;
		var clones = new List<ControlRecord>( template.Roots.Count );
		foreach ( var src in template.Roots )
		{
			if ( src is null ) continue;
			var clone = Clone( src );
			actualParent.Children.Add( clone );
			clones.Add( clone );
		}

		Log.Info( $"{LogPrefix} Document.AddTemplate(\"{template.Name}\") -> {clones.Count} clone(s) under {actualParent.ClassName}" );
		return clones;
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

	// Drop entries whose ancestor is also in the set. Order preserved per source enumeration.
	// RootRecord is filtered out (never operable).
	public IReadOnlyList<ControlRecord> ParentDedupe( IEnumerable<ControlRecord> records )
	{
		if ( records is null ) return System.Array.Empty<ControlRecord>();

		var ordered = new List<ControlRecord>();
		var seen = new HashSet<ControlRecord>();
		foreach ( var r in records )
		{
			if ( r is null || r == RootRecord ) continue;
			if ( seen.Add( r ) ) ordered.Add( r );
		}
		if ( ordered.Count <= 1 ) return ordered;

		var result = new List<ControlRecord>( ordered.Count );
		foreach ( var r in ordered )
		{
			var hasAncestor = false;
			for ( var p = FindParent( r ); p is not null && p != RootRecord; p = FindParent( p ) )
			{
				if ( seen.Contains( p ) ) { hasAncestor = true; break; }
			}
			if ( !hasAncestor ) result.Add( r );
		}
		Log.Info( $"{LogPrefix} ParentDedupe: {ordered.Count} -> {result.Count}" );
		return result;
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

	// Deep-clone subtree. ClassName is counter-minted (paste/AddTemplate inserts into the
	// live document, so collisions must be resolved). LivePanel is not cloned; caller
	// inserts and refreshes mirror.
	public ControlRecord Clone( ControlRecord source )
	{
		if ( source is null ) return null;

		var clone = new ControlRecord
		{
			Type      = source.Type,
			ClassName = MintClassName( source.Type ),
		};
		source.CopyFieldsTo( clone );

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

	// Returns null on success, else a short failure reason. `self` is excluded from collision check.
	public string ValidateClassName( string name, ControlRecord self )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return "empty";
		if ( name == RootClassName )
			return $"'{RootClassName}' is reserved for the canvas record";
		if ( !IsValidCssIdentifier( name ) )
			return "must start with a letter or underscore and contain only letters, digits, '_', or '-'";
		if ( CollidesWithAnyOther( name, self ) )
			return $"another record already uses the name '{name}'";
		return null;
	}

	private static bool IsValidCssIdentifier( string s )
	{
		var first = s[0];
		if ( !( char.IsLetter( first ) || first == '_' ) ) return false;
		for ( int i = 1; i < s.Length; i++ )
		{
			var c = s[i];
			if ( !( char.IsLetterOrDigit( c ) || c == '_' || c == '-' ) ) return false;
		}
		return true;
	}

	private bool CollidesWithAnyOther( string name, ControlRecord self )
	{
		if ( RootRecord != self && RootRecord.ClassName == name )
			return true;
		foreach ( var r in WalkAll() )
		{
			if ( r == self ) continue;
			if ( r.ClassName == name ) return true;
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
