using Editor;
using Grains.RazorDesigner.Document;
using Sandbox;

namespace Grains.RazorDesigner.Inspector;

// CustomEditor for Length (readonly record struct). UnitProxy hosts a LengthUnit
// property so EnumControlWidget has a SerializedProperty to bind to.
[CustomEditor( typeof( Length ) )]
public sealed class LengthControlWidget : ControlWidget
{
	private const string LogPrefix = "[Grains.RazorDesigner]";

	private LineEdit _valueEdit;
	private EnumControlWidget _unitWidget;
	private UnitProxy _unitProxy;
	private SerializedObject _unitSerialized;
	// Synchronous change events on both sides; without this guard SetValue would loop.
	private bool _syncing;

	private sealed class UnitProxy
	{
		public LengthUnit Unit { get; set; }
	}

	public LengthControlWidget( SerializedProperty property ) : base( property )
	{
		Log.Info( $"{LogPrefix} LengthControlWidget ctor for {property.Name}" );

		Layout = Layout.Row();
		Layout.Spacing = 2;

		_valueEdit = new LineEdit( this );
		_valueEdit.MinimumSize = new Vector2( 60, Theme.RowHeight );
		_valueEdit.MaximumSize = new Vector2( 4096, Theme.RowHeight );
		_valueEdit.SetStyles( "background-color: transparent;" );
		_valueEdit.EditingFinished += OnValueEditFinished;
		Layout.Add( _valueEdit, 1 );

		_unitProxy = new UnitProxy { Unit = LengthUnit.Auto };
		_unitSerialized = EditorTypeLibrary.GetSerializedObject( _unitProxy );
		var unitProp = _unitSerialized.GetProperty( nameof( UnitProxy.Unit ) );
		_unitWidget = new EnumControlWidget( unitProp );
		// 96px clears longest entry ("Percent") + chevron without overlap.
		_unitWidget.MinimumWidth = 96;
		_unitWidget.MaximumWidth = 96;
		_unitSerialized.OnPropertyChanged += OnUnitProxyChanged;
		Layout.Add( _unitWidget );

		SyncFromProperty();
	}

	private void SyncFromProperty()
	{
		if ( _syncing ) return;
		_syncing = true;

		try
		{
			var len = SerializedProperty.GetValue<Length>( Length.Auto );

			_valueEdit.Text = len.Value.ToString( "0.###" );

			// Push through SerializedProperty so EnumControlWidget repaints; direct field assignment skips the event.
			var unitProp = _unitSerialized.GetProperty( nameof( UnitProxy.Unit ) );
			unitProp.SetValue( len.Unit );

			_valueEdit.Enabled = len.Unit != LengthUnit.Auto;
		}
		finally
		{
			_syncing = false;
		}
	}

	private void OnUnitProxyChanged( SerializedProperty property )
	{
		if ( _syncing ) return;
		if ( ReadOnly || !SerializedProperty.IsEditable )
			return;

		_syncing = true;
		try
		{
			var current = SerializedProperty.GetValue<Length>( Length.Auto );
			var newValue = new Length( current.Value, _unitProxy.Unit );

			Log.Info( $"{LogPrefix} LengthControlWidget OnUnitChanged {newValue}" );

			PropertyStartEdit();
			SerializedProperty.SetValue( newValue );
			SignalValuesChanged();
			PropertyFinishEdit();

			_valueEdit.Enabled = _unitProxy.Unit != LengthUnit.Auto;
		}
		finally
		{
			_syncing = false;
		}
	}

	private void OnValueEditFinished()
	{
		if ( _syncing ) return;
		if ( ReadOnly || !SerializedProperty.IsEditable )
			return;

		var current = SerializedProperty.GetValue<Length>( Length.Auto );

		if ( !float.TryParse( _valueEdit.Text, out var parsed ) )
		{
			// Restore on bad input; don't commit (avoids spurious undo entry + ValueChanged).
			_valueEdit.Text = current.Value.ToString( "0.###" );
			Log.Info( $"{LogPrefix} LengthControlWidget OnValueEditFinished: parse failed, restored to {current.Value}" );
			return;
		}

		var newValue = new Length( parsed, current.Unit );

		Log.Info( $"{LogPrefix} LengthControlWidget OnValueChanged {newValue}" );

		PropertyStartEdit();
		SerializedProperty.SetValue( newValue );
		SignalValuesChanged();
		PropertyFinishEdit();
	}

	protected override void OnValueChanged()
	{
		base.OnValueChanged();
		SyncFromProperty();
	}
}
