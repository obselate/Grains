using System.Collections.Generic;
using Editor;
using Grains.RazorDesigner.Common;
using Grains.RazorDesigner.Document;
using Sandbox;
using Margin = Sandbox.UI.Margin;

namespace Grains.RazorDesigner.Inspector;

// Mirrors the canonical Editor Inspector dock (Editor/Inspector.cs) but splits properties
// across one CollapsibleSection per [Group] instead of a single sheet (grd-89w):
// - Lean toolbar with filter LineEdit
// - ScrollArea hosting a column of CollapsibleSections, each with its own scoped ControlSheet
// - Override-gated detail fields stay visible when their toggle is off; ControlWidget.ReadOnly
//   greys them out instead of hiding (so users can see what's available before opting in)
// - OnPaint fallback message when no target is bound, like the canonical "No object selected."
public sealed class InspectorPanel : Widget
{
	private const string LogPrefix = "[Grains.RazorDesigner]";
	private const string ExpandStateCookie = "razordesigner.inspector.expanded";

	public System.Action ValueChanged;
	// (record, oldClassName). Surgical: caller swaps LivePanel class + repaints tree.
	public System.Action<ControlRecord, string> ClassNameChanged;

	private readonly DesignerDocument _document;
	private ControlRecord _target;
	private SerializedObject _serialized;
	private string _priorClassName;
	// Re-entry guard: SetValue on rejected edit fires OnFinishEdit again.
	private bool _isRevertingClassName;

	private LineEdit _filterEdit;
	private string _filterText = "";
	private CollapsibleSection _identityBanner;
	private ScrollArea _scroll;
	private Widget _canvas;
	// One CollapsibleSection per [Group], built in the order defined by GroupOrder. Each
	// section hosts its own ControlSheet filtered to that group's properties — this gives
	// us per-group default-collapse and a place to hang the ReadOnly post-process.
	private readonly Dictionary<string, CollapsibleSection> _sections = new();
	// Property name -> ControlWidget, indexed at build time so toggling Override flips
	// ReadOnly inline without rebuilding the section structure.
	private readonly Dictionary<string, ControlWidget> _propertyWidgets = new();
	// Group name -> user's chosen expand state. Lazily loaded from cookie on first build,
	// updated on every section toggle (except while filter is forcing all sections open).
	// Persists across selection changes and editor sessions. (grd-9el.)
	private readonly Dictionary<string, bool> _expandState = new();
	private bool _expandStateLoaded;

	// Group display order + default-expanded policy. Identity/Layout/Flex/Content/Image/Icon
	// are "what you came to edit" — open by default. Typography/Constraints/Background/Border/
	// Effects/Interaction are the styling tail — collapsed by default; click to expand.
	// (grd-89w.) Group icons are Material Icons names — same set IconControlWidget uses.
	private static readonly (string Group, bool Expanded, string Icon)[] GroupOrder =
	{
		( "Identity",     true,  "label" ),
		( "Layout",       true,  "dashboard" ),
		( "Flex",         true,  "view_column" ),
		( "Content",      true,  "text_fields" ),
		( "Image",        true,  "image" ),
		( "Icon",         true,  "star" ),
		( "Typography",   false, "format_size" ),
		( "Constraints",  false, "fit_screen" ),
		( "Background",   false, "palette" ),
		( "Border",       false, "border_style" ),
		( "Effects",      false, "auto_awesome" ),
		( "Interaction",  false, "touch_app" ),
	};

	public InspectorPanel( Widget parent, DesignerDocument document ) : base( parent )
	{
		_document = document;
		Log.Info( $"{LogPrefix} InspectorPanel ctor" );
		Layout = Layout.Column();
		Layout.Margin = 0;
		Layout.Spacing = 0;
		MinimumWidth = 280;

		var toolbar = new Widget( this );
		toolbar.Layout = Layout.Row();
		toolbar.Layout.Margin = 4;
		toolbar.Layout.Spacing = 4;

		_filterEdit = new LineEdit( toolbar ) { PlaceholderText = "Filter properties..." };
		_filterEdit.TextEdited += OnFilterEdited;
		toolbar.Layout.Add( _filterEdit, 1 );

		Layout.Add( toolbar );
		Layout.AddSeparator();

		_identityBanner = new CollapsibleSection( this, "", "category" );
		_identityBanner.IsCollapsible = false;
		Layout.Add( _identityBanner );

		_scroll = new ScrollArea( this );
		_scroll.Canvas = new Widget();
		_scroll.Canvas.Layout = Layout.Column();
		_scroll.Canvas.Layout.Margin = new Margin( 0, 4 );
		_scroll.Canvas.VerticalSizeMode = SizeMode.CanGrow;
		_scroll.Canvas.HorizontalSizeMode = SizeMode.Flexible;
		_canvas = _scroll.Canvas;
		Layout.Add( _scroll, 1 );
		// Empty-state anchor: when _scroll/_identityBanner go invisible (no target), the column
		// has no stretch and would vertically-center the toolbar. Trailing stretch keeps the
		// toolbar pinned to top in that state; collapses to zero when _scroll's stretch is live.
		Layout.AddStretchCell();

		Rebuild();
	}

