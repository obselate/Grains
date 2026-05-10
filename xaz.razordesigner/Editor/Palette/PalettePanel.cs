using System;
using System.Collections.Generic;
using Editor;
using Grains.RazorDesigner.Common;
using Grains.RazorDesigner.Document;
using Grains.RazorDesigner.Templates;
using Sandbox;

namespace Grains.RazorDesigner.Palette;

public class PalettePanel : Widget
{
	private const string LogPrefix = "[Grains.RazorDesigner]";
	private const string CookiePrefix = "razordesigner.palette.";

	// Click-to-add target. Window decides where the new record goes (typically active selection or root).
	public event Action<ControlType> TypeAddRequested;

	// Click-to-add a saved template. Window decides where to insert.
	public event Action<PaletteTemplate> TemplateAddRequested;

	private readonly PaletteTemplateStore _templateStore = new();
	private CollapsibleSection _templatesSection;
	private WrapPanel _templatesWrap;
	public PaletteTemplateStore TemplateStore => _templateStore;

	public PalettePanel( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 0;
		Layout.Spacing = 0;
		MinimumWidth = 180;
		VerticalSizeMode = SizeMode.CanGrow;

		var byCategory = new Dictionary<ControlCategory, List<ControlType>>();
		foreach ( ControlType type in Enum.GetValues( typeof( ControlType ) ) )
		{
			var cat = ControlMetadata.Get( type ).Category;
			if ( !byCategory.TryGetValue( cat, out var list ) )
			{
				list = new List<ControlType>();
				byCategory[cat] = list;
			}
			list.Add( type );
		}

		// Templates section (top of palette). Hidden when store is empty; rebuilt on Changed.
		_templatesSection = new CollapsibleSection( this, "Templates", "bookmark" );
		_templatesWrap = new WrapPanel( null )
		{
			MinItemWidth = 92,
			ItemHeight = (int)( Theme.RowHeight + 4 ),
			HSpacing = 4,
			VSpacing = 4,
			PaddingLeft = 4,
			PaddingTop = 4,
			PaddingRight = 14,
			PaddingBottom = 4,
		};
		_templatesSection.BodyLayout.Add( _templatesWrap );

		var templatesCookie = $"{CookiePrefix}templates.expanded";
		_templatesSection.Expanded = EditorCookie.Get<bool>( templatesCookie, true );
		_templatesSection.ExpandedChanged += expanded =>
		{
			EditorCookie.Set( templatesCookie, expanded );
			Log.Info( $"{LogPrefix} Palette Templates {(expanded ? "expanded" : "collapsed")}" );
		};

		Layout.Add( _templatesSection );

		_templateStore.Changed += RebuildTemplatesSection;
		_templateStore.Scan(); // initial fill (also fires Changed and rebuilds the section)

		foreach ( ControlCategory cat in Enum.GetValues( typeof( ControlCategory ) ) )
		{
			if ( !byCategory.TryGetValue( cat, out var list ) ) continue;

			var section = new CollapsibleSection(
				this,
				ControlMetadata.CategoryDisplayName( cat ),
				CategoryIcon( cat ) );

			var wrap = new WrapPanel( null )
			{
				MinItemWidth = 92,
				ItemHeight = (int)( Theme.RowHeight + 4 ),
				HSpacing = 4,
				VSpacing = 4,
				PaddingLeft = 4,
				PaddingTop = 4,
				PaddingRight = 14,    // clear the ScrollArea's vertical scrollbar
				PaddingBottom = 4,
			};
			section.BodyLayout.Add( wrap );

			foreach ( var t in list )
				new PaletteTypeButton( wrap, this, t );

			var cookieKey = $"{CookiePrefix}{cat}.expanded";
			section.Expanded = EditorCookie.Get<bool>( cookieKey, DefaultExpanded( cat ) );
			section.ExpandedChanged += expanded =>
			{
				EditorCookie.Set( cookieKey, expanded );
				Log.Info( $"{LogPrefix} Palette category {cat} {(expanded ? "expanded" : "collapsed")}" );
			};

			Layout.Add( section );
		}

		Layout.AddStretchCell();

		Log.Info( $"{LogPrefix} PalettePanel ctor (icon grid, {byCategory.Count} categories)" );
	}

	internal void NotifyTypeClicked( ControlType type )
	{
		Log.Info( $"{LogPrefix} PalettePanel.NotifyTypeClicked: {type}" );
		TypeAddRequested?.Invoke( type );
	}

	internal void NotifyTemplateClicked( PaletteTemplate template )
	{
		Log.Info( $"{LogPrefix} PalettePanel.NotifyTemplateClicked: \"{template.Name}\"" );
		TemplateAddRequested?.Invoke( template );
	}

