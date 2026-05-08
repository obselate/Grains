using Editor;
using Grains.RazorDesigner.Document;
using Sandbox;

namespace Grains.RazorDesigner.Inspector;

// Numeric LineEdit + engine EnumControlWidget for the Length struct. Length is
// a readonly record struct so we can't use TryGetAsObject sub-property routing
// the way MarginControlWidget / VectorControlWidget do — we read/write the
// whole struct via SerializedProperty.GetValue<Length> / SetValue.
//
// Engine EnumControlWidget needs a SerializedProperty pointing at an enum.
// Length doesn't expose Unit through SerializedProperty (struct fields don't
// decompose), so we sit a tiny POCO proxy in front of LengthUnit, hand its
// serialized property to EnumControlWidget, and round-trip changes through
// our own OnUnitProxyChanged → SerializedProperty.SetValue<Length> path.
[CustomEditor( typeof( Length ) )]
public sealed class LengthControlWidget : ControlWidget
{
	private const string LogPrefix = "[Grains.RazorDesigner]";

	private LineEdit _valueEdit;
	private EnumControlWidget _unitWidget;
	private UnitProxy _unitProxy;
	private SerializedObject _unitSerialized;
	// Re-entrancy guard. Both the proxy SerializedObject and the outer
	// SerializedProperty fire change events synchronously inside SetValue,
	// so a one-shot push from either side would otherwise loop:
	//   user picks unit → proxy.OnPropertyChanged → outer.SetValue →
	//   our OnValueChanged → SyncFromProperty → proxy.SetValue (no-op) →
	//   proxy.OnPropertyChanged → ...
	private bool _syncing;

	// POCO whose only job is to host a LengthUnit property that
	// EditorTypeLibrary.GetSerializedObject can reflect on, producing a
	// SerializedProperty we can hand to EnumControlWidget.
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
		// EnumControlWidget.PaintControl draws label LeftCenter and the
		// dropdown chevron RightCenter inside the same rect (after an 8px
		// horizontal shrink). The chevron reserves ~17px on the right; we
		// need the rect wide enough for the longest entry ("Percent" — 7
		// chars) plus the chevron without overlap. ~96px clears it on
		// every theme density we've observed.
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

			// Push the unit through the proxy SerializedProperty so the
			// EnumControlWidget repaints with the new selection — direct
			// _unitProxy.Unit assignment would bypass the change event.
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
			// Bad input: restore the field, do NOT commit (avoids spurious
			// no-op undo entry + ValueChanged signal).
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

	// No OnPaint override on purpose: base ControlWidget.OnPaint paints the
	// canonical gray Theme.ControlBackground pill, and the transparent
	// LineEdit lets it show through (matching StringControlWidget's pattern).
	// MarginControlWidget overrides OnPaint to no-op because each child is
	// itself a FloatControlWidget that paints its own pill — that doesn't
	// apply here.
}
