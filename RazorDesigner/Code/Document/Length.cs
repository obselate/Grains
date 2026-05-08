namespace Grains.RazorDesigner.Document;

// No fr unit — sbox is flexbox, not CSS Grid; use FlexGrow for fractional sizing.
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
}

public enum LengthUnit { Px, Percent, Auto, Rem, Em }
