using Editor;
using Grains.RazorDesigner.Document;
using Sandbox;

namespace Grains.RazorDesigner.Inspector;

public sealed class InspectorPanel : Widget
{
	private const string LogPrefix = "[Grains.RazorDesigner]";

	public System.Action ValueChanged;
	// (record, oldClassName). Surgical: caller swaps LivePanel class + repaints tree.
	public System.Action<ControlRecord, string> ClassNameChanged;

	private readonly DesignerDocument _document;
	private ControlRecord _target;
	private SerializedObject _serialized;
	private string _priorClassName;
	private Label _headerLabel;
	// Re-entry guard: SetValue on rejected edit fires OnFinishEdit again.
	private bool _isRevertingClassName;

	public InspectorPanel( Widget parent, DesignerDocument document ) : base( parent )
	{
		_document = document;
		Log.Info( $"{LogPrefix} InspectorPanel ctor" );
		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 4;
		MinimumWidth = 180;
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

		Layout.Clear( true );
		_headerLabel = null;

		if ( _target is null )
		{
			var placeholder = Layout.Add( new Label( "No selection" ) );
			placeholder.SetStyles( "color: rgba(255, 255, 255, 0.4); font-style: italic;" );
			Layout.AddStretchCell();
			return;
		}

		_headerLabel = Layout.Add( new Label( BuildHeaderText() ) );
		_headerLabel.SetStyles( "color: white; font-weight: bold;" );

		_serialized = EditorTypeLibrary.GetSerializedObject( _target );
		_serialized.OnPropertyChanged += OnPropertyChanged;

		// ClassName: commit-time only. OnFinishEdit fires on focus loss/Enter, not per keystroke.
		var classNameProp = _serialized.GetProperty( "ClassName" );
		if ( classNameProp is not null )
			classNameProp.OnFinishEdit += OnClassNameFinishEdit;

		var isContainer = ControlMetadata.Get( _target.Type ).IsContainer;
		var isRoot      = _target == _document.RootRecord;

		bool ShouldShow( SerializedProperty p )
		{
			if ( p.Name is "Direction" or "Justify" or "Align" or "Gap" or "Padding" or "Wrap" )
				return isContainer;

			if ( isContainer && p.Name == "Content" )
				return false;

			// FlexGrow/Shrink/Basis are per-child props; RootRecord has no parent flex container.
			if ( isRoot && p.Name is "FlexGrow" or "FlexShrink" or "FlexBasis" )
				return false;

			// RootClassName is the isRoot sentinel; renaming would break serializer.
			if ( isRoot && p.Name == "ClassName" )
				return false;

			return true;
		}

		Layout.Add( ControlSheet.Create( _serialized, ShouldShow ) );
		Layout.AddStretchCell();
	}

	private void OnPropertyChanged( SerializedProperty property )
	{
		// ClassName handled on commit via OnClassNameFinishEdit.
		if ( property.Name == "ClassName" )
			return;

		Log.Info( $"{LogPrefix} Inspector.{property.Name} -> {property.GetValue<object>()}" );
		ValueChanged?.Invoke();
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

		var error = ValidateClassName( newName, _target );
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
		if ( _headerLabel is not null )
			_headerLabel.Text = BuildHeaderText();
		ClassNameChanged?.Invoke( _target, oldName );
	}

	private string BuildHeaderText()
	{
		if ( _target is null ) return "";
		if ( _target == _document.RootRecord ) return "Canvas";
		return $"{_target.Type}: {_target.ClassName}";
	}

	// Returns null on success, else a short failure reason.
	private string ValidateClassName( string name, ControlRecord self )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return "empty";
		if ( name == DesignerDocument.RootClassName )
			return $"'{DesignerDocument.RootClassName}' is reserved for the canvas record";
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
		// RootRecord is excluded from WalkAll.
		if ( _document.RootRecord != self && _document.RootRecord.ClassName == name )
			return true;
		foreach ( var r in _document.WalkAll() )
		{
			if ( r == self ) continue;
			if ( r.ClassName == name ) return true;
		}
		return false;
	}
}
