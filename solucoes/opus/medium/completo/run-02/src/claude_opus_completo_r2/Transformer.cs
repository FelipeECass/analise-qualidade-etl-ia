namespace AnatelEtl;

/// <summary>Converte uma linha bruta (campos já separados) nos registros de estação e emissão.</summary>
public sealed class Transformer
{
    private static readonly string[] Required =
    {
        "NumFistel", "NumEstacao", "NumAto", "SiglaUf", "CodMunicipio", "Municipio.NomeMunicipio", "NomeEntidade",
        "EnderecoEstacao", "Latitude", "Longitude", "Tecnologia", "DesignacaoEmissao", "FreqTxMHz", "FreqRxMHz",
        "Azimute", "PotenciaTransmissorWatts", "GanhoAntena", "AlturaAntena", "AnguloElevacao",
        "AnguloMeiaPotenciaAntena", "FrenteCostaAntena", "Polarizacao", "CodTipoClasseEstacao", "CodTipoAntena",
        "CodEquipamentoAntena", "CodEquipamentoTransmissor", "Status.state", "DataLicenciamento",
        "DataPrimeiroLicenciamento", "DataValidade",
    };

    private readonly int _fistel, _estacao, _ato, _uf, _codMun, _mun, _ent, _end, _lat, _lon, _tec, _des, _tx, _rx,
        _az, _pot, _ganho, _alt, _elev, _meia, _frente, _pol, _classe, _tipoAnt, _eqAnt, _eqTx, _status,
        _dLic, _dPrim, _dVal;

    public Transformer(string[] header)
    {
        var idx = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < header.Length; i++)
        {
            var name = header[i].Trim().TrimStart('﻿');
            idx.TryAdd(name, i);
        }
        var missing = Required.Where(r => !idx.ContainsKey(r)).ToList();
        if (missing.Count > 0) throw new InvalidDataException("Colunas ausentes no cabeçalho: " + string.Join(", ", missing));

        _fistel = idx["NumFistel"]; _estacao = idx["NumEstacao"]; _ato = idx["NumAto"]; _uf = idx["SiglaUf"];
        _codMun = idx["CodMunicipio"]; _mun = idx["Municipio.NomeMunicipio"]; _ent = idx["NomeEntidade"];
        _end = idx["EnderecoEstacao"]; _lat = idx["Latitude"]; _lon = idx["Longitude"]; _tec = idx["Tecnologia"];
        _des = idx["DesignacaoEmissao"]; _tx = idx["FreqTxMHz"]; _rx = idx["FreqRxMHz"]; _az = idx["Azimute"];
        _pot = idx["PotenciaTransmissorWatts"]; _ganho = idx["GanhoAntena"]; _alt = idx["AlturaAntena"];
        _elev = idx["AnguloElevacao"]; _meia = idx["AnguloMeiaPotenciaAntena"]; _frente = idx["FrenteCostaAntena"];
        _pol = idx["Polarizacao"]; _classe = idx["CodTipoClasseEstacao"]; _tipoAnt = idx["CodTipoAntena"];
        _eqAnt = idx["CodEquipamentoAntena"]; _eqTx = idx["CodEquipamentoTransmissor"]; _status = idx["Status.state"];
        _dLic = idx["DataLicenciamento"]; _dPrim = idx["DataPrimeiroLicenciamento"]; _dVal = idx["DataValidade"];
    }

    private static string? F(string[] row, int i) => i < row.Length ? row[i] : null;

    /// <summary>Retorna false se a linha for rejeitada (fistel, estação ou UF nulos).</summary>
    public bool TryGetKey(string[] row, out string uf, out string fistel, out string numEstacao)
    {
        var f = Cleaning.Text(F(row, _fistel));
        var e = Cleaning.Text(F(row, _estacao));
        var u = Cleaning.Upper(F(row, _uf));
        uf = u!; fistel = f!; numEstacao = e!;
        return f != null && e != null && u != null;
    }

    public Estacao BuildEstacao(string[] row, string uf, string fistel, string numEstacao)
    {
        var lat = Cleaning.Number(F(row, _lat));
        var lon = Cleaning.Number(F(row, _lon));
        var entidade = Cleaning.Normalize(F(row, _ent));
        return new Estacao(
            fistel, numEstacao, uf,
            Cleaning.Text(F(row, _codMun)),
            Cleaning.Text(F(row, _mun)),
            Cleaning.Normalize(F(row, _end)),
            lat, lon, Derivations.CoordenadaValida(lat, lon),
            entidade, Derivations.Operadora(entidade));
    }

    public Emissao BuildEmissao(string[] row, string uf, string fistel, string numEstacao)
    {
        var tecnologia = Cleaning.Upper(F(row, _tec));
        var designacao = Cleaning.Upper(F(row, _des));
        var tx = Cleaning.Number(F(row, _tx));
        var rx = Cleaning.Number(F(row, _rx));
        var az = Cleaning.Number(F(row, _az));
        var pot = Cleaning.Number(F(row, _pot));
        var elev = Cleaning.Number(F(row, _elev));
        var dPrim = Cleaning.Date(F(row, _dPrim));
        var dVal = Cleaning.Date(F(row, _dVal));
        return new Emissao(
            uf, fistel, numEstacao,
            Cleaning.Text(F(row, _ato)),
            tecnologia, Derivations.Geracao(tecnologia),
            designacao, Derivations.LarguraBandaKhz(designacao),
            tx, rx, Derivations.Duplex(tx, rx),
            az, pot,
            Cleaning.Number(F(row, _ganho)),
            Cleaning.Number(F(row, _alt)),
            Cleaning.Date(F(row, _dLic)), dPrim, dVal,
            Derivations.Vencida(dVal), Derivations.AnosLicenciada(dPrim),
            Cleaning.Text(F(row, _status)),
            Cleaning.Text(F(row, _classe)),
            Cleaning.Upper(F(row, _pol)),
            elev,
            Cleaning.Number(F(row, _meia)),
            Cleaning.Number(F(row, _frente)),
            Cleaning.Text(F(row, _tipoAnt)),
            Cleaning.Text(F(row, _eqAnt)),
            Cleaning.Text(F(row, _eqTx)),
            Derivations.PotenciaDbm(pot),
            Derivations.AzimuteValido(az),
            Derivations.ElevacaoValida(elev));
    }
}
