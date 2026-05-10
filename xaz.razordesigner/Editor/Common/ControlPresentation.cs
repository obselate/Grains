using Grains.RazorDesigner.Document;
using Sandbox;

namespace Grains.RazorDesigner.Common;

// Palette + hierarchy icon tinting. Lives outside ControlMetadata so a parallel session
// editing ControlType.cs/ControlMetadata doesn't collide with presentation tweaks.
//
// Tiers, distinct hues at similar lightness so the cue reads as "tinted" not "dim":
//   container   -> #3FA9F5 (matches SelectionRule accent)
//   leaf        -> #4DD0E1 (cyan)
//   pseudoclass -> #BA68C8 (purple-pink) — wired for the future re-introduction tier;
//                                          currently unreachable (no pseudoclass controls
//                                          in the enum).
//   template    -> #F59E0B (amber) — palette tile only. Warm hue picked to read as "user-
//                                     authored, not a built-in" against the cool blue/cyan/
//                                     purple tier — the only warm tint in the system.
public static class ControlPresentation
{
	public static readonly Color ContainerTint   = new( 0.247f, 0.663f, 0.961f ); // #3FA9F5
	public static readonly Color LeafTint        = new( 0.302f, 0.816f, 0.882f ); // #4DD0E1
	public static readonly Color PseudoclassTint = new( 0.729f, 0.408f, 0.784f ); // #BA68C8
	public static readonly Color TemplateTint    = new( 0.961f, 0.620f, 0.043f ); // #F59E0B

	public static Color IconTint( ControlType type )
	{
		// IsPseudoclass not represented in metadata yet — all current types resolve to
		// container or leaf. When pseudoclass tier comes back, add the check here.
		var meta = ControlMetadata.Get( type );
		return meta.IsContainer ? ContainerTint : LeafTint;
	}
}
