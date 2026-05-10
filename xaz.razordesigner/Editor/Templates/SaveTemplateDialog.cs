using System;
using System.Collections.Generic;
using Editor;
using Grains.RazorDesigner.Document;
using Sandbox;

namespace Grains.RazorDesigner.Templates;

// Modal save dialog. Pattern mirrors DesignerWindow.OpenCustomViewportDialog: Editor.Dialog
// with SetModal + Show; buttons close via Close(); confirm fires a callback with the
// populated PaletteTemplate.
public sealed class SaveTemplateDialog
{
	private const string LogPrefix = "[Grains.RazorDesigner]";

	private static readonly char[] InvalidNameChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

	private readonly Widget _parent;
	private readonly PaletteTemplateStore _store;
	private readonly IReadOnlyList<ControlRecord> _selectedRoots;
	// LCA-of-selection — null when wrap is meaningless. Drives wrap-checkbox enabled state
	// and supplies layout-only properties for the wrapper Panel.
	private readonly ControlRecord _wrapInheritFrom;

	private Editor.Dialog _dialog;
	private LineEdit _nameEdit;
	private Editor.Label _errorLabel;
	private Editor.Label _wrapHint;
	private Checkbox _wrapCheckbox;
	private Editor.Button _saveButton;
	private IconHolder _iconHolder;

	public SaveTemplateDialog( Widget parent, PaletteTemplateStore store, IReadOnlyList<ControlRecord> selectedRoots, ControlRecord wrapInheritFrom )
	{
		_parent = parent;
		_store = store;
		_selectedRoots = selectedRoots;
		_wrapInheritFrom = wrapInheritFrom;
		_iconHolder = new IconHolder { Icon = "" };
	}

	// onConfirm receives the populated record. onCancel is optional. The dialog is
	// shown immediately.
	public void Show( Action<PaletteTemplate> onConfirm, Action onCancel = null )
	{
		_dialog = new Editor.Dialog( _parent );
		_dialog.Window.WindowTitle = "Save as Template";
		_dialog.Window.SetWindowIcon( "bookmark_add" );
		_dialog.Window.SetModal( true, true );
		_dialog.Window.MinimumWidth = 360;

		_dialog.Layout = Layout.Column();
		_dialog.Layout.Margin = 16;
		_dialog.Layout.Spacing = 10;

		// Name field with live validation.
		_dialog.Layout.Add( new Editor.Label( _dialog ) { Text = "Name" } );
		_nameEdit = new LineEdit( _dialog ) { PlaceholderText = "ButtonRow" };
		_nameEdit.TextEdited += _ => RevalidateName();
		_dialog.Layout.Add( _nameEdit );

		_errorLabel = new Editor.Label( _dialog ) { Text = "" };
		_errorLabel.SetStyles( "color: #e07070; font-size: 11px;" );
		_dialog.Layout.Add( _errorLabel );

		// Icon picker — engine [IconName] + ControlSheet binding.
		_dialog.Layout.Add( new Editor.Label( _dialog ) { Text = "Icon" } );
		var iconSheet = new ControlSheet();
		iconSheet.IncludePropertyNames = false;
		var serialized = EditorTypeLibrary.GetSerializedObject( _iconHolder );
		iconSheet.AddObject( serialized );
		_dialog.Layout.Add( iconSheet );

		// Wrap checkbox + hint.
		var canWrap = _wrapInheritFrom is not null && _selectedRoots is { Count: > 1 };
		_wrapCheckbox = new Checkbox( "Wrap selected controls in a new Panel", _dialog );
		_wrapCheckbox.Enabled = canWrap;
		_wrapCheckbox.Value = false;
		_wrapCheckbox.ToolTip = canWrap
			? $"Inherits Direction / Wrap / Justify / Align from \"{_wrapInheritFrom.ClassName}\"."
			: "Only meaningful when 2+ siblings are selected.";
		_dialog.Layout.Add( _wrapCheckbox );

		_wrapHint = new Editor.Label( _dialog )
		{
			Text = canWrap
				? "Disabled when the selection is a single root or spans multiple parents."
				: "(Save the selection as-is.)",
		};
		_wrapHint.SetStyles( "color: #888; font-size: 11px;" );
		_dialog.Layout.Add( _wrapHint );

		// Buttons.
		var buttonRow = _dialog.Layout.Add( Layout.Row() );
		buttonRow.Spacing = 6;
		buttonRow.AddStretchCell();

		var cancelButton = new Editor.Button( _dialog ) { Text = "Cancel", MinimumWidth = 72 };
		cancelButton.MouseLeftPress += () =>
		{
			Log.Info( $"{LogPrefix} SaveTemplateDialog cancelled" );
			_dialog.Close();
			onCancel?.Invoke();
		};
		buttonRow.Add( cancelButton );

		_saveButton = new Editor.Button( _dialog ) { Text = "Save", MinimumWidth = 72 };
		_saveButton.MouseLeftPress += () => OnSave( onConfirm );
		buttonRow.Add( _saveButton );

		_dialog.Window.AdjustSize();
		_dialog.Show();

		RevalidateName();
		_nameEdit.Focus();
	}

