using AnatelEtl.Models;

namespace AnatelEtl.Transform;

public sealed record TransformOutcome(Estacao? Estacao, Emissao? Emissao, string? RejectReason)
{
    public bool IsRejected => RejectReason is not null;

    public static TransformOutcome Rejected(string reason) => new(null, null, reason);
}

/// <summary>
/// Converte uma linha crua em <see cref="Estacao"/> + <see cref="Emissao"/> tipadas e enriquecidas.
/// O relógio é injetado para que as colunas dependentes de "hoje" sejam testáveis.
/// </summary>
public sealed class EmissaoTransformer(TimeProvider clock)
{
    public const string MissingFistel = "sem_fistel";
    public const string MissingStation = "sem_num_estacao";
    public const string MissingUf = "sem_uf";

    private const double DaysPerYear = 365.25;
    private const double MinLatitude = -33.75;
    private const double MaxLatitude = 5.28;
    private const double MinLongitude = -73.99;
    private const double MaxLongitude = -28.83;

    public TransformOutcome Transform(RawEmissao raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        if (raw.Fistel is null)
        {
            return TransformOutcome.Rejected(MissingFistel);
        }

        if (raw.NumEstacao is null)
        {
            return TransformOutcome.Rejected(MissingStation);
        }

        if (raw.SiglaUf is null)
        {
            return TransformOutcome.Rejected(MissingUf);
        }

        return new TransformOutcome(BuildEstacao(raw), BuildEmissao(raw), null);
    }

    private static Estacao BuildEstacao(RawEmissao raw)
    {
        var latitude = ValueParser.ParseDouble(raw.Latitude);
        var longitude = ValueParser.ParseDouble(raw.Longitude);
        var entidade = TextNormalizer.Normalize(raw.NomeEntidade);

        return new Estacao(
            Fistel: raw.Fistel!,
            NumEstacao: raw.NumEstacao!,
            Uf: raw.SiglaUf!.ToUpperInvariant(),
            CodMunicipio: raw.CodMunicipio,
            Municipio: raw.Municipio,
            Endereco: TextNormalizer.Normalize(raw.Endereco),
            Latitude: latitude,
            Longitude: longitude,
            CoordenadaValida: IsValidBrazilianCoordinate(latitude, longitude),
            Entidade: entidade,
            Operadora: OperadoraClassifier.Classify(entidade));
    }

    private Emissao BuildEmissao(RawEmissao raw)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var freqTx = ValueParser.ParseDouble(raw.FreqTxMhz);
        var freqRx = ValueParser.ParseDouble(raw.FreqRxMhz);
        var validade = ValueParser.ParseDate(raw.DataValidade);
        var primeiroLicenciamento = ValueParser.ParseDate(raw.DataPrimeiroLicenciamento);
        var geracao = SpectrumClassifier.ClassifyGeneration(raw.Tecnologia);
        var faixa = SpectrumClassifier.ClassifyBand(freqTx);
        // Subtração em decimal exato e metade para longe do zero, como na especificação (correção de 14/11/2026).
        var duplex = freqTx is { } tx && freqRx is { } rx
            ? (double)Math.Round((decimal)tx - (decimal)rx, 3, MidpointRounding.AwayFromZero)
            : (double?)null;
        var azimute = ValueParser.ParseDouble(raw.Azimute);
        var elevacao = ValueParser.ParseDouble(raw.AnguloElevacao);
        var potencia = ValueParser.ParseDouble(raw.PotenciaTransmissorWatts);

        return new Emissao(
            Uf: raw.SiglaUf!.ToUpperInvariant(),
            Fistel: raw.Fistel!,
            NumEstacao: raw.NumEstacao!,
            NumAto: raw.NumAto,
            Tecnologia: raw.Tecnologia?.ToUpperInvariant(),
            Geracao: geracao,
            DesignacaoEmissao: raw.DesignacaoEmissao?.ToUpperInvariant(),
            LarguraBandaKhz: EmissionDesignation.ParseBandwidthKhz(raw.DesignacaoEmissao),
            FreqTxMhz: freqTx,
            FreqRxMhz: freqRx,
            DuplexMhz: duplex,
            FaixaFrequencia: faixa,
            Azimute: azimute,
            PotenciaW: potencia,
            GanhoAntena: ValueParser.ParseDouble(raw.GanhoAntena),
            AlturaAntenaM: ValueParser.ParseDouble(raw.AlturaAntena),
            DataLicenciamento: ValueParser.ParseDate(raw.DataLicenciamento),
            DataPrimeiroLicenciamento: primeiroLicenciamento,
            DataValidade: validade,
            Vencida: validade is { } expiry && expiry < today,
            AnosLicenciada: primeiroLicenciamento is { } first
                ? Math.Round((today.DayNumber - first.DayNumber) / DaysPerYear, 1)
                : null,
            Status: raw.Status,
            ClasseEstacao: raw.ClasseEstacao,
            Polarizacao: raw.Polarizacao?.ToUpperInvariant(),
            AnguloElevacaoGraus: elevacao,
            AnguloMeiaPotenciaGraus: ValueParser.ParseDouble(raw.AnguloMeiaPotencia),
            FrenteCostaDb: ValueParser.ParseDouble(raw.FrenteCosta),
            CodTipoAntena: raw.CodTipoAntena,
            CodEquipamentoAntena: raw.CodEquipamentoAntena,
            CodEquipamentoTransmissor: raw.CodEquipamentoTransmissor,
            PotenciaDbm: TechnicalValidator.ToDbm(potencia),
            AzimuteValido: TechnicalValidator.IsAzimuthValid(azimute),
            ElevacaoValida: TechnicalValidator.IsElevationValid(elevacao),
            DuplexCoerente: TechnicalValidator.IsDuplexConsistent(geracao, faixa, duplex));
    }

    public static bool IsValidBrazilianCoordinate(double? latitude, double? longitude)
    {
        if (latitude is not { } lat || longitude is not { } lon)
        {
            return false;
        }

        var isNullIsland = lat == 0 && lon == 0;
        return !isNullIsland
            && lat is >= MinLatitude and <= MaxLatitude
            && lon is >= MinLongitude and <= MaxLongitude;
    }
}
