using System.Globalization;

namespace RobControl.Core.Trending;

/// <summary>Turns what a controller prints for a value into a number: 12, -3.5, 1.2E+03, ON/OFF, TRUE/FALSE.</summary>
public static class ScalarParser
{
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // The value is the first word; a register's comment can follow it ("1287  'Weld count'").
        string word = text.Trim().Split([' ', '\t'], 2)[0].Trim('\'', '"');

        switch (word.ToUpperInvariant())
        {
            case "ON":
            case "TRUE":
                value = 1;
                return true;
            case "OFF":
            case "FALSE":
                value = 0;
                return true;
        }

        return double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }
}
