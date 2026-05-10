using System;
using System.Globalization;

namespace Grains.RazorDesigner.Document;

// No fr unit: sbox is flexbox not CSS Grid. Use FlexGrow for fractional sizing.
public readonly record struct Length( float Value, LengthUnit Unit )
{
	public static Length Auto => new( 0f, LengthUnit.Auto );
	public static Length Px( float v ) => new( v, LengthUnit.Px );
	public static Length Percent( float v ) => new( v, LengthUnit.Percent );
	public static Length Rem( float v ) => new( v, LengthUnit.Rem );
	public static Length Em( float v ) => new( v, LengthUnit.Em );

	public string ToCss() => Unit switch
	{
		LengthUnit.Auto    => "auto",
		LengthUnit.Px      => $"{Value:0.##}px",
		LengthUnit.Percent => $"{Value:0.##}%",
		LengthUnit.Rem     => $"{Value:0.##}rem",
		LengthUnit.Em      => $"{Value:0.##}em",
		_                  => "auto",
	};

	public override string ToString() => ToCss();

	// Tolerant: case-insensitive, trailing/leading whitespace ignored, bare number -> Px.
	// Throws FormatException on unrecognised input. Use TryParse for non-throwing.
	public static Length Parse( string s )
	{
		if ( !TryParse( s, out var v ) )
			throw new FormatException( $"Length.Parse: cannot parse \"{s}\"" );
		return v;
	}

	public static bool TryParse( string s, out Length result )
	{
		result = Auto;
		if ( string.IsNullOrWhiteSpace( s ) ) return false;
		var t = s.Trim();
		if ( t.Equals( "auto", StringComparison.OrdinalIgnoreCase ) )
		{
			result = Auto;
			return true;
		}

		// Strip suffix; whatever's left must parse as a float.
		string suffix = "";
		int splitAt = t.Length;
		for ( int i = 0; i < t.Length; i++ )
		{
			var c = t[i];
			if ( char.IsDigit( c ) || c == '.' || c == '-' || c == '+' ) continue;
			splitAt = i;
			break;
		}
		var numPart = t.Substring( 0, splitAt );
		suffix = t.Substring( splitAt ).Trim().ToLowerInvariant();

		if ( !float.TryParse( numPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var num ) )
			return false;

		result = suffix switch
		{
			""    => Px( num ),  // bare number -> px (forgiving)
			"px"  => Px( num ),
			"%"   => Percent( num ),
			"rem" => Rem( num ),
			"em"  => Em( num ),
			_     => default,
		};
		return suffix is "" or "px" or "%" or "rem" or "em";
	}
}

public enum LengthUnit { Px, Percent, Auto, Rem, Em }