	public void SetTarget( ControlRecord record )
	{
		_target = record;
		_priorClassName = _target?.ClassName;
		Log.Info( $"{LogPrefix} Inspector.SetTarget({_target?.ClassName ?? "<none>"})" );
		Rebuild();
	}

	private void Rebuild()
	{
		// Unwire prior _serialized: orphaned instance still holds delegate refs.
		if ( _serialized is not null )
		{
			_serialized.OnPropertyChanged -= OnPropertyChanged;
			var oldClassName = _serialized.GetProperty( "ClassName" );
			if ( oldClassName is not null )
				oldClassName.OnFinishEdit -= OnClassNameFinishEdit;
			_serialized = null;
		}

		if ( _target is null )
		{
			_identityBanner.Visible = false;
			_scroll.Visible = false;
			_canvas.Layout.Clear( true );
			_sections.Clear();
			_propertyWidgets.Clear();
			Update();
			return;
		}

		_identityBanner.Visible = true;
		_identityBanner.Title = BuildHeaderText();
		_identityBanner.Icon = ControlMetadata.Get( _target.Type ).IconName;
		_scroll.Visible = true;

		_serialized = EditorTypeLibrary.GetSerializedObject( _target );
		_serialized.OnPropertyChanged += OnPropertyChanged;

		// ClassName: commit-time only. OnFinishEdit fires on focus loss/Enter, not per keystroke.
		var classNameProp = _serialized.GetProperty( "ClassName" );
		if ( classNameProp is not null )
			classNameProp.OnFinishEdit += OnClassNameFinishEdit;

		BuildSections();
	}

	// Build one CollapsibleSection per [Group] (grd-89w). Each section hosts its own
	// ControlSheet populated row-by-row via AddRow — returns the ControlWidget directly,
	// which we stash by property name so ApplyReadOnlyStates can flip ReadOnly later.
	// Iterating SerializedObject manually (canonical pattern: see addons/tools
	// FolderMetadataDialog.cs) avoids ControlSheet.AddObject's auto-grouping behaviour
	// (it wraps multi-property groups in a ControlSheetGroup widget that hides individual
	// rows behind a header) and gives us the per-row handles we need.
	private void BuildSections()
	{
		_canvas.Layout.Clear( true );
		_sections.Clear();
		_propertyWidgets.Clear();

		var filterActive = !string.IsNullOrEmpty( _filterText );

		foreach ( var (groupName, defaultExpanded, icon) in GroupOrder )
		{
			var sheet = new ControlSheet();
			sheet.IncludePropertyNames = true;
			var addedAny = false;

			foreach ( var prop in _serialized )
			{
				if ( !string.Equals( prop.GroupName, groupName, System.StringComparison.Ordinal ) )
					continue;
				if ( !ShouldShow( prop ) ) continue;

				var widget = sheet.AddRow( prop );
				if ( widget is null ) continue;
				_propertyWidgets[prop.Name] = widget;
				addedAny = true;
			}

			if ( !addedAny ) continue;

			LoadExpandStateOnce();
			var preferred = _expandState.TryGetValue( groupName, out var saved ) ? saved : defaultExpanded;

			var section = new CollapsibleSection( _canvas, groupName, icon );
			// Filter expansion: when the user is searching, force every populated section
			// open so matches in collapsed groups aren't invisible. Otherwise honour the
			// user's saved preference, falling back to GroupOrder default if none.
			section.Expanded = filterActive || preferred;
			section.BodyLayout.Add( sheet );
			// Persist user toggles. Skip while filter is forcing-open: filter is a transient
			// view, toggles inside it shouldn't overwrite the user's actual preference. (grd-9el.)
			var capturedFilterActive = filterActive;
			var capturedGroupName = groupName;
			section.ExpandedChanged = expanded =>
			{
				if ( capturedFilterActive ) return;
				_expandState[capturedGroupName] = expanded;
				SaveExpandState();
			};
			_canvas.Layout.Add( section );
			_sections[groupName] = section;
		}

		_canvas.Layout.AddStretchCell();
		ApplyReadOnlyStates();
		_scroll.Update();
	}

