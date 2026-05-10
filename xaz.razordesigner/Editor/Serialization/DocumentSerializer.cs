using System.Globalization;
using System.Text;
using Grains.RazorDesigner.Document;

namespace Grains.RazorDesigner.Serialization;

public static class DocumentSerializer
{
    private const int SchemaVersion = 2;

    // Sandbox.UI outline shorthand only accepts 'solid'.
    private const string SelectionRule =
        ".selected { outline: 2px solid #3FA9F5; }";

    // Preview-only; NOT emitted into saved .razor.scss. Engine chrome only — anything that
    // must apply regardless of the loaded PreviewTheme. .preview-panel keeps only structural
    // position:relative so .preview-chrome-label's absolute positioning scopes to it, not
    // <root>; visual baseline (border, bg) lives in PreviewTheme so user-imported .scss
    // themes can replace it.
    private const string PreviewMarkerRules =
        // min-width/min-height keep an Auto/Auto Image with no src visible during design;
        // intrinsic-texture sizing handles the with-src case via the engine itself. Preview-
        // only — never written into saved .razor.scss.
        ".preview-image-empty { " +
            "background-image: linear-gradient(to bottom right, rgba(120, 120, 160, 0.5), rgba(60, 60, 90, 0.5)); " +
            "border: 1px solid rgba(180, 180, 200, 0.5); " +
            "min-width: 32px; " +
            "min-height: 32px; " +
        "}\n" +
        ".preview-panel { position: relative; }\n" +
        ".preview-chrome-label { " +
            "position: absolute; " +
            "top: 0; left: 0; right: 0; bottom: 0; " +
            "justify-content: center; " +
            "align-items: center; " +
            "color: rgba(255, 255, 255, 0.4); " +
            "font-size: 20px; " +
            "text-align: center; " +
        "}\n" +
        // Chrome-hidden mode (toolbar toggle): scaffolding off, design renders as it
        // would outside the editor. Hides chrome labels, the empty-image placeholder,
        // selection outline, and the visual baselines on catch-all stand-ins
        // (.preview-buttongroup / .preview-dropdown). Real Sandbox.UI controls (Panel,
        // Form, Field, ...) are NOT targeted here — they no longer wear .preview-panel
        // (see DesignerWindow.MirrorRecord case ControlType.Panel) so the PreviewTheme
        // baseline doesn't apply to them, and user OverrideBackground/OverrideBorder on
        // those records render unobstructed in chrome-hidden mode (grd-1en).
        ".chrome-hidden .preview-chrome-label { display: none; }\n" +
        ".chrome-hidden .preview-image-empty { " +
            "background-image: none; " +
            "border: none; " +
            "min-width: 0; " +
            "min-height: 0; " +
        "}\n" +
        // Stand-in resets. Each stand-in still wears .preview-panel for the structural
        // `position: relative`, but its visual baseline (.preview-panel { border, bg,
        // min-height } in PreviewTheme) is overridden by these one-class-deeper rules.
        ".chrome-hidden .preview-buttongroup { " +
            "background-color: transparent; " +
            "border: none; " +
            "min-height: 0; " +
        "}\n" +
        ".chrome-hidden .preview-dropdown { " +
            "background-image: none; " +
            "background-color: transparent; " +
            "border: none; " +
            "min-height: 0; " +
            "box-shadow: none; " +
        "}\n" +
        ".chrome-hidden .preview-dropdown > .inner { " +
            "background-color: transparent; " +
            "border-left: none; " +
        "}\n" +
        // `outline: none` would be cleaner, but the engine's outline shorthand parser
        // only accepts the `<width> solid <color>` form (see SelectionRule above).
        // Zero-width transparent solid is the parser-friendly equivalent.
        ".chrome-hidden .selected { outline: 0px solid transparent; }";

    public static string GenerateRazorMarkup( DesignerDocument doc )
    {
        var sb = new StringBuilder();
        sb.AppendLine( $"@* Grains.RazorDesigner schema={SchemaVersion} *@" );
        sb.AppendLine( "@inherits Panel" );
        sb.AppendLine( "<root class=\"root\">" );
        foreach ( var r in doc.RootRecord.Children )
            EmitRazor( sb, r, indent: "    " );
        sb.AppendLine( "</root>" );
        return sb.ToString();
    }

