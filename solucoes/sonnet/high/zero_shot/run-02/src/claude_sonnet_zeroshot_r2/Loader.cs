using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Etl;

public readonly record struct LoadStats(long Read, long Rejected, long Stations, long Emissions, long Duplicates);

/// <summary>Lê os CSVs, aplica as regras e grava estacao/emissao.</summary>
public sealed class Loader : IDisposable
{
    private readonly SqliteCommand _insEstacao;
    private readonly SqliteCommand _insEmissao;
    private readonly SqliteParameter[] _pe;
    private readonly SqliteParameter[] _pm;

    private readonly HashSet<StationKey> _stations = new();
    private readonly HashSet<EmissionKey> _emissions = new();

    private long _read, _rejected, _dupes;

    public LoadStats Stats => new(_read, _rejected, _stations.Count, _emissions.Count, _dupes);

    private static readonly string[] EstacaoCols =
        ["fistel", "num_estacao", "uf", "cod_municipio", "municipio", "endereco", "latitude", "longitude", "coordenada_valida", "entidade", "operadora"];

    private static readonly string[] EmissaoCols =
    [
        "uf", "fistel", "num_estacao", "num_ato", "tecnologia", "geracao", "designacao_emissao", "largura_banda_khz",
        "freq_tx_mhz", "freq_rx_mhz", "duplex_mhz", "azimute", "potencia_w", "ganho_antena", "altura_antena_m",
        "data_licenciamento", "data_primeiro_licenciamento", "data_validade", "vencida", "anos_licenciada",
        "status", "classe_estacao", "polarizacao", "angulo_elevacao_graus", "angulo_meia_potencia_graus", "frente_costa_db",
        "cod_tipo_antena", "cod_equipamento_antena", "cod_equipamento_transmissor", "potencia_dbm", "azimute_valido", "elevacao_valida",
    ];

    public Loader(SqliteConnection conn, SqliteTransaction tx)
    {
        (_insEstacao, _pe) = Prepare(conn, tx, "estacao", EstacaoCols);
        (_insEmissao, _pm) = Prepare(conn, tx, "emissao", EmissaoCols);
    }