	internal void RequestTemplateDelete( PaletteTemplate template )
	{
		var dialog = new Editor.Dialog( this );
		dialog.Window.WindowTitle = "Delete template";
		dialog.Window.SetWindowIcon( "delete" );
		dialog.Window.SetModal( true, true );
		dialog.Window.MinimumWidth = 320;

		dialog.Layout = Layout.Column();
		dialog.Layout.Margin = 16;
		dialog.Layout.Spacing = 10;

		dialog.Layout.Add( new Editor.Label( dialog )
		{
			Text = $"Delete template \"{template.Name}\"?",
		} );

		var hint = new Editor.Label( dialog )
		{
			Text = "Already-instantiated copies in open documents are unaffected.",
		};
		hint.SetStyles( "color: #888; font-size: 11px;" );
		dialog.Layout.Add( hint );

		var buttonRow = dialog.Layout.Add( Layout.Row() );
		buttonRow.Spacing = 6;
		buttonRow.AddStretchCell();

		var cancel = new Editor.Button( dialog ) { Text = "Cancel", MinimumWidth = 72 };
		cancel.MouseLeftPress += () => dialog.Close();
		buttonRow.Add( cancel );

		var del = new Editor.Button( dialog ) { Text = "Delete", MinimumWidth = 72 };
		del.SetStyles( "color: #e07070;" );
		del.MouseLeftPress += () =>
		{
			Log.Info( $"{LogPrefix} Palette delete confirmed: \"{template.Name}\"" );
			_templateStore.Delete( template );
			dialog.Close();
		};
		buttonRow.Add( del );

		dialog.Window.AdjustSize();
		dialog.Show();
	}

	private void RebuildTemplatesSection()
	{
		var templates = _templateStore.All;

		// Hide the entire section (header + body) when there are no templates.
		_templatesSection.Visible = templates.Count > 0;

		// Bulk-mutate inside SuspendUpdates: hides the wrap during DestroyChildren + AddChild,
		// then Dispose restores visibility — that Hidden→visible transition fires a Show event
		// which triggers a fresh Qt layout cascade. Engine canonical idiom (HammerManagedInspector,
		// Widget.cs:1248 docs). Plain Update()/UpdateGeometry alone weren't propagating the
		// FixedHeight change upstream because the parent Qt layout never re-requested.
		using ( Editor.SuspendUpdates.For( _templatesWrap ) )
		{
			_templatesWrap.DestroyChildren();
			foreach ( var t in templates )
				new PaletteTemplateButton( _templatesWrap, this, t );
		}

		// After SuspendUpdates dispose, force one more layout pass + propagate upstream so
		// the section body and palette scroll-area canvas re-measure us with the new height.
		_templatesWrap.Relayout();
		_templatesWrap.UpdateGeometry();
		_templatesSection.UpdateGeometry();
		UpdateGeometry();

		Log.Info( $"{LogPrefix} PalettePanel.RebuildTemplatesSection: {templates.Count} tile(s), section.Visible={_templatesSection.Visible}" );
	}

	private static bool DefaultExpanded( ControlCategory cat ) =>
		cat is ControlCategory.Layout or ControlCategory.Display or ControlCategory.Input;

	private static string CategoryIcon( ControlCategory cat ) => cat switch
	{
		ControlCategory.Layout   => "view_quilt",
		ControlCategory.Display  => "visibility",
		ControlCategory.Input    => "edit",
		ControlCategory.Form     => "list_alt",
		_ => "category",
	};

	// ToolboxItem-style icon button. Hover paint, drag source, click-to-add.
	// Reference: addons/tools/Code/Editor/DooEditor/DooToolbox.cs:37
	private sealed class PaletteTypeButton : Widget
	{
		private readonly PalettePanel _owner;
		private readonly ControlType _type;
		private readonly ControlMeta _meta;

		public PaletteTypeButton( Widget parent, PalettePanel owner, ControlType type ) : base( parent )
		{
			_owner = owner;
			_type = type;
			_meta = ControlMetadata.Get( type );

			ToolTip = type.ToString();
			Cursor = CursorShape.Finger;
			MouseTracking = true;
			IsDraggable = true;
		}

		protected override void OnPaint()
		{
			var rect = LocalRect.Shrink( 1 );
			Paint.Antialiasing = true;
			Paint.TextAntialiasing = true;

			// Tint the whole button (fill + border) by category so the container/leaf split
			// reads at a glance, not just from the icon. Fill is dilute so the saturated icon
			// still pops; border is louder so the chip silhouette carries the cue too.
			var tint = ControlPresentation.IconTint( _type );
			var fillAlpha = Paint.HasMouseOver ? 0.35f : 0.15f;
			var borderAlpha = Paint.HasMouseOver ? 0.55f : 0.25f;
			Paint.SetBrush( tint.WithAlpha( fillAlpha ) );
			Paint.SetPen( tint.WithAlpha( borderAlpha ) );
			Paint.DrawRect( rect, 3 );

			var hoverOpacity = Paint.HasMouseOver ? 1f : 0.85f;
			var iconRect = new Rect( rect.Left + 4, rect.Top, 20, rect.Height );
			Paint.SetPen( tint.WithAlphaMultiplied( hoverOpacity ) );
			Paint.DrawIcon( iconRect, _meta.IconName, 16, TextFlag.Center );

			var textRect = rect;
			textRect.Left = iconRect.Right + 2;
			textRect.Right -= 4;
			Paint.SetPen( Theme.Text.WithAlphaMultiplied( hoverOpacity ) );
			Paint.SetDefaultFont();
			Paint.DrawText( textRect, _type.ToString(), TextFlag.LeftCenter );
		}