    private static void EmitRazor( StringBuilder sb, ControlRecord r, string indent )
    {
        var meta = ControlMetadata.Get( r.Type );

        if ( meta.IsContainer )
        {
            // Self-close empty containers — matches hand-authored razor convention; the
            // <div></div> two-line form reads as "I forgot to write children" to a reviewer.
            if ( r.Children.Count == 0 )
            {
                sb.AppendLine( $"{indent}<{meta.Tag} class=\"{r.ClassName}\" />" );
                return;
            }
            sb.AppendLine( $"{indent}<{meta.Tag} class=\"{r.ClassName}\">" );
            foreach ( var child in r.Children )
                EmitRazor( sb, child, indent + "    " );
            sb.AppendLine( $"{indent}</{meta.Tag}>" );
            return;
        }

        switch ( r.Type )
        {
            case ControlType.Label:
            case ControlType.Button:
                sb.AppendLine(
                    $"{indent}<{meta.Tag} class=\"{r.ClassName}\">{Escape( r.Content )}</{meta.Tag}>" );
                break;

            case ControlType.Image:
                sb.AppendLine(
                    $"{indent}<{meta.Tag} class=\"{r.ClassName}\" src=\"{Escape( r.Source )}\" />" );
                break;

            case ControlType.TextEntry:
                sb.AppendLine(
                    $"{indent}<{meta.Tag} class=\"{r.ClassName}\" placeholder=\"{Escape( r.Placeholder )}\" />" );
                break;

            // IconPanel's glyph is text content of <i> (Sandbox.UI.IconPanel.Text). Reads from
            // IconName (engine icon picker via [IconName]); plain self-close pre-grd-zcq emitted
            // no glyph at all into saved razor — a pre-existing bug masked by live preview
            // reading record.Content directly. Now consistent across save + live mirror.
            case ControlType.IconPanel:
                sb.AppendLine(
                    $"{indent}<{meta.Tag} class=\"{r.ClassName}\">{Escape( r.IconName )}</{meta.Tag}>" );
                break;

            default:
                sb.AppendLine( $"{indent}<{meta.Tag} class=\"{r.ClassName}\" />" );
                break;
        }
    }

    // Preview pipeline: selection chrome -> theme -> chrome markers -> document rules.
    // The theme layer is swappable (PreviewTheme.Default or PreviewTheme.FromFile).
    public static string GeneratePreviewStylesheet( DesignerDocument doc, PreviewTheme theme )
    {
        var sb = new StringBuilder();
        sb.AppendLine( SelectionRule );
        sb.AppendLine( ( theme ?? PreviewTheme.Default ).Css );
        sb.AppendLine( PreviewMarkerRules );
        EmitDocumentRules( sb, doc, indent: "" );
        return sb.ToString();
    }

    // Saved scss never includes theme — only the user's per-record edits. Header comment
    // notes which theme the design was authored against so users can match at runtime.
    public static string GenerateSavedScss( DesignerDocument doc, string className, PreviewTheme theme )
    {
        var sb = new StringBuilder();
        sb.AppendLine( $"// designed against theme: {(theme ?? PreviewTheme.Default).Name}" );
        sb.AppendLine( $"{className} {{" );
        EmitDocumentRules( sb, doc, indent: "    " );
        sb.AppendLine( "}" );
        return sb.ToString();
    }

    private static void EmitDocumentRules( StringBuilder sb, DesignerDocument doc, string indent )
    {
        EmitRuleTree( sb, doc.RootRecord, indent );
    }

    private static void EmitRuleTree( StringBuilder sb, ControlRecord r, string indent )
    {
        var meta = ControlMetadata.Get( r.Type );
        var inner = indent + "    ";
        var body = BuildRuleBody( r, meta, inner );

        // .root is compound with the wrapper class, not a descendant; emit flat.
        var isRoot = r.ClassName == DesignerDocument.RootClassName;

        if ( isRoot )
        {
            if ( body.Length > 0 )
            {
                sb.AppendLine( $"{indent}.{r.ClassName} {{" );
                sb.Append( body );
                sb.AppendLine( $"{indent}}}" );
            }
            if ( meta.IsContainer )
            {
                foreach ( var c in r.Children )
                    EmitRuleTree( sb, c, indent );
            }
            return;
        }

        var hasChildren = meta.IsContainer && r.Children.Count > 0;
        if ( body.Length == 0 && !hasChildren ) return;

        sb.AppendLine( $"{indent}.{r.ClassName} {{" );
        if ( body.Length > 0 ) sb.Append( body );
        if ( hasChildren )
        {
            foreach ( var c in r.Children )
                EmitRuleTree( sb, c, inner );
        }
        sb.AppendLine( $"{indent}}}" );
    }

