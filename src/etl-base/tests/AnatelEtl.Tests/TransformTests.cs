using AnatelEtl.Transform;

namespace AnatelEtl.Tests;

public class EmissionDesignationTests
{
    [Theory]
    [InlineData("5M00G7W", 5000)]
    [InlineData("10M0G7W", 10000)]
    [InlineData("200KG7W", 200)]
    [InlineData("2M50F9W", 2500)]
    [InlineData("3M73F9W", 3730)]
    [InlineData("1M25F9W", 1250)]
    public void ParseBandwidthKhz_ValidDesignation_ReturnsKhz(string designation, double expected)
    {
        var result = EmissionDesignation.ParseBandwidthKhz(designation);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AB")]
    [InlineData("XXXXXXX")]
    [InlineData("M500G7W")]
    public void ParseBandwidthKhz_InvalidDesignation_ReturnsNull(string? designation)
    {
        Assert.Null(EmissionDesignation.ParseBandwidthKhz(designation));
    }
}

public class OperadoraClassifierTests
{
    [Theory]
    [InlineData("TELEFÔNICA BRASIL S.A.", "VIVO")]
    [InlineData("Telefonica Brasil S.a.", "VIVO")]
    [InlineData("TELEFONICA BRASIL S.A.", "VIVO")]
    [InlineData("CLARO S.A.", "CLARO")]
    [InlineData("EMBRATEL TVSAT", "CLARO")]
    [InlineData("TIM S A", "TIM")]
    [InlineData("OI MOVEL S.A.", "OI")]
    [InlineData("Empresa Qualquer Ltda", "OUTRAS")]
    public void Classify_NamesWithDifferentSpellings_ReturnsCanonicalOperator(string raw, string expected)
    {
        var normalized = TextNormalizer.Normalize(raw);

        Assert.Equal(expected, OperadoraClassifier.Classify(normalized));
    }

    [Fact]
    public void Classify_NullName_ReturnsOutras()
    {
        Assert.Equal(OperadoraClassifier.Outras, OperadoraClassifier.Classify(null));
    }
}

public class TextNormalizerTests
{
    [Theory]
    [InlineData("Telefônica  Brasil S.A.", "TELEFONICA BRASIL S A")]
    [InlineData("  Rua São Francisco, 123 ", "RUA SAO FRANCISCO 123")]
    [InlineData("R. Floriano Peixoto - 358", "R FLORIANO PEIXOTO 358")]
    public void Normalize_RemovesAccentsPunctuationAndExtraSpaces(string raw, string expected)
    {
        Assert.Equal(expected, TextNormalizer.Normalize(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("...")]
    public void Normalize_EmptyOrOnlyPunctuation_ReturnsNull(string? raw)
    {
        Assert.Null(TextNormalizer.Normalize(raw));
    }
}

public class SpectrumClassifierTests
{
    [Theory]
    [InlineData(763.0, "700 MHz")]
    [InlineData(874.5, "850/900 MHz")]
    [InlineData(1855.0, "1800 MHz")]
    [InlineData(2130.0, "2100 MHz")]
    [InlineData(2655.0, "2600 MHz")]
    [InlineData(3500.0, "3500 MHz")]
    [InlineData(150.0, "VHF/UHF (<700 MHz)")]
    [InlineData(4900.0, "3,8-6 GHz")]
    [InlineData(10000.0, "6 GHz+ (enlaces)")]
    [InlineData(1200.0, "OUTRA")]
    public void ClassifyBand_ReturnsExpectedBand(double mhz, string expected)
    {
        Assert.Equal(expected, SpectrumClassifier.ClassifyBand(mhz));
    }

    [Fact]
    public void ClassifyBand_NullFrequency_ReturnsNotAvailable()
    {
        Assert.Equal(SpectrumClassifier.NotAvailable, SpectrumClassifier.ClassifyBand(null));
    }

    [Theory]
    [InlineData("GSM", "2G")]
    [InlineData("wcdma", "3G")]
    [InlineData("WDCMA", "3G")]
    [InlineData("CDMA", "2G")]
    [InlineData("LTE", "4G")]
    [InlineData("NR", "5G")]
    [InlineData("TETRA", "OUTRA")]
    [InlineData(null, "N/D")]
    public void ClassifyGeneration_ReturnsExpectedGeneration(string? technology, string expected)
    {
        Assert.Equal(expected, SpectrumClassifier.ClassifyGeneration(technology));
    }
}

public class TechnicalValidatorTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(359.9, true)]
    [InlineData(360, true)]
    [InlineData(-1, false)]
    [InlineData(400, false)]
    public void IsAzimuthValid_ChecksZeroTo360(double degrees, bool expected)
    {
        Assert.Equal(expected, TechnicalValidator.IsAzimuthValid(degrees));
    }

