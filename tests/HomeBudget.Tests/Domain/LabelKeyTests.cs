using HomeBudget.Api.Domain.Rules;

namespace HomeBudget.Tests.Domain;

public sealed class LabelKeyTests
{
    [Theory]
    [InlineData("PRLV SEPA MACIF SA ECH/251025 ID EMETTEUR/FR12ZZZ123", "MACIF SA")]
    [InlineData("PRLV SEPA MACIF SA ECH/251125 ID EMETTEUR/FR12ZZZ123", "MACIF SA")]
    [InlineData("ACHAT CB SUPERMARCHE   23.11.26", "SUPERMARCHE")]
    [InlineData("PAIEMENT PAR CARTE X1234 CARREFOUR MARKET 12/10", "CARREFOUR MARKET")]
    [InlineData("CB CARREFOUR MARKET 03/11", "CARREFOUR MARKET")]
    [InlineData("VIREMENT DE EMPLOYEUR SA", "EMPLOYEUR SA")]
    [InlineData("VIR SEPA RECU DE M DUPONT JEAN LOYER NOVEMBRE", "DUPONT JEAN LOYER")]
    [InlineData("Prélèvement Électricité de France", "ELECTRICITE FRANCE")]
    [InlineData("CHEQUE 1234567", "CHEQUE")]
    public void Key_keeps_the_meaningful_words_of_the_bank_label(string label, string expected) =>
        LabelKey.From(label).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CB 12/10 X1234")]
    public void Label_without_meaningful_word_has_an_empty_key(string? label) =>
        LabelKey.From(label).ShouldBeEmpty();

    [Fact]
    public void Key_length_is_bounded()
    {
        var label = string.Join(' ', Enumerable.Repeat(new string('A', 60), 3));
        LabelKey.From(label).Length.ShouldBeLessThanOrEqualTo(LabelKey.MaxLength);
    }
}
