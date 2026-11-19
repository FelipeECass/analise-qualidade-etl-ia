using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace Etl;

/// <summary>Transforma registros brutos em linhas de estação/emissão (seções 1 a 5).</summary>
public sealed class Transformer
{
    private static readonly string[] Required =
    {
        "Status.state", "NomeEntidade", "NumFistel", "NumAto", "NumEstacao", "EnderecoEstacao", "SiglaUf",
        "CodMunicipio", "DesignacaoEmissao", "Tecnologia", "FreqTxMHz", "FreqRxMHz", "Azimute",
        "CodTipoClasseEstacao", "CodTipoAntena", "CodEquipamentoAntena", "GanhoAntena", "FrenteCostaAntena",
        "AnguloMeiaPotenciaAntena", "AnguloElevacao", "Polarizacao", "AlturaAntena", "CodEquipamentoTransmissor",
        "PotenciaTransmissorWatts", "Latitude", "Longitude", "DataLicenciamento", "DataPrimeiroLicenciamento",
        "DataValidade", "Municipio.NomeMunicipio",
    };

    private readonly int _status, _entidade, _fistel, _ato, _estacao, _endereco, _uf, _codMun, _desig, _tec,
        _tx, _rx, _az, _classe, _tipoAnt, _eqAnt, _ganho, _frente, _meia, _elev, _pol, _altura, _eqTx, _pot,
        _lat, _lon, _dLic, _dPri, _dVal, _mun;

    public Transformer(string[] header)
    {
        var idx = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < header.Length; i++)
        {
            var name = header[i].Trim().TrimStart('﻿');
            idx.TryAdd(name, i);
        }
        var missing = Required.Where(r => !idx.ContainsKey(r)).ToList();
        if (missing.Count > 0)
            throw new InvalidDataException("Colunas ausentes no cabeçalho: " + string.Join(", ", missing));

