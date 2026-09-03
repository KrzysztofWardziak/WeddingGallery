using System.Globalization;
using System.Text;

namespace WeddingGallery.Application.Events;

/// <summary>
/// Turns an event name into the path segment guests see on the printed QR card:
/// "Katarzyna i Krzysztof" becomes "Katarzyna-i-Krzysztof".
/// </summary>
public static class SlugGenerator
{
    /// <summary>
    /// The Slug column holds 50 characters; stopping at 40 leaves room for the "-2", "-3"
    /// suffix <see cref="Services.EventService"/> appends when two events share a name.
    /// </summary>
    public const int MaxLength = 40;

    /// <summary>
    /// Produces the slug for <paramref name="name"/>, or an empty string when the name
    /// contains no letter or digit that survives the transformation.
    /// </summary>
    public static string Generate(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var decomposed = MapIndivisibleLetters(name).Normalize(NormalizationForm.FormD);
        var slug = new StringBuilder(MaxLength);

        foreach (var character in decomposed)
        {
            // The accent that FormD just split off its base letter.
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (IsAsciiLetterOrDigit(character))
            {
                if (slug.Length == MaxLength) break;
                slug.Append(character);
            }
            else if (slug.Length > 0 && slug.Length < MaxLength && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        // A separator can be left dangling either by trailing punctuation or by the length
        // cap cutting in before the next word.
        return slug.ToString().TrimEnd('-');
    }

    // FormD decomposes most Polish letters into a base letter plus a combining mark, but the
    // stroked L is a single indivisible code point, so it has to be mapped by hand.
    private static string MapIndivisibleLetters(string name) =>
        name.Replace('ł', 'l').Replace('Ł', 'L');

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
}