	// Drives the show-but-disable affordance: detail fields stay visible when their Override
	// toggle is off, but their ControlWidget is grayed via ReadOnly. Called on initial build
	// and again whenever an Override<X> toggle changes.
	private void ApplyReadOnlyStates()
	{
		if ( _target is null ) return;

		SetReadOnly( !_target.OverrideTypography,
			"FontFamily", "FontSize", "FontWeight", "Color", "TextAlign" );
		SetReadOnly( !_target.OverrideBackground,
			"BackgroundColor", "BackgroundImage" );
		SetReadOnly( !_target.OverrideBorder,
			"BorderRadius", "BorderColor", "BorderWidth" );
		SetReadOnly( !_target.OverrideEffects,
			"BoxShadowX", "BoxShadowY", "BoxShadowBlur", "BoxShadowColor", "BoxShadowInset", "Opacity" );
		SetReadOnly( !_target.OverrideConstraints,
			"Margin", "MinWidth", "MaxWidth", "MinHeight", "MaxHeight" );
		SetReadOnly( !_target.OverrideInteraction,
			"Cursor", "Overflow" );
	}

	private void SetReadOnly( bool readOnly, params string[] propertyNames )
	{
		foreach ( var name in propertyNames )
		{
			if ( _propertyWidgets.TryGetValue( name, out var w ) )
				w.ReadOnly = readOnly;
		}
	}

	// Lazy-load the user's saved section expand state from EditorCookie. Cookie format is
	// "Group=1,Other=0,..." — flat key=bool, comma-separated. One cookie holds all groups
	// (12 entries max). Loaded once per InspectorPanel lifetime; subsequent BuildSections
	// calls reuse the in-memory dict. (grd-9el.)
	private void LoadExpandStateOnce()
	{
		if ( _expandStateLoaded ) return;
		_expandStateLoaded = true;

		var raw = EditorCookie.Get<string>( ExpandStateCookie, null );
		if ( string.IsNullOrEmpty( raw ) ) return;

		foreach ( var pair in raw.Split( ',', System.StringSplitOptions.RemoveEmptyEntries ) )
		{
			var eq = pair.IndexOf( '=' );
			if ( eq <= 0 ) continue;
			var key = pair.Substring( 0, eq );
			var val = pair.Substring( eq + 1 );
			_expandState[key] = val == "1";
		}
		Log.Info( $"{LogPrefix} Inspector expand-state loaded: {_expandState.Count} entries" );
	}

	private void SaveExpandState()
	{
		var sb = new System.Text.StringBuilder();
		foreach ( var kv in _expandState )
		{
			if ( sb.Length > 0 ) sb.Append( ',' );
			sb.Append( kv.Key ).Append( '=' ).Append( kv.Value ? '1' : '0' );
		}
		EditorCookie.Set( ExpandStateCookie, sb.ToString() );
	}

	// Painted when no target is bound. Mirrors canonical Editor Inspector.cs OnPaint behaviour.
	protected override void OnPaint()
	{
		if ( _target is not null ) return;

		Paint.ClearPen();
		Paint.ClearBrush();
		Paint.SetDefaultFont( italic: true );
		Paint.SetPen( Theme.SurfaceLightBackground );

		var r = LocalRect;
		r.Top += 128;
		Paint.DrawText( r, "No control selected.", TextFlag.CenterTop );
	}