        _status = idx["Status.state"]; _entidade = idx["NomeEntidade"]; _fistel = idx["NumFistel"];
        _ato = idx["NumAto"]; _estacao = idx["NumEstacao"]; _endereco = idx["EnderecoEstacao"];
        _uf = idx["SiglaUf"]; _codMun = idx["CodMunicipio"]; _desig = idx["DesignacaoEmissao"];
        _tec = idx["Tecnologia"]; _tx = idx["FreqTxMHz"]; _rx = idx["FreqRxMHz"]; _az = idx["Azimute"];
        _classe = idx["CodTipoClasseEstacao"]; _tipoAnt = idx["CodTipoAntena"];
        _eqAnt = idx["CodEquipamentoAntena"]; _ganho = idx["GanhoAntena"]; _frente = idx["FrenteCostaAntena"];
        _meia = idx["AnguloMeiaPotenciaAntena"]; _elev = idx["AnguloElevacao"]; _pol = idx["Polarizacao"];
        _altura = idx["AlturaAntena"]; _eqTx = idx["CodEquipamentoTransmissor"];
        _pot = idx["PotenciaTransmissorWatts"]; _lat = idx["Latitude"]; _lon = idx["Longitude"];
        _dLic = idx["DataLicenciamento"]; _dPri = idx["DataPrimeiroLicenciamento"];
        _dVal = idx["DataValidade"]; _mun = idx["Municipio.NomeMunicipio"];
    }

    private static string? F(string[] row, int i) => i < row.Length ? row[i] : null;

    /// <summary>Chave da estação (uf, fistel, num_estacao); null se a linha deve ser rejeitada.</summary>
    public (string Uf, string Fistel, string NumEstacao)? StationKey(string[] row)
    {
        var fistel = Cleaning.Text(F(row, _fistel));
        var num = Cleaning.Text(F(row, _estacao));
        var uf = Cleaning.Upper(F(row, _uf));
        if (fistel == null || num == null || uf == null) return null;
        return (uf, fistel, num);
    }

    public EstacaoRow BuildEstacao(string[] row, (string Uf, string Fistel, string NumEstacao) key)
    {
        var lat = Cleaning.Number(F(row, _lat));
        var lon = Cleaning.Number(F(row, _lon));
        var entidade = Cleaning.Normalize(F(row, _entidade));
        return new EstacaoRow
        {
            Fistel = key.Fistel,
            NumEstacao = key.NumEstacao,
            Uf = key.Uf,
            CodMunicipio = Cleaning.Text(F(row, _codMun)),
            Municipio = Cleaning.Text(F(row, _mun)),
            Endereco = Cleaning.Normalize(F(row, _endereco)),
            Latitude = lat,
            Longitude = lon,
            CoordenadaValida = Derivations.CoordenadaValida(lat, lon),
            Entidade = entidade,
            Operadora = Derivations.Operadora(entidade),
        };
    }

    public EmissaoRow BuildEmissao(string[] row, (string Uf, string Fistel, string NumEstacao) key)
    {
        var tec = Cleaning.Upper(F(row, _tec));
        var desig = Cleaning.Upper(F(row, _desig));
        var tx = Cleaning.Number(F(row, _tx));
        var rx = Cleaning.Number(F(row, _rx));
        var az = Cleaning.Number(F(row, _az));
        var pot = Cleaning.Number(F(row, _pot));
        var elev = Cleaning.Number(F(row, _elev));
        var dPri = Cleaning.Date(F(row, _dPri));
        var dVal = Cleaning.Date(F(row, _dVal));
        return new EmissaoRow
        {
            Uf = key.Uf,
            Fistel = key.Fistel,
            NumEstacao = key.NumEstacao,
            NumAto = Cleaning.Text(F(row, _ato)),
            Tecnologia = tec,
            Geracao = Derivations.Geracao(tec),
            DesignacaoEmissao = desig,
            LarguraBandaKhz = Derivations.LarguraBandaKhz(desig),
            FreqTxMhz = tx,
            FreqRxMhz = rx,
            DuplexMhz = Derivations.DuplexMhz(tx, rx),
            Azimute = az,
            PotenciaW = pot,
            GanhoAntena = Cleaning.Number(F(row, _ganho)),
            AlturaAntenaM = Cleaning.Number(F(row, _altura)),
            DataLicenciamento = Cleaning.Date(F(row, _dLic)),
            DataPrimeiroLicenciamento = dPri,
            DataValidade = dVal,
            Vencida = Derivations.Vencida(dVal),
            AnosLicenciada = Derivations.AnosLicenciada(dPri),
            Status = Cleaning.Text(F(row, _status)),
            ClasseEstacao = Cleaning.Text(F(row, _classe)),
            Polarizacao = Cleaning.Upper(F(row, _pol)),
            AnguloElevacaoGraus = elev,
            AnguloMeiaPotenciaGraus = Cleaning.Number(F(row, _meia)),
            FrenteCostaDb = Cleaning.Number(F(row, _frente)),
            CodTipoAntena = Cleaning.Text(F(row, _tipoAnt)),
            CodEquipamentoAntena = Cleaning.Text(F(row, _eqAnt)),
            CodEquipamentoTransmissor = Cleaning.Text(F(row, _eqTx)),
            PotenciaDbm = Derivations.PotenciaDbm(pot),
            AzimuteValido = Derivations.AzimuteValido(az),
            ElevacaoValida = Derivations.ElevacaoValida(elev),
        };
    }

    /// <summary>
    /// Hash de 128 bits da chave de duplicidade de emissão (valores tratados; NULL == NULL;
    /// números comparados por valor).
    /// </summary>
    public static UInt128 EmissionKey(EmissaoRow e)
    {
        var sb = new StringBuilder(128);
        void S(string? s) { if (s == null) sb.Append('\u0002'); else sb.Append(s); sb.Append('\u0001'); }
        void N(double? d)
        {
            if (d is not double v) sb.Append('\u0002');
            else sb.Append((v == 0 ? 0.0 : v).ToString("R", CultureInfo.InvariantCulture));
            sb.Append('\u0001');
        }
        S(e.Uf); S(e.Fistel); S(e.NumEstacao); S(e.NumAto);
        N(e.Azimute); N(e.FreqTxMhz); N(e.FreqRxMhz);
        S(e.DesignacaoEmissao); S(e.Tecnologia);
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return XxHash128.HashToUInt128(bytes);
    }
}
