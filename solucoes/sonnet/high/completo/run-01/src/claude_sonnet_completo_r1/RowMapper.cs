namespace AnatelEtl;

/// <summary>Converte uma linha bruta (campos localizados pelo nome do cabeçalho) em Estacao + Emissao.</summary>
public sealed class RowMapper
{
    private static readonly string[] Required =
    {
        "Status.state","NomeEntidade","NumFistel","NumAto","NumEstacao","EnderecoEstacao","SiglaUf","CodMunicipio",
        "DesignacaoEmissao","Tecnologia","FreqTxMHz","FreqRxMHz","Azimute","CodTipoClasseEstacao","CodTipoAntena",
        "CodEquipamentoAntena","GanhoAntena","FrenteCostaAntena","AnguloMeiaPotenciaAntena","AnguloElevacao","Polarizacao",
        "AlturaAntena","CodEquipamentoTransmissor","PotenciaTransmissorWatts","Latitude","Longitude","DataLicenciamento",
        "DataPrimeiroLicenciamento","DataValidade","Municipio.NomeMunicipio",
    };

    private readonly Dictionary<string, int> _ix = new(StringComparer.Ordinal);
    private readonly int _columns;

    public RowMapper(IReadOnlyList<string> header)
    {
        _columns = header.Count;
        for (int i = 0; i < header.Count; i++)
        {
            // BOM residual ou espaços no cabeçalho não devem atrapalhar a localização
            var name = header[i].Trim('﻿', ' ', '\t');
            _ix.TryAdd(name, i);
        }
        var missing = Required.Where(r => !_ix.ContainsKey(r)).ToList();
        if (missing.Count > 0)
            throw new InvalidDataException("Colunas ausentes no cabeçalho: " + string.Join(", ", missing));
    }

    private string? Raw(List<string> f, string name)
    {
        int i = _ix[name];
        return i < f.Count ? f[i] : null;
    }

    /// <summary>Retorna false se a linha é rejeitada (NumFistel, NumEstacao ou SiglaUf nulos).</summary>
    public bool TryMap(List<string> f, out Estacao? estacao, out Emissao? emissao)
    {
        estacao = null; emissao = null;

        var fistel = Cleaner.Text(Raw(f, "NumFistel"));
        var numEstacao = Cleaner.Text(Raw(f, "NumEstacao"));
        var uf = Cleaner.Upper(Raw(f, "SiglaUf"));
        if (fistel is null || numEstacao is null || uf is null) return false;

        var entidade = Cleaner.Normalize(Raw(f, "NomeEntidade"));
        var lat = Cleaner.Number(Raw(f, "Latitude"));
        var lon = Cleaner.Number(Raw(f, "Longitude"));

        estacao = new Estacao
        {
            Fistel = fistel,
            NumEstacao = numEstacao,
            Uf = uf,
            CodMunicipio = Cleaner.Text(Raw(f, "CodMunicipio")),
            Municipio = Cleaner.Text(Raw(f, "Municipio.NomeMunicipio")),
            Endereco = Cleaner.Normalize(Raw(f, "EnderecoEstacao")),
            Latitude = lat,
            Longitude = lon,
            CoordenadaValida = Derivations.CoordenadaValida(lat, lon),
            Entidade = entidade,
            Operadora = Derivations.Operadora(entidade),
        };

        var tecnologia = Cleaner.Upper(Raw(f, "Tecnologia"));
        var designacao = Cleaner.Upper(Raw(f, "DesignacaoEmissao"));
        var tx = Cleaner.Number(Raw(f, "FreqTxMHz"));
        var rx = Cleaner.Number(Raw(f, "FreqRxMHz"));
        var azimute = Cleaner.Number(Raw(f, "Azimute"));
        var potencia = Cleaner.Number(Raw(f, "PotenciaTransmissorWatts"));
        var elevacao = Cleaner.Number(Raw(f, "AnguloElevacao"));
        var dtPrimeiro = Cleaner.Date(Raw(f, "DataPrimeiroLicenciamento"));
        var dtValidade = Cleaner.Date(Raw(f, "DataValidade"));

        emissao = new Emissao
        {
            Uf = uf,
            Fistel = fistel,
            NumEstacao = numEstacao,
            NumAto = Cleaner.Text(Raw(f, "NumAto")),
            Tecnologia = tecnologia,
            Geracao = Derivations.Geracao(tecnologia),
            DesignacaoEmissao = designacao,
            LarguraBandaKhz = Derivations.LarguraBandaKhz(designacao),
            FreqTxMhz = tx,
            FreqRxMhz = rx,
            DuplexMhz = Derivations.DuplexMhz(tx, rx),
            Azimute = azimute,
            PotenciaW = potencia,
            GanhoAntena = Cleaner.Number(Raw(f, "GanhoAntena")),
            AlturaAntenaM = Cleaner.Number(Raw(f, "AlturaAntena")),
            DataLicenciamento = Cleaner.Date(Raw(f, "DataLicenciamento")),
            DataPrimeiroLicenciamento = dtPrimeiro,
            DataValidade = dtValidade,
            Vencida = Derivations.Vencida(dtValidade),
            AnosLicenciada = Derivations.AnosLicenciada(dtPrimeiro),
            Status = Cleaner.Text(Raw(f, "Status.state")),
            ClasseEstacao = Cleaner.Text(Raw(f, "CodTipoClasseEstacao")),
            Polarizacao = Cleaner.Upper(Raw(f, "Polarizacao")),
            AnguloElevacaoGraus = elevacao,
            AnguloMeiaPotenciaGraus = Cleaner.Number(Raw(f, "AnguloMeiaPotenciaAntena")),
            FrenteCostaDb = Cleaner.Number(Raw(f, "FrenteCostaAntena")),
            CodTipoAntena = Cleaner.Text(Raw(f, "CodTipoAntena")),
            CodEquipamentoAntena = Cleaner.Text(Raw(f, "CodEquipamentoAntena")),
            CodEquipamentoTransmissor = Cleaner.Text(Raw(f, "CodEquipamentoTransmissor")),
            PotenciaDbm = Derivations.PotenciaDbm(potencia),
            AzimuteValido = Derivations.Azimute(azimute),
            ElevacaoValida = Derivations.Elevacao(elevacao),
        };
        return true;
    }
}