		protected override void OnMouseClick( MouseEvent e )
		{
			base.OnMouseClick( e );
			if ( e.LeftMouseButton )
				_owner.NotifyTypeClicked( _type );
		}

		protected override void OnDragStart()
		{
			base.OnDragStart();

			var drag = new Drag( this );
			drag.Data.Object = _type;
			drag.Data.Text = $"palette:{_type}";
			drag.Execute();

			Log.Info( $"{LogPrefix} PaletteTypeButton.OnDragStart: {_type}" );
		}
	}

	// Mirrors PaletteTypeButton: tinted chip + icon + name. Drag payload is a PaletteTemplate
	// reference (drop receivers discriminate by Drag.Data.Object runtime type). Click-to-add
	// via PalettePanel.NotifyTemplateClicked. Right-click -> confirm-and-delete dialog.
	private sealed class PaletteTemplateButton : Widget
	{
		private readonly PalettePanel _owner;
		private readonly PaletteTemplate _template;

		public PaletteTemplateButton( Widget parent, PalettePanel owner, PaletteTemplate template ) : base( parent )
		{
			_owner = owner;
			_template = template;

			ToolTip = template.Name;
			Cursor = CursorShape.Finger;
			MouseTracking = true;
			IsDraggable = true;
		}

		protected override void OnPaint()
		{
			var rect = LocalRect.Shrink( 1 );
			Paint.Antialiasing = true;
			Paint.TextAntialiasing = true;

			// Visuals match PaletteTypeButton; tint is warm amber to distinguish user-
			// authored templates from built-in controls at a glance. (Supersedes design-
			// doc #9's "no tint, no badge" — the neutral grey blended too well into the
			// built-in chips and templates need to stand out as a distinct palette tier.
			// Warm against the cool container/leaf/pseudoclass tints.)
			var tint = ControlPresentation.TemplateTint;
			var fillAlpha = Paint.HasMouseOver ? 0.18f : 0.08f;
			var borderAlpha = Paint.HasMouseOver ? 0.55f : 0.25f;
			Paint.SetBrush( tint.WithAlpha( fillAlpha ) );
			Paint.SetPen( tint.WithAlpha( borderAlpha ) );
			Paint.DrawRect( rect, 3 );

			var hoverOpacity = Paint.HasMouseOver ? 1f : 0.85f;
			var iconRect = new Rect( rect.Left + 4, rect.Top, 20, rect.Height );
			var icon = string.IsNullOrEmpty( _template.IconName ) ? "bookmark" : _template.IconName;
			Paint.SetPen( tint.WithAlphaMultiplied( hoverOpacity ) );
			Paint.DrawIcon( iconRect, icon, 16, TextFlag.Center );

			var textRect = rect;
			textRect.Left = iconRect.Right + 2;
			textRect.Right -= 4;
			Paint.SetPen( Theme.Text.WithAlphaMultiplied( hoverOpacity ) );
			Paint.SetDefaultFont();
			Paint.DrawText( textRect, _template.Name, TextFlag.LeftCenter );
		}

		protected override void OnMouseClick( MouseEvent e )
		{
			base.OnMouseClick( e );
			if ( e.LeftMouseButton )
				_owner.NotifyTemplateClicked( _template );
		}

		protected override void OnContextMenu( ContextMenuEvent e )
		{
			base.OnContextMenu( e );
			var menu = new Menu( this );
			if ( _template.IsReadOnly )
			{
				// Bundled templates (Assets/Templates/Included/) ship with the addon and aren't
				// the user's to delete. Show the option disabled so users don't wonder why the
				// context menu is empty.
				menu.AddOption( new Option
				{
					Text = "Delete… (bundled)",
					Icon = "delete",
					Enabled = false,
				} );
			}
			else
			{
				menu.AddOption( "Delete…", "delete", () => _owner.RequestTemplateDelete( _template ) );
			}
			menu.OpenAtCursor();
			e.Accepted = true;
		}

		protected override void OnDragStart()
		{
			base.OnDragStart();
			var drag = new Drag( this );
			drag.Data.Object = _template;
			drag.Data.Text = $"template:{_template.Name}";
			drag.Execute();
			Log.Info( $"{LogPrefix} PaletteTemplateButton.OnDragStart: \"{_template.Name}\"" );
		}
	}
}
