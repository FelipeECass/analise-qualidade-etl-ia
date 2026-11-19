namespace Etl;

/// <summary>Converte uma linha bruta (campos) em Station + Emission, aplicando as seções 1 a 5.</summary>
public sealed class RowMapper
{
    private readonly Dictionary<string, int> _index;

    public RowMapper(IReadOnlyList<string> header)
    {
        _index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < header.Count; i++)
        {
            var name = header[i].Trim().TrimStart('﻿');
            _index.TryAdd(name, i);
        }
    }

    private string? Raw(List<string> f, string col)
        => _index.TryGetValue(col, out var i) && i < f.Count ? f[i] : null;

    private string? Text(List<string> f, string col) => Cleaner.Clean(Raw(f, col));
    private string? Upper(List<string> f, string col) => Cleaner.Upper(Raw(f, col));
    private double? Num(List<string> f, string col) => Cleaner.ParseNumber(Raw(f, col));
    private DateOnly? Date(List<string> f, string col) => Cleaner.ParseDate(Raw(f, col));

    /// <summary>Retorna false se a linha deve ser rejeitada (fistel, num_estacao ou uf nulos).</summary>
    public bool TryMap(List<string> f, out Station station, out Emission emission)
    {
        station = null!;
        emission = null!;

        var fistel = Text(f, "NumFistel");
        var numEstacao = Text(f, "NumEstacao");
        var uf = Upper(f, "SiglaUf");
        if (fistel == null || numEstacao == null || uf == null) return false;

        var entidade = Cleaner.NormalizeText(Raw(f, "NomeEntidade"));
        var lat = Num(f, "Latitude");
        var lon = Num(f, "Longitude");

        station = new Station
        {
            Fistel = fistel,
            NumEstacao = numEstacao,
            Uf = uf,
            CodMunicipio = Text(f, "CodMunicipio"),
            Municipio = Text(f, "Municipio.NomeMunicipio"),
            Endereco = Cleaner.NormalizeText(Raw(f, "EnderecoEstacao")),
            Latitude = lat,
            Longitude = lon,
            CoordenadaValida = Derivations.CoordenadaValida(lat, lon),
            Entidade = entidade,
            Operadora = Derivations.Operadora(entidade),
        };

        var tecnologia = Upper(f, "Tecnologia");
        var designacao = Upper(f, "DesignacaoEmissao");
        var tx = Num(f, "FreqTxMHz");
        var rx = Num(f, "FreqRxMHz");
        var azimute = Num(f, "Azimute");
        var potencia = Num(f, "PotenciaTransmissorWatts");
        var elevacao = Num(f, "AnguloElevacao");
        var dataValidade = Date(f, "DataValidade");
        var dataPrimeira = Date(f, "DataPrimeiroLicenciamento");

        emission = new Emission
        {
            Uf = uf,
            Fistel = fistel,
            NumEstacao = numEstacao,
            NumAto = Text(f, "NumAto"),
            Tecnologia = tecnologia,
            Geracao = Derivations.Geracao(tecnologia),
            DesignacaoEmissao = designacao,
            LarguraBandaKhz = Derivations.LarguraBandaKhz(designacao),
            FreqTxMhz = tx,
            FreqRxMhz = rx,
            DuplexMhz = Derivations.Duplex(tx, rx),
            Azimute = azimute,
            PotenciaW = potencia,
            GanhoAntena = Num(f, "GanhoAntena"),
            AlturaAntenaM = Num(f, "AlturaAntena"),
            DataLicenciamento = Date(f, "DataLicenciamento"),
            DataPrimeiroLicenciamento = dataPrimeira,
            DataValidade = dataValidade,
            Vencida = Derivations.Vencida(dataValidade),
            AnosLicenciada = Derivations.AnosLicenciada(dataPrimeira),
            Status = Text(f, "Status.state"),
            ClasseEstacao = Text(f, "CodTipoClasseEstacao"),
            Polarizacao = Upper(f, "Polarizacao"),
            AnguloElevacaoGraus = elevacao,
            AnguloMeiaPotenciaGraus = Num(f, "AnguloMeiaPotenciaAntena"),
            FrenteCostaDb = Num(f, "FrenteCostaAntena"),
            CodTipoAntena = Text(f, "CodTipoAntena"),
            CodEquipamentoAntena = Text(f, "CodEquipamentoAntena"),
            CodEquipamentoTransmissor = Text(f, "CodEquipamentoTransmissor"),
            PotenciaDbm = Derivations.PotenciaDbm(potencia),
            AzimuteValido = Derivations.AzimuteValido(azimute),
            ElevacaoValida = Derivations.ElevacaoValida(elevacao),
        };
        return true;
    }
}
