namespace TessynDesktop.WPF.State;

/// <summary>A colour as hue (0-360), saturation and value (0-1) and alpha (0-255), with the conversions to and from hex. No WPF types, so the picker's maths is testable anywhere.</summary>
/// <remarks>Created by Claude</remarks>
public readonly record struct Hsv(double H, double S, double V, byte A = 255)
{
	/// <summary>The colour of a validated hex string (<c>#RGB</c>, <c>#RRGGBB</c> or <c>#AARRGGBB</c>).</summary>
	public static Hsv FromHex(string hex)
	{
		var normal = ColourCatalog.Normalize(hex);
		var alpha = normal.Length == 9 ? Convert.ToByte(normal.Substring(1, 2), 16) : (byte)255;
		var rgb = normal.Substring(normal.Length - 6);
		return FromRgb(Convert.ToByte(rgb.Substring(0, 2), 16), Convert.ToByte(rgb.Substring(2, 2), 16), Convert.ToByte(rgb.Substring(4, 2), 16), alpha);
	}

	public static Hsv FromRgb(byte r, byte g, byte b, byte a = 255)
	{
		double red = r / 255.0, green = g / 255.0, blue = b / 255.0;
		var max = Math.Max(red, Math.Max(green, blue));
		var min = Math.Min(red, Math.Min(green, blue));
		var delta = max - min;
		double hue;
		if (delta < 1e-9)
			hue = 0;
		else if (max == red)
			hue = 60 * (((green - blue) / delta % 6 + 6) % 6);
		else if (max == green)
			hue = 60 * ((blue - red) / delta + 2);
		else
			hue = 60 * ((red - green) / delta + 4);

		return new Hsv(hue, max < 1e-9 ? 0 : delta / max, max, a);
	}

	/// <summary>The red, green and blue bytes.</summary>
	public (byte R, byte G, byte B) ToRgb()
	{
		var h = (H % 360 + 360) % 360;
		var s = Math.Clamp(S, 0, 1);
		var v = Math.Clamp(V, 0, 1);
		var c = v * s;
		var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
		var m = v - c;
		var (r, g, b) = (int)(h / 60) switch
		{
			0 => (c, x, 0.0),
			1 => (x, c, 0.0),
			2 => (0.0, c, x),
			3 => (0.0, x, c),
			4 => (x, 0.0, c),
			_ => (c, 0.0, x),
		};

		return (To(r + m), To(g + m), To(b + m));

		static byte To(double channel) => (byte)Math.Clamp((int)Math.Round(channel * 255), 0, 255);
	}

	/// <summary><c>#RRGGBB</c>, or <c>#AARRGGBB</c> when the colour is not opaque.</summary>
	public string ToHex()
	{
		var (r, g, b) = ToRgb();
		return A == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{A:X2}{r:X2}{g:X2}{b:X2}";
	}
}