	private void RevalidateName()
	{
		var name = (_nameEdit.Text ?? "").Trim();
		string error = null;

		if ( string.IsNullOrEmpty( name ) )
		{
			error = "Name required.";
		}
		else if ( name.IndexOfAny( InvalidNameChars ) >= 0 )
		{
			error = "Name contains an invalid character (/ \\ : * ? \" < > |).";
		}
		else if ( _store.NameExists( name ) )
		{
			error = $"A template named \"{name}\" already exists.";
		}

		_errorLabel.Text = error ?? "";
		_saveButton.Enabled = error is null;
	}

	private void OnSave( Action<PaletteTemplate> onConfirm )
	{
		var name = (_nameEdit.Text ?? "").Trim();
		var icon = _iconHolder.Icon ?? "";
		var wrap = _wrapCheckbox.Enabled && _wrapCheckbox.Value;

		IReadOnlyList<ControlRecord> roots;
		if ( wrap && _wrapInheritFrom is not null )
		{
			var wrapper = new ControlRecord
			{
				Type = ControlType.Panel,
				ClassName = "wrapper",
				Width = Length.Auto,
				Height = Length.Auto,
				Direction = _wrapInheritFrom.Direction,
				Wrap = _wrapInheritFrom.Wrap,
				Justify = _wrapInheritFrom.Justify,
				Align = _wrapInheritFrom.Align,
			};
			foreach ( var r in _selectedRoots )
			{
				if ( r is null ) continue;
				wrapper.Children.Add( CloneSerialisable( r ) );
			}
			roots = new[] { wrapper };
		}
		else
		{
			var list = new List<ControlRecord>( _selectedRoots.Count );
			foreach ( var r in _selectedRoots )
			{
				if ( r is null ) continue;
				list.Add( CloneSerialisable( r ) );
			}
			roots = list;
		}

		// FilePath is overwritten by Store.Save with the actual on-disk path.
		var template = new PaletteTemplate(
			Name: name,
			IconName: icon,
			WrappedInContainer: wrap,
			Roots: roots,
			FilePath: "" );

		Log.Info( $"{LogPrefix} SaveTemplateDialog confirm: \"{name}\", icon=\"{icon}\", wrap={wrap}, roots={roots.Count}" );

		_dialog.Close();
		onConfirm?.Invoke( template );
	}

	// We intentionally do NOT call Document.Clone here because that counter-mints class
	// names against the live document — but the saved template should preserve the
	// original class names (the document mints fresh ones at AddTemplate time).
	private static ControlRecord CloneSerialisable( ControlRecord src )
	{
		var clone = new ControlRecord
		{
			Type = src.Type,
			ClassName = src.ClassName,
		};
		src.CopyFieldsTo( clone );
		foreach ( var c in src.Children )
			clone.Children.Add( CloneSerialisable( c ) );
		return clone;
	}

	// Synthetic property holder that surfaces an [IconName] string to ControlSheet.
	// EditorTypeLibrary.GetSerializedObject + sheet.AddObject hooks the engine's
	// IconControlWidget automatically (see addons/tools/Code/Widgets/IconPicker/).
	private sealed class IconHolder
	{
		[IconName]
		[Title( "" )]
		public string Icon { get; set; }
	}
}
