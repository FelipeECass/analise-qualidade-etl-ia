namespace AnatelEtl;

/// <summary>Converte uma linha bruta (campos do CSV) em estação + emissão, conforme o cabeçalho.</summary>
public sealed class Transformer
{
    private readonly int _status, _entidade, _fistel, _numAto, _numEstacao, _endereco, _uf, _codMun,
        _designacao, _tecnologia, _freqTx, _freqRx, _azimute, _classe, _tipoAntena, _equipAntena,
        _ganho, _frenteCosta, _meiaPot, _elevacao, _polarizacao, _altura, _equipTx, _potencia,
        _lat, _lon, _dataLic, _dataPrim, _dataVal, _municipio;

    public Transformer(IReadOnlyList<string> header)
    {
        var idx = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < header.Count; i++)
        {
            var name = header[i].Trim().TrimStart('﻿');
            idx.TryAdd(name, i);
        }
        int Col(string name) => idx.TryGetValue(name, out var i)
            ? i : throw new InvalidDataException($"Coluna obrigatória ausente no cabeçalho: {name}");

        _status = Col("Status.state"); _entidade = Col("NomeEntidade"); _fistel = Col("NumFistel");
        _numAto = Col("NumAto"); _numEstacao = Col("NumEstacao"); _endereco = Col("EnderecoEstacao");
        _uf = Col("SiglaUf"); _codMun = Col("CodMunicipio"); _designacao = Col("DesignacaoEmissao");
        _tecnologia = Col("Tecnologia"); _freqTx = Col("FreqTxMHz"); _freqRx = Col("FreqRxMHz");
        _azimute = Col("Azimute"); _classe = Col("CodTipoClasseEstacao"); _tipoAntena = Col("CodTipoAntena");
        _equipAntena = Col("CodEquipamentoAntena"); _ganho = Col("GanhoAntena"); _frenteCosta = Col("FrenteCostaAntena");
        _meiaPot = Col("AnguloMeiaPotenciaAntena"); _elevacao = Col("AnguloElevacao"); _polarizacao = Col("Polarizacao");
        _altura = Col("AlturaAntena"); _equipTx = Col("CodEquipamentoTransmissor"); _potencia = Col("PotenciaTransmissorWatts");
        _lat = Col("Latitude"); _lon = Col("Longitude"); _dataLic = Col("DataLicenciamento");
        _dataPrim = Col("DataPrimeiroLicenciamento"); _dataVal = Col("DataValidade"); _municipio = Col("Municipio.NomeMunicipio");
    }

    /// <summary>Chave da estação (uf, fistel, num_estacao) ou null se a linha deve ser rejeitada.</summary>
    public (string Uf, string Fistel, string NumEstacao)? Key(IReadOnlyList<string> f)
    {
        var fistel = Cleaning.Text(Get(f, _fistel));
        var num = Cleaning.Text(Get(f, _numEstacao));
        var uf = Cleaning.Upper(Get(f, _uf));
        if (fistel is null || num is null || uf is null) return null;
        return (uf, fistel, num);
    }

    public Estacao BuildEstacao(IReadOnlyList<string> f, (string Uf, string Fistel, string NumEstacao) key)
    {
        var lat = Cleaning.Number(Get(f, _lat));
        var lon = Cleaning.Number(Get(f, _lon));
        var entidade = Cleaning.Normalize(Get(f, _entidade));
        return new Estacao(
            key.Fistel, key.NumEstacao, key.Uf,
            Cleaning.Text(Get(f, _codMun)),
            Cleaning.Text(Get(f, _municipio)),
            Cleaning.Normalize(Get(f, _endereco)),
            lat, lon, Derivations.CoordenadaValida(lat, lon),
            entidade, Derivations.Operadora(entidade));
    }

    public Emissao BuildEmissao(IReadOnlyList<string> f, (string Uf, string Fistel, string NumEstacao) key)
    {
        var tecnologia = Cleaning.Upper(Get(f, _tecnologia));
        var designacao = Cleaning.Upper(Get(f, _designacao));
        var tx = Cleaning.Number(Get(f, _freqTx));
        var rx = Cleaning.Number(Get(f, _freqRx));
        var az = Cleaning.Number(Get(f, _azimute));
        var pot = Cleaning.Number(Get(f, _potencia));
        var elev = Cleaning.Number(Get(f, _elevacao));
        var dLic = Cleaning.Date(Get(f, _dataLic));
        var dPrim = Cleaning.Date(Get(f, _dataPrim));
        var dVal = Cleaning.Date(Get(f, _dataVal));

        return new Emissao(
            key.Uf, key.Fistel, key.NumEstacao,
            Cleaning.Text(Get(f, _numAto)),
            tecnologia, Derivations.Geracao(tecnologia),
            designacao, Derivations.LarguraBandaKhz(designacao),
            tx, rx, Derivations.Duplex(tx, rx), az,
            pot, Cleaning.Number(Get(f, _ganho)), Cleaning.Number(Get(f, _altura)),
            dLic, dPrim, dVal,
            Derivations.Vencida(dVal), Derivations.AnosLicenciada(dPrim),
            Cleaning.Text(Get(f, _status)), Cleaning.Text(Get(f, _classe)), Cleaning.Upper(Get(f, _polarizacao)),
            elev, Cleaning.Number(Get(f, _meiaPot)), Cleaning.Number(Get(f, _frenteCosta)),
            Cleaning.Text(Get(f, _tipoAntena)), Cleaning.Text(Get(f, _equipAntena)), Cleaning.Text(Get(f, _equipTx)),
            Derivations.PotenciaDbm(pot), Derivations.AzimuteValido(az), Derivations.ElevacaoValida(elev));
    }

    private static string? Get(IReadOnlyList<string> f, int i) => i < f.Count ? f[i] : null;
}