	private bool ShouldShow( SerializedProperty p )
	{
		if ( p.HasAttribute<HideAttribute>() )
			return false;

		var isContainer = ControlMetadata.Get( _target.Type ).IsContainer;
		var isRoot      = _target == _document.RootRecord;

		if ( p.Name is "Direction" or "Justify" or "Align" or "Gap" or "Padding" or "Wrap" )
		{
			if ( !isContainer ) return false;
		}

		if ( isContainer && p.Name == "Content" )
			return false;

		// Per-type fields gated by ControlType. Template for Slider Min/Max/Step, Button Icon, etc.
		// Image owns Source (not Content); IconPanel owns IconName (not Content); TextEntry
		// owns Placeholder (not Content); other types hide all three. IconName carries
		// [IconName] which routes to the engine's IconControlWidget grid picker — same widget
		// as SaveTemplateDialog's template-icon row.
		if ( p.Name == "Source" && _target.Type != ControlType.Image )
			return false;
		if ( p.Name == "IconName" && _target.Type != ControlType.IconPanel )
			return false;
		if ( p.Name == "Placeholder" && _target.Type != ControlType.TextEntry )
			return false;
		if ( p.Name == "CheckboxSize" && _target.Type != ControlType.Checkbox )
			return false;
		if ( p.Name == "Content" && _target.Type is ControlType.Image or ControlType.IconPanel or ControlType.TextEntry )
			return false;

		// Typography surface varies by control type. Label/Button/TextEntry/Checkbox expose
		// FontFamily/FontSize/FontWeight/Color — they all render plain text via standard
		// CSS font-* + color, and inheritance reaches the inner label even when the
		// engine wraps text in a child element (Button/TextEntry/Checkbox each have an
		// internal Label). IconPanel exposes only Size+Color: changing FontFamily would
		// replace the icon font and render the literal string ("help") instead of the
		// glyph; FontWeight is a no-op against the single-weight Material Icons font.
		// TextAlign is Label-only by design: on Button/TextEntry/Checkbox the inner
		// Label is shrink-wrapped inside a flex parent, so `text-align` has nothing to
		// align — the menu addon convention is `justify-content` on the wrapping parent
		// for that case. Users align those controls by wrapping them in a Panel and
		// setting Layout > Justify on the Panel, not by reaching for TextAlign.
		// Override-gating of detail fields is no longer done here — under grd-89w the
		// fields stay visible and are grayed via ControlWidget.ReadOnly post-build.
		var isTextControl = _target.Type is ControlType.Label or ControlType.Button
			or ControlType.TextEntry or ControlType.Checkbox;
		var isIconControl = _target.Type is ControlType.IconPanel;

		if ( p.Name is "OverrideTypography" or "FontSize" or "Color" )
		{
			if ( !isTextControl && !isIconControl ) return false;
		}
		if ( p.Name is "FontFamily" or "FontWeight" )
		{
			if ( !isTextControl ) return false;
		}
		if ( p.Name == "TextAlign" && _target.Type != ControlType.Label )
			return false;

		// Constraints (Tier 2) — Margin hidden on root (no parent flex container, same
		// skip as FlexGrow/Shrink/Basis below). Override-gating for the rest is via
		// ReadOnly post-build, not hide-here.
		if ( isRoot && p.Name == "Margin" ) return false;

		// FlexGrow/Shrink/Basis are per-child props; RootRecord has no parent flex container.
		if ( isRoot && p.Name is "FlexGrow" or "FlexShrink" or "FlexBasis" )
			return false;

		// RootClassName is the isRoot sentinel; renaming would break serializer.
		if ( isRoot && p.Name == "ClassName" )
			return false;

		if ( !string.IsNullOrEmpty( _filterText ) )
		{
			var ft = _filterText.ToLowerInvariant();
			if ( (p.Name?.ToLowerInvariant().Contains( ft )) == true ) return true;
			if ( (p.DisplayName?.ToLowerInvariant().Contains( ft )) == true ) return true;
			if ( (p.GroupName?.ToLowerInvariant().Contains( ft )) == true ) return true;
			return false;
		}

		return true;
	}

	private void OnFilterEdited( string text )
	{
		_filterText = text ?? "";
		if ( _serialized is null ) return;
		BuildSections();
	}

	private void OnPropertyChanged( SerializedProperty property )
	{
		// ClassName handled on commit via OnClassNameFinishEdit.
		if ( property.Name == "ClassName" )
			return;

		Log.Info( $"{LogPrefix} Inspector.{property.Name} -> {property.GetValue<object>()}" );
		ValueChanged?.Invoke();

		// Override<Group> toggles flip detail-field ReadOnly state. Walk the indexed
		// widgets and re-apply — no full rebuild, no flicker, no scroll-position loss.
		if ( property.Name.StartsWith( "Override" ) )
			ApplyReadOnlyStates();
	}

	private void OnClassNameFinishEdit( SerializedProperty property )
	{
		HandleClassNameChange();
	}

	private void HandleClassNameChange()
	{
		if ( _target is null ) return;
		if ( _isRevertingClassName ) return;

		var newName = _target.ClassName;
		if ( newName == _priorClassName )
			return;

		var error = _document.ValidateClassName( newName, _target );
		if ( error is not null )
		{
			Log.Warning( $"{LogPrefix} Inspector.ClassName '{newName}' rejected: {error} -> reverting to '{_priorClassName}'" );

			// bd memory: engine-stringcontrolwidget-onvalue. SetValue re-syncs LineEdit on focus loss.
			_isRevertingClassName = true;
			try
			{
				var prop = _serialized?.GetProperty( "ClassName" );
				if ( prop is not null )
					prop.SetValue<string>( _priorClassName );
				else
					_target.ClassName = _priorClassName;
			}
			finally
			{
				_isRevertingClassName = false;
			}
			return;
		}

		Log.Info( $"{LogPrefix} Inspector.ClassName '{_priorClassName}' -> '{newName}'" );
		var oldName = _priorClassName;
		_priorClassName = newName;
		_identityBanner.Title = BuildHeaderText();
		_identityBanner.Update();
		ClassNameChanged?.Invoke( _target, oldName );
	}

	private string BuildHeaderText()
	{
		if ( _target is null ) return "";
		if ( _target == _document.RootRecord ) return "Canvas";
		return $"{_target.Type}: {_target.ClassName}";
	}
}
