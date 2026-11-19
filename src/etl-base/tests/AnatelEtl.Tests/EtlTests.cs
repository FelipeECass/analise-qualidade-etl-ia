using System.IO.Compression;
using System.Text;
using AnatelEtl.Extract;
using AnatelEtl.Load;
using AnatelEtl.Models;
using AnatelEtl.Transform;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AnatelEtl.Tests;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public class AnatelZipReaderTests
{
    [Fact]
    public void SplitLine_QuotedFieldsWithDelimiterAndEscapedQuotes_AreParsedCorrectly()
    {
        var fields = AnatelZipReader.SplitLine("\"a|b\"|\"c\"\"d\"|\"\"|plain");

        Assert.Equal(["a|b", "c\"d", "", "plain"], fields);
    }

    [Fact]
    public void Read_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => AnatelZipReader.Read("nao-existe.zip"));
    }
}

public class EmissaoTransformerTests
{
    private static readonly EmissaoTransformer Transformer =
        new(new FixedTimeProvider(new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero)));

    private static RawEmissao ValidRaw() => new()
    {
        Fistel = "50409146366",
        NumEstacao = "64300",
        NumAto = "155832022",
        NomeEntidade = "Telefonica Brasil S.a.",
        SiglaUf = "ac",
        CodMunicipio = "1200401",
        Municipio = "Rio Branco",
        Endereco = "RUA FLORIANO PEIXOTO  358",
        DesignacaoEmissao = "5M00G9W",
        Tecnologia = "WCDMA",
        FreqTxMhz = "874.5",
        FreqRxMhz = "829.5",
        Azimute = "40",
        PotenciaTransmissorWatts = "40",
        GanhoAntena = "11.2",
        AlturaAntena = "35",
        Latitude = "-9.972025",
        Longitude = "-67.813556",
        DataLicenciamento = "2025-03-25",
        DataPrimeiroLicenciamento = "2000-11-27",
        DataValidade = "2028-11-29",
        Status = "LIC-LIC-01",
        ClasseEstacao = "FB",
        Polarizacao = "x",
        AnguloElevacao = "0",
        AnguloMeiaPotencia = "65",
        FrenteCosta = "24",
        CodTipoAntena = "760",
        CodEquipamentoAntena = "005200301293",
        CodEquipamentoTransmissor = "019980701882",
    };

    [Fact]
    public void Transform_ValidRow_ProducesEnrichedStationAndEmission()
    {
        var outcome = Transformer.Transform(ValidRaw());

        Assert.False(outcome.IsRejected);
        var estacao = outcome.Estacao!;
        var emissao = outcome.Emissao!;

        Assert.Equal("AC", estacao.Uf);
        Assert.Equal("VIVO", estacao.Operadora);
        Assert.Equal("RUA FLORIANO PEIXOTO 358", estacao.Endereco);
        Assert.True(estacao.CoordenadaValida);

        Assert.Equal("3G", emissao.Geracao);
        Assert.Equal(5000, emissao.LarguraBandaKhz);
        Assert.Equal(45, emissao.DuplexMhz);
        Assert.Equal("850/900 MHz", emissao.FaixaFrequencia);
        Assert.False(emissao.Vencida);
        Assert.Equal(25.9, emissao.AnosLicenciada);
    }

    [Fact]
    public void Transform_ValidRow_KeepsRadioFieldsAndComputesTechnicalChecks()
    {
        var emissao = Transformer.Transform(ValidRaw()).Emissao!;

        Assert.Equal("LIC-LIC-01", emissao.Status);
        Assert.Equal("FB", emissao.ClasseEstacao);
        Assert.Equal("X", emissao.Polarizacao);
        Assert.Equal(0, emissao.AnguloElevacaoGraus);
        Assert.Equal(65, emissao.AnguloMeiaPotenciaGraus);
        Assert.Equal(24, emissao.FrenteCostaDb);
        Assert.Equal("760", emissao.CodTipoAntena);
        Assert.Equal("005200301293", emissao.CodEquipamentoAntena);
        Assert.Equal("019980701882", emissao.CodEquipamentoTransmissor);
        Assert.Equal(46.0, emissao.PotenciaDbm);
        Assert.True(emissao.AzimuteValido);
        Assert.True(emissao.ElevacaoValida);
        Assert.True(emissao.DuplexCoerente);
    }

    [Fact]
    public void Transform_ImplausibleValues_AreFlaggedNotDropped()
    {
        var raw = ValidRaw() with { Azimute = "400", AnguloElevacao = "120", FreqRxMhz = "800" };

        var outcome = Transformer.Transform(raw);

        Assert.False(outcome.IsRejected);
        Assert.False(outcome.Emissao!.AzimuteValido);
        Assert.False(outcome.Emissao.ElevacaoValida);
        Assert.False(outcome.Emissao.DuplexCoerente);
    }

    [Fact]
    public void Transform_MissingAzimuth_LeavesValidationUndecided()
    {
        var outcome = Transformer.Transform(ValidRaw() with { Azimute = null });

        Assert.Null(outcome.Emissao!.AzimuteValido);
    }

    [Fact]
    public void Transform_ExpiredLicense_IsFlaggedAsVencida()
    {
        var raw = ValidRaw() with { DataValidade = "2020-01-01" };

        var outcome = Transformer.Transform(raw);

        Assert.True(outcome.Emissao!.Vencida);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("48.85", "2.35")]
    [InlineData(null, "-67.8")]
    [InlineData("abc", "-67.8")]
    public void Transform_InvalidCoordinates_AreKeptButFlagged(string? latitude, string? longitude)
    {
        var raw = ValidRaw() with { Latitude = latitude, Longitude = longitude };

        var outcome = Transformer.Transform(raw);

        Assert.False(outcome.IsRejected);
        Assert.False(outcome.Estacao!.CoordenadaValida);
    }

    [Fact]
    public void Transform_MissingFistel_IsRejected()
    {
        var outcome = Transformer.Transform(ValidRaw() with { Fistel = null });

        Assert.True(outcome.IsRejected);
        Assert.Equal(EmissaoTransformer.MissingFistel, outcome.RejectReason);
    }

    [Fact]
    public void Transform_MissingStation_IsRejected()
    {
        var outcome = Transformer.Transform(ValidRaw() with { NumEstacao = null });

        Assert.Equal(EmissaoTransformer.MissingStation, outcome.RejectReason);
    }
}