    private static (SqliteCommand, SqliteParameter[]) Prepare(SqliteConnection conn, SqliteTransaction tx, string table, string[] cols)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"INSERT INTO {table} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", cols.Select((_, i) => "$p" + i))})";
        var ps = new SqliteParameter[cols.Length];
        for (var i = 0; i < cols.Length; i++)
        {
            ps[i] = cmd.CreateParameter();
            ps[i].ParameterName = "$p" + i;
            cmd.Parameters.Add(ps[i]);
        }
        cmd.Prepare();
        return (cmd, ps);
    }

    private readonly record struct StationKey(string Uf, string Fistel, string NumEstacao);

    private readonly record struct EmissionKey(
        string Uf, string Fistel, string NumEstacao, string? NumAto, double? Azimute,
        double? FreqTx, double? FreqRx, string? Designacao, string? Tecnologia);

    private static object Db(object? v) => v ?? DBNull.Value;
    private static object DbBool(bool? v) => v is null ? DBNull.Value : (v.Value ? 1L : 0L);
    private static object DbDate(DateOnly? d) => d is null ? DBNull.Value : d.Value.ToString("yyyy-MM-dd");

    public void LoadZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.Name) && e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    ?? zip.Entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.Name))
                    ?? throw new InvalidDataException($"ZIP sem arquivo: {zipPath}");
        using var stream = entry.Open();
        using var text = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16);
        var csv = new CsvReader(text);

        var fields = new List<string>(48);
        if (!csv.ReadRecord(fields)) return;
        var header = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < fields.Count; i++)
            header.TryAdd(fields[i].Trim().TrimStart('\uFEFF'), i);

        int Col(string name) => header.TryGetValue(name, out var i) ? i : -1;
        int cFistel = Col("NumFistel"), cEstacao = Col("NumEstacao"), cUf = Col("SiglaUf"), cAto = Col("NumAto"),
            cCodMun = Col("CodMunicipio"), cMun = Col("Municipio.NomeMunicipio"), cEnt = Col("NomeEntidade"),
            cEnd = Col("EnderecoEstacao"), cLat = Col("Latitude"), cLon = Col("Longitude"), cTec = Col("Tecnologia"),
            cDes = Col("DesignacaoEmissao"), cTx = Col("FreqTxMHz"), cRx = Col("FreqRxMHz"), cAz = Col("Azimute"),
            cPot = Col("PotenciaTransmissorWatts"), cGanho = Col("GanhoAntena"), cAlt = Col("AlturaAntena"),
            cEle = Col("AnguloElevacao"), cMeia = Col("AnguloMeiaPotenciaAntena"), cFc = Col("FrenteCostaAntena"),
            cPol = Col("Polarizacao"), cClasse = Col("CodTipoClasseEstacao"), cTipoAnt = Col("CodTipoAntena"),
            cEqAnt = Col("CodEquipamentoAntena"), cEqTx = Col("CodEquipamentoTransmissor"), cStatus = Col("Status.state"),
            cDtLic = Col("DataLicenciamento"), cDtPrim = Col("DataPrimeiroLicenciamento"), cDtVal = Col("DataValidade");

        if (cFistel < 0 || cEstacao < 0 || cUf < 0)
            throw new InvalidDataException($"Cabeçalho sem NumFistel/NumEstacao/SiglaUf em {zipPath}");

        string? Get(int col) => col >= 0 && col < fields.Count ? Cleaning.Clean(fields[col]) : null;
        double? Num(int col) => Cleaning.ParseNumber(Get(col));
        DateOnly? Date(int col) => Cleaning.ParseDate(Get(col));

        while (csv.ReadRecord(fields))
        {
            _read++;
            var fistel = Get(cFistel);
            var numEstacao = Get(cEstacao);
            var uf = Cleaning.Upper(Get(cUf));
            if (fistel is null || numEstacao is null || uf is null)
            {
                _rejected++;
                continue;
            }

            var numAto = Get(cAto);
            var tecnologia = Cleaning.Upper(Get(cTec));
            var designacao = Cleaning.Upper(Get(cDes));
            var freqTx = Num(cTx);
            var freqRx = Num(cRx);
            var azimute = Num(cAz);

            // Estação: atributos da primeira linha em que aparece.
            if (_stations.Add(new StationKey(uf, fistel, numEstacao)))
            {
                var entidade = Cleaning.NormalizeText(Get(cEnt));
                var lat = Num(cLat);
                var lon = Num(cLon);
                _pe[0].Value = fistel;
                _pe[1].Value = numEstacao;
                _pe[2].Value = uf;
                _pe[3].Value = Db(Get(cCodMun));
                _pe[4].Value = Db(Get(cMun));
                _pe[5].Value = Db(Cleaning.NormalizeText(Get(cEnd)));
                _pe[6].Value = Db(lat);
                _pe[7].Value = Db(lon);
                _pe[8].Value = Derivations.CoordenadaValida(lat, lon) ? 1L : 0L;
                _pe[9].Value = Db(entidade);
                _pe[10].Value = Derivations.Operadora(entidade);
                _insEstacao.ExecuteNonQuery();
            }

            // Emissão: descarta duplicadas (mantém a primeira).
            if (!_emissions.Add(new EmissionKey(uf, fistel, numEstacao, numAto, azimute, freqTx, freqRx, designacao, tecnologia)))
            {
                _dupes++;
                continue;
            }

            var potencia = Num(cPot);
            var elevacao = Num(cEle);
            var dtLic = Date(cDtLic);
            var dtPrim = Date(cDtPrim);
            var dtVal = Date(cDtVal);

            _pm[0].Value = uf;
            _pm[1].Value = fistel;
            _pm[2].Value = numEstacao;
            _pm[3].Value = Db(numAto);
            _pm[4].Value = Db(tecnologia);
            _pm[5].Value = Derivations.Geracao(tecnologia);
            _pm[6].Value = Db(designacao);
            _pm[7].Value = Db(Derivations.LarguraBandaKhz(designacao));
            _pm[8].Value = Db(freqTx);
            _pm[9].Value = Db(freqRx);
            _pm[10].Value = Db(Derivations.DuplexMhz(freqTx, freqRx));
            _pm[11].Value = Db(azimute);
            _pm[12].Value = Db(potencia);
            _pm[13].Value = Db(Num(cGanho));
            _pm[14].Value = Db(Num(cAlt));
            _pm[15].Value = DbDate(dtLic);
            _pm[16].Value = DbDate(dtPrim);
            _pm[17].Value = DbDate(dtVal);
            _pm[18].Value = Derivations.Vencida(dtVal) ? 1L : 0L;
            _pm[19].Value = Db(Derivations.AnosLicenciada(dtPrim));
            _pm[20].Value = Db(Get(cStatus));
            _pm[21].Value = Db(Get(cClasse));
            _pm[22].Value = Db(Cleaning.Upper(Get(cPol)));
            _pm[23].Value = Db(elevacao);
            _pm[24].Value = Db(Num(cMeia));
            _pm[25].Value = Db(Num(cFc));
            _pm[26].Value = Db(Get(cTipoAnt));
            _pm[27].Value = Db(Get(cEqAnt));
            _pm[28].Value = Db(Get(cEqTx));
            _pm[29].Value = Db(Derivations.PotenciaDbm(potencia));
            _pm[30].Value = DbBool(Derivations.AzimuteValido(azimute));
            _pm[31].Value = DbBool(Derivations.ElevacaoValida(elevacao));
            _insEmissao.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        _insEstacao.Dispose();
        _insEmissao.Dispose();
    }
}
