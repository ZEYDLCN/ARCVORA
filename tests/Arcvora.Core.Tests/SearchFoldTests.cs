using Arcvora.Core.Text;

namespace Arcvora.Core.Tests;

public class SearchFoldTests
{
    [Theory]
    [InlineData("ışıl", "isil")]
    [InlineData("Işıl", "Isil")]
    [InlineData("İŞİL", "ISIL")]
    [InlineData("öztürk ÖZTÜRK", "ozturk OZTURK")]
    [InlineData("çağrı ÇAĞRI", "cagri CAGRI")]
    [InlineData("Şirket gideri: İstanbul", "Sirket gideri: Istanbul")]
    [InlineData("zaten ascii 123 _%", "zaten ascii 123 _%")]
    [InlineData("", "")]
    public void Turkish_letters_become_ascii_and_everything_else_is_kept(string input, string expected) =>
        Assert.Equal(expected, SearchFold.Ascii(input));

    [Fact]
    public void Null_is_treated_as_empty() => Assert.Equal("", SearchFold.Ascii(null));

    [Fact]
    public void Other_unicode_letters_are_left_alone()
    {
        // Yalnızca Türkçe'ye özgü 12 harf katlanır; başka alfabeler/aksanlar değişmez.
        Assert.Equal("é 日本 ñ ä ß", SearchFold.Ascii("é 日本 ñ ä ß"));
    }

    [Fact]
    public void Folding_is_idempotent()
    {
        var once = SearchFold.Ascii("Işıl Öztürk Şirket Çağrı Ğ");
        Assert.Equal(once, SearchFold.Ascii(once));
    }
}
