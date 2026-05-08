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

    // Preview-only; NOT emitted into saved .razor.scss. .preview-panel/.preview-layout
    // need position:relative so .preview-chrome-label's absolute positioning scopes to them, not <root>.
    private const string PreviewMarkerRules =
        ".preview-button { " +
            "background-color: rgba(60, 60, 80, 0.85); " +
            "border: 1px solid rgba(180, 180, 200, 0.6); " +
            "border-radius: 3px; " +
            "text-align: center; " +
            "color: white; " +
        "}\n" +
        ".preview-textentry { " +
            "background-color: rgba(20, 20, 30, 0.6); " +
            "border: 1px solid rgba(120, 120, 140, 0.6); " +
            "padding-left: 6px; " +
            "color: rgba(200, 200, 200, 0.7); " +
        "}\n" +
        ".preview-image-empty { " +
            "background-image: linear-gradient(to bottom right, rgba(120, 120, 160, 0.5), rgba(60, 60, 90, 0.5)); " +
            "border: 1px solid rgba(180, 180, 200, 0.5); " +
        "}\n" +
        ".preview-panel { " +
            "position: relative; " +
            "border: 2px solid rgba(180, 180, 195, 0.45); " +
            "background-color: rgba(180, 180, 195, 0.05); " +
            "min-height: 32px; " +
        "}\n" +
        ".preview-label { " +
            "border: 1px solid rgba(220, 220, 220, 0.22); " +
        "}\n" +
        ".preview-layout { " +
            "position: relative; " +
            "border: 2px solid rgba(63, 169, 245, 0.45); " +
            "background-color: rgba(63, 169, 245, 0.05); " +
            "min-height: 32px; " +
        "}\n" +
        ".preview-chrome-label { " +
            "position: absolute; " +
            "top: 0; left: 0; right: 0; bottom: 0; " +
            "justify-content: center; " +
            "align-items: center; " +
            "color: rgba(255, 255, 255, 0.4); " +
            "font-size: 12px; " +
            "text-align: center; " +
        "}";

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
                    $"{indent}<{meta.Tag} class=\"{r.ClassName}\" src=\"{Escape( r.Content )}\" />" );
                break;

            case ControlType.TextEntry:
                sb.AppendLine(
                    $"{indent}<{meta.Tag} class=\"{r.ClassName}\" placeholder=\"{Escape( r.Content )}\" />" );
                break;

            default:
                sb.AppendLine( $"{indent}<{meta.Tag} class=\"{r.ClassName}\" />" );
                break;
        }
    }

    public static string GeneratePreviewStylesheet( DesignerDocument doc )
    {
        var sb = new StringBuilder();
        sb.AppendLine( SelectionRule );
        sb.AppendLine( PreviewMarkerRules );
        EmitDocumentRules( sb, doc, indent: "" );
        return sb.ToString();
    }

    public static string GenerateSavedScss( DesignerDocument doc, string className )
    {
        var sb = new StringBuilder();
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
            if ( r.Gap > 0f )
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
