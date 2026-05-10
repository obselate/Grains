using System;
using System.Collections.Generic;
using System.IO;
using Editor;
using Sandbox;

namespace Grains.RazorDesigner.Templates;

public sealed class PaletteTemplateStore
{
	private const string LogPrefix = "[Grains.RazorDesigner]";

	// Per-user (writable) — saved templates land here in the user's currently-loaded project.
	private const string UserTemplatesSubdir = "RazorDesigner/Templates";

	// Bundled (read-only) — ships with the addon. Same dual-context lookup used for themes
	// (DesignerWindow.BundledThemesDirectoryPath): published consumer Ident + dev workspace
	// Ident, first existing wins.
	private static readonly (string Ident, string Subpath)[] BundledTemplateRoots = new[]
	{
		( "razordesigner",        "Assets/Templates/Included" ),
		( "grains_razordesigner", "Libraries/xaz.razordesigner/Assets/Templates/Included" ),
	};

	private readonly List<PaletteTemplate> _all = new();

	public IReadOnlyList<PaletteTemplate> All => _all;
	public event Action Changed;

	// Returns absolute path; null when no project is loaded (defensive — shouldn't happen
	// in editor flows but Project.Current is documented to be nullable).
	public string TemplatesDirectoryPath
	{
		get
		{
			var assetsRoot = Project.Current?.GetAssetsPath();
			if ( string.IsNullOrEmpty( assetsRoot ) ) return null;
			return Path.Combine( assetsRoot, UserTemplatesSubdir );
		}
	}

	public static string BundledTemplatesDirectoryPath()
	{
		foreach ( var (ident, subpath) in BundledTemplateRoots )
		{
			var root = System.Linq.Enumerable
				.FirstOrDefault( EditorUtility.Projects.GetAll(), p => string.Equals( p.Config?.Ident, ident, StringComparison.OrdinalIgnoreCase ) )
				?.GetRootPath();
			if ( string.IsNullOrEmpty( root ) ) continue;
			var dir = Path.Combine( root, subpath );
			if ( Directory.Exists( dir ) ) return dir;
		}
		return null;
	}

	public void Scan()
	{
		_all.Clear();

		var bundledDir = BundledTemplatesDirectoryPath();
		var bundledCount = ScanDirectory( bundledDir, isReadOnly: true );

		var userDir = TemplatesDirectoryPath;
		if ( string.IsNullOrEmpty( userDir ) )
			Log.Warning( $"{LogPrefix} TemplateStore.Scan: no project assets path available; user templates skipped" );
		var userCount = ScanDirectory( userDir, isReadOnly: false );

		Log.Info( $"{LogPrefix} TemplateStore.Scan: {bundledCount} bundled + {userCount} user = {_all.Count} total" );
		Changed?.Invoke();
	}

	// Reads *.json from `dir`; tags each loaded template with isReadOnly. Returns the count
	// added (0 if dir is null/missing). Per-file failures swallowed-and-warned so one bad file
	// doesn't poison the others.
	private int ScanDirectory( string dir, bool isReadOnly )
	{
		if ( string.IsNullOrEmpty( dir ) || !Directory.Exists( dir ) ) return 0;

		var added = 0;
		var files = Directory.GetFiles( dir, "*.json", SearchOption.TopDirectoryOnly );
		foreach ( var path in files )
		{
			PaletteTemplate t;
			try
			{
				var json = File.ReadAllText( path );
				t = PaletteTemplateSerializer.Deserialize( json, path );
			}
			catch ( PaletteTemplateException ex )
			{
				Log.Warning( $"{LogPrefix} TemplateStore.Scan: skipping {Path.GetFileName( path )} ({ex.Message})" );
				continue;
			}
			catch ( IOException ex )
			{
				Log.Warning( $"{LogPrefix} TemplateStore.Scan: cannot read {Path.GetFileName( path )} ({ex.Message})" );
				continue;
			}
			if ( isReadOnly )
				t = t with { IsReadOnly = true };
			_all.Add( t );
			added++;
		}
		return added;
	}

	// Caller validates Name uniqueness via NameExists before calling. We re-check here
	// only for race safety on subsequent open-dock scans.
	public void Save( PaletteTemplate template )
	{
		if ( template is null ) throw new ArgumentNullException( nameof( template ) );

		var dir = TemplatesDirectoryPath;
		if ( string.IsNullOrEmpty( dir ) )
		{
			Log.Warning( $"{LogPrefix} TemplateStore.Save: no project assets path; cannot save \"{template.Name}\"" );
			return;
		}

		Directory.CreateDirectory( dir );
		var fileName = SanitiseFilename( template.Name ) + ".json";
		var fullPath = Path.Combine( dir, fileName );

		// Persist with the actual file path baked in (deserialize uses it as identity).
		var stamped = template with { FilePath = fullPath };
		var json = PaletteTemplateSerializer.Serialize( stamped );

		try
		{
			File.WriteAllText( fullPath, json );
			Log.Info( $"{LogPrefix} TemplateStore.Save: \"{template.Name}\" -> {fullPath}" );
		}
		catch ( IOException ex )
		{
			Log.Warning( $"{LogPrefix} TemplateStore.Save: write failed for {fullPath} ({ex.Message})" );
			return;
		}

		Scan(); // refreshes _all and fires Changed
	}

	public void Delete( PaletteTemplate template )
	{
		if ( template is null ) throw new ArgumentNullException( nameof( template ) );
		if ( string.IsNullOrEmpty( template.FilePath ) ) return;
		if ( template.IsReadOnly )
		{
			Log.Warning( $"{LogPrefix} TemplateStore.Delete: \"{template.Name}\" is bundled (read-only); refused" );
			return;
		}

		try
		{
			if ( File.Exists( template.FilePath ) )
				File.Delete( template.FilePath );
			Log.Info( $"{LogPrefix} TemplateStore.Delete: \"{template.Name}\" removed ({template.FilePath})" );
		}
		catch ( IOException ex )
		{
			Log.Warning( $"{LogPrefix} TemplateStore.Delete: failed for {template.FilePath} ({ex.Message})" );
			return;
		}

		Scan();
	}

	public bool NameExists( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return false;
		foreach ( var t in _all )
		{
			if ( string.Equals( t.Name, name, StringComparison.OrdinalIgnoreCase ) )
				return true;
		}
		return false;
	}

	// Belt-and-braces: dialog already blocks `/ \ : * ? " < > |`, but a Name could still
	// be e.g. "  " or end with "." which Windows rejects. Fall through to "Untitled" so
	// the user gets a save rather than a silent failure.
	private static string SanitiseFilename( string name )
	{
		var trimmed = (name ?? "").Trim();
		if ( string.IsNullOrEmpty( trimmed ) ) return "Untitled";

		var invalid = Path.GetInvalidFileNameChars();
		var chars = trimmed.ToCharArray();
		for ( int i = 0; i < chars.Length; i++ )
		{
			foreach ( var c in invalid )
				if ( chars[i] == c ) chars[i] = '_';
		}
		var result = new string( chars ).Trim().TrimEnd( '.' );
		return string.IsNullOrEmpty( result ) ? "Untitled" : result;
	}
}