public sealed class EtlPipelineTests : IDisposable
{
    private const string Header =
        "NumFistel|NumEstacao|NumAto|NomeEntidade|SiglaUf|CodMunicipio|Municipio.NomeMunicipio|EnderecoEstacao|" +
        "DesignacaoEmissao|Tecnologia|FreqTxMHz|FreqRxMHz|Azimute|PotenciaTransmissorWatts|GanhoAntena|" +
        "AlturaAntena|Latitude|Longitude|DataLicenciamento|DataPrimeiroLicenciamento|DataValidade";

    private readonly string tempDirectory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "anatel-etl-" + Guid.NewGuid().ToString("N"))).FullName;

    private readonly SqliteConnection connection;

    public EtlPipelineTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
    }

    public void Dispose()
    {
        connection.Dispose();
        Directory.Delete(tempDirectory, recursive: true);
    }

    [Fact]
    public void Run_LoadsDeduplicatedAndEnrichedRows()
    {
        var zip = CreateZip(
            Row("111", "1", "TELEFÔNICA BRASIL S.A.", "LTE", "2655", "2535", "-9.97", "-67.81", "2030-01-01"),
            Row("111", "1", "TELEFÔNICA BRASIL S.A.", "LTE", "2655", "2535", "-9.97", "-67.81", "2030-01-01"), // duplicata exata
            Row("111", "1", "TELEFÔNICA BRASIL S.A.", "GSM", "880", "835", "-9.97", "-67.81", "2020-01-01"),
            Row("222", "7", "CLARO S.A.", "NR", "3500", "3500", "0", "0", "2030-01-01"),
            Row("", "9", "ALGUÉM", "LTE", "2655", "2535", "-9.97", "-67.81", "2030-01-01")); // sem fistel

        var pipeline = new EtlPipeline(connection, new FixedTimeProvider(new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero)));

        var report = pipeline.Run(zip);

        Assert.Equal(5, report.Get(EtlReport.RowsRead));
        Assert.Equal(1, report.Get(EtlReport.RejectedPrefix + EmissaoTransformer.MissingFistel));
        Assert.Equal(1, report.Get(EtlReport.DuplicateEmissions));
        Assert.Equal(2, report.Get(EtlReport.StationsLoaded));
        Assert.Equal(3, report.Get(EtlReport.EmissionsLoaded));
        Assert.Equal(1, report.Get(EtlReport.InvalidCoordinates));
        Assert.Equal(1, report.Get(EtlReport.ExpiredLicenses));

        var byGeneration = new AnalyticsQueries(connection).EmissoesPorGeracao("AC");
        Assert.Contains(byGeneration, g => g is { Grupo: "4G", Total: 1 });
        Assert.Contains(byGeneration, g => g is { Grupo: "2G", Total: 1 });
    }

    [Fact]
    public void Run_CalledTwice_IsIdempotent()
    {
        var zip = CreateZip(
            Row("111", "1", "TIM S A", "LTE", "2655", "2535", "-9.97", "-67.81", "2030-01-01"));
        var pipeline = new EtlPipeline(connection, TimeProvider.System);

        pipeline.Run(zip);
        pipeline.Run(zip);

        var emissions = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM emissao;");
        var stations = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM estacao;");
        Assert.Equal(1, emissions);
        Assert.Equal(1, stations);
    }

    private static string Row(
        string fistel, string estacao, string entidade, string tecnologia,
        string tx, string rx, string lat, string lon, string validade) =>
        string.Join('|', new[]
        {
            fistel, estacao, "100", entidade, "AC", "1200401", "Rio Branco", "RUA X", "5M00G7W", tecnologia,
            tx, rx, "40", "40", "11.2", "35", lat, lon, "2025-03-25", "2000-11-27", validade,
        }.Select(v => v.Length == 0 ? "\"\"" : $"\"{v}\""));

    private string CreateZip(params string[] rows)
    {
        var zipPath = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("csv_licenciamento_teste.csv");
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.WriteLine(Header);
        foreach (var row in rows)
        {
            writer.WriteLine(row);
        }

        return zipPath;
    }
}
