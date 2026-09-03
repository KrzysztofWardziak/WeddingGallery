using WeddingGallery.Application.Events;

namespace WeddingGallery.Application.Tests.Events;

public class SlugGeneratorTests
{
    [Fact]
    public void Joins_words_with_hyphens_and_keeps_the_capitalisation()
    {
        Assert.Equal("Katarzyna-i-Krzysztof", SlugGenerator.Generate("Katarzyna i Krzysztof"));
    }

    [Theory]
    [InlineData("Zuzanna i Michał", "Zuzanna-i-Michal")]
    [InlineData("Łucja i Paweł", "Lucja-i-Pawel")]
    [InlineData("Gośka i Żaneta", "Goska-i-Zaneta")]
    [InlineData("Ćma i Śnieg", "Cma-i-Snieg")]
    [InlineData("Bożena i Źródło", "Bozena-i-Zrodlo")]
    public void Strips_polish_diacritics_down_to_their_base_letters(string name, string expected)
    {
        // The QR code URL has to survive being copied out of a browser bar: a slug with
        // diacritics percent-encodes into noise like %C5%82.
        Assert.Equal(expected, SlugGenerator.Generate(name));
    }

    [Theory]
    [InlineData("Ania & Tomek 2026", "Ania-Tomek-2026")]
    [InlineData("Ania   i    Tomek", "Ania-i-Tomek")]
    [InlineData("  Ania i Tomek  ", "Ania-i-Tomek")]
    [InlineData("Ania/Tomek: wesele!", "Ania-Tomek-wesele")]
    [InlineData("---Ania---", "Ania")]
    public void Collapses_everything_outside_letters_and_digits_into_single_hyphens(string name, string expected)
    {
        Assert.Equal(expected, SlugGenerator.Generate(name));
    }

    [Fact]
    public void Caps_the_length_and_never_ends_on_a_hyphen()
    {
        var slug = SlugGenerator.Generate(new string('a', SlugGenerator.MaxLength + 10));

        Assert.Equal(SlugGenerator.MaxLength, slug.Length);

        var truncatedMidSeparator = SlugGenerator.Generate(new string('a', SlugGenerator.MaxLength) + " Tomek");

        Assert.Equal(SlugGenerator.MaxLength, truncatedMidSeparator.Length);
        Assert.DoesNotContain("-", truncatedMidSeparator);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!! ???")]
    [InlineData("😀🎉")]
    public void Returns_empty_when_the_name_leaves_nothing_usable(string name)
    {
        // EventService reads an empty result as "fall back to a random slug" rather than
        // writing a blank path segment.
        Assert.Equal(string.Empty, SlugGenerator.Generate(name));
    }
}
