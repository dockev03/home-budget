using System.Text;

using HomeBudget.Api.Domain.Imports;
using HomeBudget.Api.Domain.Recurrences;

namespace HomeBudget.Tests.Domain;

public sealed class StatementReaderTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static decimal Dec(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Profil de l'export CSV de La Banque Postale (même structure que l'export réel, données fictives).</summary>
    private static readonly CsvOptions BanquePostale = new(';', "dd/MM/yyyy", ',', 1, 2, 3, null, null);

    /// <summary>Export CSV fictif au format de La Banque Postale : en-tête du relevé, ligne vide, titres, opérations.</summary>
    public const string BanquePostaleCsv =
        "Numéro Compte   ;0000000X000\r\n"
        + "Type         ;CCP\r\n"
        + "Compte tenu en  ;euros\r\n"
        + "Date            ;25/11/2026\r\n"
        + "Solde (EUROS)   ;1 234,56\r\n"
        + "\r\n"
        + "Date;Libellé;Montant(EUROS)\r\n"
        + "24/11/2026;\"ACHAT CB SUPERMARCHE   23.11.26 CARTE NUMERO                123  \";-29,99\r\n"
        + "23/11/2026;\"PRELEVEMENT DE OPERATEUR TEL REF : 123456 Votre abonnement\";-15,99\r\n"
        + "23/11/2026;\"VIREMENT DE EMPLOYEUR SA REFERENCE : 0000001 \";2 500,00\r\n"
        + "12/11/2026;\"CHEQUE N  1234567\";-10,50\r\n";

    /// <summary>Export OFX 1.0.2 (SGML) fictif au format de La Banque Postale : une seule ligne, balises de valeur non fermées.</summary>
    public const string BanquePostaleOfx =
        "OFXHEADER:100\r\nDATA:OFXSGML\r\nVERSION:102\r\nSECURITY:NONE\r\nENCODING:USASCII\r\nCHARSET:1252\r\n"
        + "COMPRESSION:NONE\r\nOLDFILEUID:NONE\r\nNEWFILEUID:NONE\r\n"
        + "<OFX><SIGNONMSGSRSV1><SONRS><STATUS><CODE>0<SEVERITY>INFO</STATUS><DTSERVER>20261125134013<LANGUAGE>FRE</SONRS></SIGNONMSGSRSV1>"
        + "<BANKMSGSRSV1><STMTTRNRS><TRNUID>1<STATUS><CODE>0<SEVERITY>INFO</STATUS><STMTRS><CURDEF>EUR"
        + "<BANKACCTFROM><BANKID>00000<BRANCHID>00000<ACCTID>0000000X000<ACCTTYPE>CHECKING</BANKACCTFROM>"
        + "<BANKTRANLIST><DTSTART>20261101<DTEND>20261125"
        + "<STMTTRN><TRNTYPE>POS<DTPOSTED>20261124<TRNAMT>-29.99<FITID>AB1$C2%D@<NAME>ACHAT CB SUPERMARCHE   23.11.26</STMTTRN>"
        + "<STMTTRN><TRNTYPE>PAYMENT<DTPOSTED>20261123<TRNAMT>-15.99<FITID>AB1$C2%D2<NAME>PRELEVEMENT DE OPERATEUR TEL</STMTTRN>"
        + "<STMTTRN><TRNTYPE>DIRECTDEP<DTPOSTED>20261123<TRNAMT>2500.00<FITID>AB1$C2%D3<NAME>VIREMENT DE EMPLOYEUR SA</STMTTRN>"
        + "<STMTTRN><TRNTYPE>CHECK<DTPOSTED>20261112<TRNAMT>-10.50<FITID>AB1$C2%D4<CHECKNUM>1234567<NAME>CHEQUE</STMTTRN>"
        + "</BANKTRANLIST><LEDGERBAL><BALAMT>1234.56<DTASOF>20261124</LEDGERBAL></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>";

    [Fact]
    public void Banque_Postale_csv_in_windows_1252_is_read_after_its_header()
    {
        var text = StatementText.Decode(Encoding.Latin1.GetBytes(BanquePostaleCsv));
        text.ShouldContain("Libellé");
        StatementText.DetectFormat("relevé.csv", text).ShouldBe(ImportFormat.Csv);

        var result = CsvStatementReader.Read(text, BanquePostale);

        result.Errors.ShouldBeEmpty();
        result.Lines.Count.ShouldBe(4);
        result.Lines[0].ShouldBe(new StatementLine(8, D(2026, 11, 24), -29.99m, "ACHAT CB SUPERMARCHE 23.11.26 CARTE NUMERO 123", null));
        result.Lines[2].Amount.ShouldBe(2500m);
        result.Lines[3].Label.ShouldBe("CHEQUE N 1234567");
    }

    [Fact]
    public void Csv_reports_unreadable_lines_after_the_first_date_and_reads_debit_credit_columns()
    {
        const string text = "Date,Libellé,Débit,Crédit\n2026-11-02,\"Salaire, novembre\",,\"2,500.00\"\n2026-11-03,Loyer,800.00,\nbad,Ligne,1.00,\n2026-11-04,Vide,,\n";
        var options = new CsvOptions(',', "yyyy-MM-dd", '.', 1, 2, null, 3, 4);

        var result = CsvStatementReader.Read(text, options);

        result.Lines.Select(l => (l.Label, l.Amount)).ShouldBe([("Salaire, novembre", 2500m), ("Loyer", -800m)]);
        result.Errors.Select(e => e.Line).ShouldBe([4, 5]);
    }

    [Fact]
    public void Tsv_and_quoted_fields_with_line_breaks_are_supported()
    {
        const string text = "date\tlabel\tamount\r\n01/11/2026\t\"Ligne\r\nsur deux\"\t-1,5\r\n02/11/2026\t\"Dit \"\"bonjour\"\"\"\t3\r\n";
        var result = CsvStatementReader.Read(text, new CsvOptions('\t', "dd/MM/yyyy", ',', 1, 2, 3, null, null));

        result.Errors.ShouldBeEmpty();
        result.Lines.Select(l => (l.Line, l.Label, l.Amount)).ShouldBe([(2, "Ligne sur deux", -1.5m), (4, "Dit \"bonjour\"", 3m)]);
    }

    [Fact]
    public void Csv_without_any_readable_date_explains_what_to_check()
    {
        var result = CsvStatementReader.Read("a;b;c\n1;2;3\n", BanquePostale);

        result.Lines.ShouldBeEmpty();
        result.Errors.ShouldHaveSingleItem().Message.ShouldContain("Aucune date lisible");
    }

    [Fact]
    public void Banque_Postale_sgml_ofx_is_read_with_fitid()
    {
        StatementText.DetectFormat("export.txt", BanquePostaleOfx).ShouldBe(ImportFormat.Ofx);

        var result = OfxStatementReader.Read(BanquePostaleOfx);

        result.Errors.ShouldBeEmpty();
        result.Lines.Count.ShouldBe(4);
        result.Lines[0].ShouldBe(new StatementLine(1, D(2026, 11, 24), -29.99m, "ACHAT CB SUPERMARCHE 23.11.26", "AB1$C2%D@"));
        result.Lines[2].Amount.ShouldBe(2500m);
        result.Lines[3].Label.ShouldBe("CHEQUE");
    }

    [Fact]
    public void Xml_ofx_with_memo_and_entities_is_read()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <?OFX OFXHEADER="200" VERSION="211"?>
            <OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><BANKTRANLIST>
              <STMTTRN><TRNTYPE>DEBIT</TRNTYPE><DTPOSTED>20261105120000.000[+1:CET]</DTPOSTED><TRNAMT>-12,30</TRNAMT>
                <FITID>X1</FITID><NAME>Café &amp; Thé</NAME><MEMO>Carte 1234</MEMO></STMTTRN>
              <STMTTRN><TRNTYPE>DEBIT</TRNTYPE><DTPOSTED>2026</DTPOSTED><TRNAMT>-1</TRNAMT><FITID>X2</FITID></STMTTRN>
            </BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>
            """;

        var result = OfxStatementReader.Read(xml);

        result.Lines.ShouldHaveSingleItem().ShouldBe(new StatementLine(1, D(2026, 11, 5), -12.30m, "Café & Thé Carte 1234", "X1"));
        result.Errors.ShouldHaveSingleItem().Line.ShouldBe(2);
    }

    [Fact]
    public void Qif_is_read_with_french_dates_and_both_decimal_separators()
    {
        const string qif = "!Type:Bank\nD25/11/2026\nT-1,234.56\nPLoyer\nMNovembre\n^\nD26/11'26\nT15,20\nPRemboursement\n^\nDdemain\nT1\n^\n";
        StatementText.DetectFormat("export.dat", qif).ShouldBe(ImportFormat.Qif);

        var result = QifStatementReader.Read(qif);

        result.Lines.Select(l => (l.Line, l.Date, l.Amount, l.Label)).ShouldBe(
            [(2, D(2026, 11, 25), -1234.56m, "Loyer Novembre"), (7, D(2026, 11, 26), 15.20m, "Remboursement")]);
        result.Errors.ShouldHaveSingleItem().Line.ShouldBe(11);
    }

    [Theory]
    [InlineData("-1 234,56", ',', "-1234.56")]
    [InlineData("1.234,56", ',', "1234.56")]
    [InlineData("−12,00 €", ',', "-12")]
    [InlineData("1,234.56", '.', "1234.56")]
    public void Amounts_are_parsed_with_their_decimal_separator(string text, char separator, string expected)
    {
        StatementText.TryParseAmount(text, separator, out var amount).ShouldBeTrue();
        amount.ShouldBe(Dec(expected));
    }

    [Theory]
    [InlineData("1,234")] // trois décimales
    [InlineData("abc")]
    [InlineData("")]
    public void Unreadable_amounts_are_refused(string text) =>
        StatementText.TryParseAmount(text, ',', out _).ShouldBeFalse();

    [Fact]
    public void Utf8_and_bom_files_are_decoded_as_utf8()
    {
        StatementText.Decode(Encoding.UTF8.GetBytes("Libellé")).ShouldBe("Libellé");
        StatementText.Decode([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("é")]).ShouldBe("é");
        // 0x80 est le signe euro en Windows-1252.
        StatementText.Decode([0x80]).ShouldBe("€");
    }

    [Fact]
    public void Fingerprints_use_fitid_or_keep_identical_purchases_apart_and_stable()
    {
        StatementLine[] lines =
        [
            new(1, D(2026, 11, 6), -6.50m, "ACHAT CB  Burger", null),
            new(2, D(2026, 11, 6), -6.50m, "achat cb burger", null),
            new(3, D(2026, 11, 6), -6.50m, "Autre", "FIT-1"),
        ];

        var first = StatementReader.Fingerprint(lines);
        var again = StatementReader.Fingerprint(lines);

        first.Select(f => f.Hash).Distinct().Count().ShouldBe(3);
        first[2].Hash.ShouldBe("fitid:FIT-1");
        again.Select(f => f.Hash).ShouldBe(first.Select(f => f.Hash));
        StatementReader.NormalizeKey("Prélèvement  d'Orange-SA").ShouldBe("PRELEVEMENT D ORANGE SA");
    }

    [Theory]
    [InlineData("0", ToleranceUnit.Amount, "-30", "-30", true)]
    [InlineData("0", ToleranceUnit.Amount, "-30", "-30.01", false)]
    [InlineData("2", ToleranceUnit.Amount, "-30", "-31.99", true)]
    [InlineData("10", ToleranceUnit.Percent, "-100", "-110", true)]
    [InlineData("10", ToleranceUnit.Percent, "-100", "-110.01", false)]
    [InlineData("50", ToleranceUnit.Percent, "-10", "10", false)] // sens opposé
    public void Tolerance_accepts_amounts_close_enough(string value, ToleranceUnit unit, string expected, string actual, bool accepted) =>
        new AmountTolerance(Dec(value), unit).Accepts(Dec(expected), Dec(actual)).ShouldBe(accepted);

    [Fact]
    public void Tolerance_validation_bounds_percent_and_cents()
    {
        new AmountTolerance(100, ToleranceUnit.Percent).IsValid.ShouldBeTrue();
        new AmountTolerance(101, ToleranceUnit.Percent).IsValid.ShouldBeFalse();
        new AmountTolerance(-1, ToleranceUnit.Amount).IsValid.ShouldBeFalse();
        new AmountTolerance(1.234m, ToleranceUnit.Amount).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Csv_options_validation_requires_amount_or_debit_and_credit()
    {
        BanquePostale.Validate().ShouldBeEmpty();
        (BanquePostale with { AmountColumn = null }).Validate().ShouldContainKey("amountColumn");
        (BanquePostale with { DebitColumn = 4 }).Validate().ShouldContainKey("amountColumn");
        (BanquePostale with { LabelColumn = 1 }).Validate().ShouldContainKey("columns");
        (BanquePostale with { Delimiter = ',', DecimalSeparator = ',' }).Validate().ShouldContainKey("decimalSeparator");
        (BanquePostale with { DateFormat = "yyyy" }).Validate().ShouldContainKey("dateFormat");
    }
}