    private static StringBuilder BuildRuleBody( ControlRecord r, ControlMeta meta, string inner )
    {
        var body = new StringBuilder();

        if ( r.Width.Unit != LengthUnit.Auto )
            body.AppendLine( $"{inner}width: {r.Width.ToCss()};" );
        if ( r.Height.Unit != LengthUnit.Auto )
            body.AppendLine( $"{inner}height: {r.Height.ToCss()};" );

        var isRoot = r.ClassName == DesignerDocument.RootClassName;

        // Skip flex-self props on root: no parent flex container.
        if ( !isRoot )
        {
            // Guards: engine defaults (grow=0, shrink=1), not creation hints.
            if ( r.FlexGrow != 0f )
                body.AppendLine( $"{inner}flex-grow: {r.FlexGrow.ToString( "0.##", CultureInfo.InvariantCulture )};" );
            if ( r.FlexShrink != 1f )
                body.AppendLine( $"{inner}flex-shrink: {r.FlexShrink.ToString( "0.##", CultureInfo.InvariantCulture )};" );
            if ( r.FlexBasis.Unit != LengthUnit.Auto )
                body.AppendLine( $"{inner}flex-basis: {r.FlexBasis.ToCss()};" );
        }

        if ( meta.IsContainer )
        {
            // YogaWrapper engine defaults: direction=Row, justify=Start, align=Stretch.
            if ( r.Direction != FlexDirection.Row )
                body.AppendLine( $"{inner}flex-direction: {Css( r.Direction )};" );
            if ( r.Justify != JustifyContent.Start )
                body.AppendLine( $"{inner}justify-content: {Css( r.Justify )};" );
            if ( r.Align != AlignItems.Stretch )
                body.AppendLine( $"{inner}align-items: {Css( r.Align )};" );
            // Gap can only have a visible effect when there are >=2 siblings to space.
            // Suppress on empty/single-child containers so the saved scss doesn't carry
            // "phantom" gap declarations that imply children that aren't there.
            if ( r.Gap > 0f && r.Children.Count >= 2 )
                body.AppendLine( $"{inner}gap: {Px( r.Gap )};" );
            // Skip Auto (no padding-auto codepath in Sandbox.UI) and 0px (engine default).
            if ( r.Padding.Unit != LengthUnit.Auto && !( r.Padding.Unit == LengthUnit.Px && r.Padding.Value == 0f ) )
                body.AppendLine( $"{inner}padding: {r.Padding.ToCss()};" );
            if ( r.Wrap != FlexWrap.NoWrap )
                body.AppendLine( $"{inner}flex-wrap: {Css( r.Wrap )};" );
        }

        if ( !string.IsNullOrEmpty( meta.ExtraStyle ) )
        {
            foreach ( var decl in meta.ExtraStyle.Split( ';' ) )
            {
                var trimmed = decl.Trim();
                if ( trimmed.Length == 0 ) continue;
                body.AppendLine( $"{inner}{trimmed};" );
            }
        }

        // Typography — gated by OverrideTypography toggle so the inspector can carry
        // sensible visible defaults (white #FFFFFF, 14px, 400) without leaking them into
        // saved scss when the user hasn't opted in. Today only Label sets the toggle
        // (Inspector hides it for other types).
        if ( r.OverrideTypography )
        {
            // FontFamily skip-when-empty stays — empty means "keep theme's family but
            // override size/weight/color." Size/weight/color always emit when overriding.
            if ( !string.IsNullOrEmpty( r.FontFamily ) )
                body.AppendLine( $"{inner}font-family: {r.FontFamily};" );
            if ( r.FontSize.Unit != LengthUnit.Auto )
                body.AppendLine( $"{inner}font-size: {r.FontSize.ToCss()};" );
            body.AppendLine( $"{inner}font-weight: {r.FontWeight.ToString( CultureInfo.InvariantCulture )};" );
            body.AppendLine( $"{inner}color: {r.Color.Hex};" );
            // text-align is Label-only at the inspector level; gate at emit too so a
            // template-imported record with TextAlign set on a non-Label silently no-ops.
            if ( r.Type == ControlType.Label )
                body.AppendLine( $"{inner}text-align: {Css( r.TextAlign )};" );
        }

        // Background — gated by OverrideBackground. background-image accepts url(...) /
        // linear-gradient(...) / radial-gradient(...) per Sandbox.UI Styles.Set.cs SetImage.
        // When the user overrides the background but doesn't set their own image, emit
        // `background-image: none` to wipe any theme gradient (PreviewTheme paints .button
        // / .form / .preview-dropdown with linear-gradients that would otherwise mask the
        // user's background-color). Same fix applies in saved .razor.scss output: the
        // consuming addon's theme gradient gets correctly overridden too. (grd-cf8.)
        if ( r.OverrideBackground )
        {
            body.AppendLine( $"{inner}background-color: {r.BackgroundColor.Hex};" );
            if ( !string.IsNullOrWhiteSpace( r.BackgroundImage ) )
                body.AppendLine( $"{inner}background-image: {r.BackgroundImage.Trim()};" );
            else
                body.AppendLine( $"{inner}background-image: none;" );
        }

        // Border — gated by OverrideBorder. Engine accepts longhand border-color / border-width
        // directly (Styles.Set.cs:73-81); border-radius single Length applies to all four corners.
        if ( r.OverrideBorder )
        {
            if ( r.BorderRadius.Unit != LengthUnit.Auto )
                body.AppendLine( $"{inner}border-radius: {r.BorderRadius.ToCss()};" );
            if ( r.BorderWidth.Unit != LengthUnit.Auto )
                body.AppendLine( $"{inner}border-width: {r.BorderWidth.ToCss()};" );
            body.AppendLine( $"{inner}border-color: {r.BorderColor.Hex};" );
        }

        // Effects — gated by OverrideEffects. Single-layer box-shadow:
        //   <x> <y> <blur> <color> [inset]
        // Engine SetShadow (Styles.Set.cs:832-891) accepts comma-separated multi-layer too,
        // but the inspector exposes one layer; users can hand-edit saved scss for stacks.
        // Opacity extends this group (Tier 2): float 0..1, always emitted when override on.
        if ( r.OverrideEffects )
        {
            var inset = r.BoxShadowInset ? " inset" : "";
            body.AppendLine(
                $"{inner}box-shadow: {r.BoxShadowX.ToCss()} {r.BoxShadowY.ToCss()} {r.BoxShadowBlur.ToCss()} {r.BoxShadowColor.Hex}{inset};" );
            body.AppendLine( $"{inner}opacity: {r.Opacity.ToString( "0.##", CultureInfo.InvariantCulture )};" );
        }

        // Constraints (Tier 2) — gated by OverrideConstraints. Margin shorthand mirrors
        // padding (engine SetMargin parses 1-4 lengths, Styles.Set.cs:717-761). Min/max
        // sizes skip emit when Auto so a "max-width only" override stays compact.
        if ( r.OverrideConstraints )
        {
            // Margin on root has no flex parent to space against — same skip as flex-self.
            if ( !isRoot && r.Margin.Unit != LengthUnit.Auto && !( r.Margin.Unit == LengthUnit.Px && r.Margin.Value == 0f ) )
                body.AppendLine( $"{inner}margin: {r.Margin.ToCss()};" );
            if ( r.MinWidth.Unit != LengthUnit.Auto )
                body.AppendLine( $"{inner}min-width: {r.MinWidth.ToCss()};" );
            if ( r.MaxWidth.Unit != LengthUnit.Auto )
                body.AppendLine( $"{inner}max-width: {r.MaxWidth.ToCss()};" );
            if ( r.MinHeight.Unit != LengthUnit.Auto )
                body.AppendLine( $"{inner}min-height: {r.MinHeight.ToCss()};" );
            if ( r.MaxHeight.Unit != LengthUnit.Auto )
                body.AppendLine( $"{inner}max-height: {r.MaxHeight.ToCss()};" );
        }

        // Checkbox box sizing (grd-4gq). Sizes the .checkmark child independently of the
        // label's FontSize. Inner glyph font-size is derived as 0.75 * CheckboxSize so
        // the 16/12 PreviewTheme default ratio is preserved at any size — works across
        // px/rem/em/percent, no calc() needed. Auto skips emit so the theme baseline
        // (16x16, font-size 12px) takes effect — matches the Length-skip-when-Auto
        // convention used by FlexBasis/MinWidth/Margin/etc. Necessary because the
        // .checkmark glyph is `color: transparent` when unchecked, so width/height: auto
        // would collapse the box to zero content. Nested scss form `> .checkmark { ... }`
        // matches the container-children convention already used by EmitRuleTree.
        if ( r.Type == ControlType.Checkbox && r.CheckboxSize.Unit != LengthUnit.Auto )
        {
            var inner2 = inner + "    ";
            var glyphFontSize = new Length( r.CheckboxSize.Value * 0.75f, r.CheckboxSize.Unit );
            body.AppendLine( $"{inner}> .checkmark {{" );
            body.AppendLine( $"{inner2}width: {r.CheckboxSize.ToCss()};" );
            body.AppendLine( $"{inner2}height: {r.CheckboxSize.ToCss()};" );
            body.AppendLine( $"{inner2}font-size: {glyphFontSize.ToCss()};" );
            body.AppendLine( $"{inner}}}" );
        }

        // Interaction (Tier 2) — gated by OverrideInteraction. Engine accepts any cursor
        // string (Styles.Set.cs:2702 just stores the value); we emit our curated enum's
        // CSS keyword. Overflow shorthand drives both axes via BaseStyles.cs SetOverflow.
        if ( r.OverrideInteraction )
        {
            body.AppendLine( $"{inner}cursor: {Css( r.Cursor )};" );
            body.AppendLine( $"{inner}overflow: {Css( r.Overflow )};" );
        }

        return body;
    }

    private static string Css( FlexDirection d ) => d switch
    {
        FlexDirection.Row => "row",
        FlexDirection.Column => "column",
        _ => "row",
    };

    private static string Css( JustifyContent j ) => j switch
    {
        JustifyContent.Start => "flex-start",
        JustifyContent.Center => "center",
        JustifyContent.End => "flex-end",
        JustifyContent.SpaceBetween => "space-between",
        JustifyContent.SpaceAround => "space-around",
        _ => "flex-start",
    };

    private static string Css( AlignItems a ) => a switch
    {
        AlignItems.Start => "flex-start",
        AlignItems.Center => "center",
        AlignItems.End => "flex-end",
        AlignItems.Stretch => "stretch",
        _ => "stretch",
    };

    private static string Css( FlexWrap w ) => w switch
    {
        FlexWrap.NoWrap => "nowrap",
        FlexWrap.Wrap => "wrap",
        FlexWrap.WrapReverse => "wrap-reverse",
        _ => "nowrap",
    };

    private static string Css( TextAlignment t ) => t switch
    {
        TextAlignment.Left => "left",
        TextAlignment.Center => "center",
        TextAlignment.Right => "right",
        _ => "left",
    };

    // Curated CSS cursor keywords. Engine just stores the string (Styles.Set.cs:2702),
    // so the enum -> string mapping IS the validation surface for our designer.
    private static string Css( CursorKind c ) => c switch
    {
        CursorKind.Auto => "auto",
        CursorKind.Default => "default",
        CursorKind.Pointer => "pointer",
        CursorKind.Text => "text",
        CursorKind.Grab => "grab",
        CursorKind.Grabbing => "grabbing",
        CursorKind.Wait => "wait",
        CursorKind.Crosshair => "crosshair",
        CursorKind.Move => "move",
        CursorKind.NotAllowed => "not-allowed",
        CursorKind.None => "none",
        _ => "auto",
    };

    private static string Css( OverflowKind o ) => o switch
    {
        OverflowKind.Visible => "visible",
        OverflowKind.Hidden => "hidden",
        OverflowKind.Scroll => "scroll",
        OverflowKind.Clip => "clip",
        OverflowKind.ClipWhole => "clip-whole",
        _ => "visible",
    };

    private static string Px( float v ) => v.ToString( "F0", CultureInfo.InvariantCulture ) + "px";

    private static string Escape( string s )
    {
        if ( string.IsNullOrEmpty( s ) ) return "";
        return s
            .Replace( "&", "&amp;" )
            .Replace( "<", "&lt;" )
            .Replace( ">", "&gt;" )
            .Replace( "\"", "&quot;" );
    }
}