    [Fact]
    public void IsAzimuthValid_Null_ReturnsNull()
    {
        Assert.Null(TechnicalValidator.IsAzimuthValid(null));
    }

    [Theory]
    [InlineData(-10, true)]
    [InlineData(0, true)]
    [InlineData(90, true)]
    [InlineData(120, false)]
    public void IsElevationValid_ChecksMinus90ToPlus90(double degrees, bool expected)
    {
        Assert.Equal(expected, TechnicalValidator.IsElevationValid(degrees));
    }

    [Theory]
    [InlineData("4G", "700 MHz", 55, true)]
    [InlineData("3G", "850/900 MHz", 45, true)]
    [InlineData("3G", "850/900 MHz", -45, true)]
    [InlineData("4G", "1800 MHz", 95, true)]
    [InlineData("3G", "2100 MHz", 190, true)]
    [InlineData("4G", "2600 MHz", 120, true)]
    [InlineData("5G", "3500 MHz", 0, true)]
    [InlineData("4G", "2600 MHz", 25, false)]
    [InlineData("3G", "850/900 MHz", 10, false)]
    public void IsDuplexConsistent_KnownBands_ComparesWithExpectedSpacing(
        string generation, string band, double duplex, bool expected)
    {
        Assert.Equal(expected, TechnicalValidator.IsDuplexConsistent(generation, band, duplex));
    }

    [Theory]
    [InlineData("N/D", "850/900 MHz", 10.0)]
    [InlineData("OUTRA", "700 MHz", 5.0)]
    [InlineData("4G", "VHF/UHF (<700 MHz)", 5.0)]
    [InlineData("4G", "700 MHz", null)]
    public void IsDuplexConsistent_NotApplicable_ReturnsNull(string generation, string band, double? duplex)
    {
        Assert.Null(TechnicalValidator.IsDuplexConsistent(generation, band, duplex));
    }

    [Theory]
    [InlineData(1, 30.0)]
    [InlineData(40, 46.0)]
    [InlineData(0.001, 0.0)]
    public void ToDbm_ConvertsWatts(double watts, double expected)
    {
        Assert.Equal(expected, TechnicalValidator.ToDbm(watts));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(null)]
    public void ToDbm_NonPositive_ReturnsNull(double? watts)
    {
        Assert.Null(TechnicalValidator.ToDbm(watts));
    }
}

public class ValueParserTests
{
    [Theory]
    [InlineData("55", 55)]
    [InlineData("55.0", 55)]
    [InlineData("18.000", 18)]
    [InlineData("-9.655306", -9.655306)]
    [InlineData("12,5", 12.5)]
    public void ParseDouble_ValidNumbers_ReturnsValue(string text, double expected)
    {
        Assert.Equal(expected, ValueParser.ParseDouble(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NA")]
    [InlineData("NaN")]
    public void ParseDouble_InvalidNumbers_ReturnsNull(string? text)
    {
        Assert.Null(ValueParser.ParseDouble(text));
    }

    [Fact]
    public void ParseDate_IsoDate_ReturnsDateOnly()
    {
        Assert.Equal(new DateOnly(2025, 7, 18), ValueParser.ParseDate("2025-07-18"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("18/07/2025")]
    [InlineData("2025-13-40")]
    public void ParseDate_InvalidDate_ReturnsNull(string? text)
    {
        Assert.Null(ValueParser.ParseDate(text));
    }
}
