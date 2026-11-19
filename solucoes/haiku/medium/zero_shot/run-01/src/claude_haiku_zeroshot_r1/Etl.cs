using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;

namespace ClaudeHaikuZeroshotR1;

/// <summary>Pipeline ETL: lê os ZIPs por UF, trata, deduplica e grava o SQLite.</summary>
internal static class Etl
{
    private const string Sep = "\u001F";
    private const string Nulo = "\u0000NULL";

    private static readonly string[] Colunas =
    {
        "NumFistel", "NumEstacao", "NumAto", "SiglaUf", "CodMunicipio", "Municipio.NomeMunicipio",
        "NomeEntidade", "EnderecoEstacao", "Latitude", "Longitude", "Tecnologia", "DesignacaoEmissao",
        "FreqTxMHz", "FreqRxMHz", "Azimute", "PotenciaTransmissorWatts", "GanhoAntena", "AlturaAntena",
        "AnguloElevacao", "AnguloMeiaPotenciaAntena", "FrenteCostaAntena", "Polarizacao",
        "CodTipoClasseEstacao", "CodTipoAntena", "CodEquipamentoAntena", "CodEquipamentoTransmissor",
        "Status.state", "DataLicenciamento", "DataPrimeiroLicenciamento", "DataValidade",
    };

    public static void Run(string filesDir, string ddlPath, string dbPath)
    {
        var ddl = File.ReadAllText(ddlPath, Encoding.UTF8);

        var csb = new SqliteConnectionStringBuilder { DataSource = dbPath };
        using var conn = new SqliteConnection(csb.ToString());
        conn.Open();

        using (var cmdDdl = conn.CreateCommand())
        {
            cmdDdl.CommandText = ddl;
            cmdDdl.ExecuteNonQuery();
        }

        using var tx = conn.BeginTransaction();
        using var insEstacao = conn.CreateCommand();
        insEstacao.Transaction = tx;
        insEstacao.CommandText = """
            INSERT INTO estacao (fistel, num_estacao, uf, cod_municipio, municipio, endereco, latitude,
                                 longitude, coordenada_valida, entidade, operadora)
            VALUES ($fistel, $num_estacao, $uf, $cod_municipio, $municipio, $endereco, $latitude,
                    $longitude, $coordenada_valida, $entidade, $operadora)
            """;
        var pEst = AddParams(insEstacao, "fistel", "num_estacao", "uf", "cod_municipio", "municipio", "endereco",
            "latitude", "longitude", "coordenada_valida", "entidade", "operadora");

        using var insEmissao = conn.CreateCommand();
        insEmissao.Transaction = tx;
        insEmissao.CommandText = """
            INSERT INTO emissao (uf, fistel, num_estacao, num_ato, tecnologia, geracao, designacao_emissao,
                                 largura_banda_khz, freq_tx_mhz, freq_rx_mhz, duplex_mhz, azimute, potencia_w,
                                 ganho_antena, altura_antena_m, data_licenciamento, data_primeiro_licenciamento,
                                 data_validade, vencida, anos_licenciada, status, classe_estacao, polarizacao,
                                 angulo_elevacao_graus, angulo_meia_potencia_graus, frente_costa_db,
                                 cod_tipo_antena, cod_equipamento_antena, cod_equipamento_transmissor,
                                 potencia_dbm, azimute_valido, elevacao_valida)
            VALUES ($uf, $fistel, $num_estacao, $num_ato, $tecnologia, $geracao, $designacao_emissao,
                    $largura_banda_khz, $freq_tx_mhz, $freq_rx_mhz, $duplex_mhz, $azimute, $potencia_w,
                    $ganho_antena, $altura_antena_m, $data_licenciamento, $data_primeiro_licenciamento,
                    $data_validade, $vencida, $anos_licenciada, $status, $classe_estacao, $polarizacao,
                    $angulo_elevacao_graus, $angulo_meia_potencia_graus, $frente_costa_db,
                    $cod_tipo_antena, $cod_equipamento_antena, $cod_equipamento_transmissor,
                    $potencia_dbm, $azimute_valido, $elevacao_valida)
            """;
        var pEmi = AddParams(insEmissao, "uf", "fistel", "num_estacao", "num_ato", "tecnologia", "geracao",
            "designacao_emissao", "largura_banda_khz", "freq_tx_mhz", "freq_rx_mhz", "duplex_mhz", "azimute",
            "potencia_w", "ganho_antena", "altura_antena_m", "data_licenciamento", "data_primeiro_licenciamento",
            "data_validade", "vencida", "anos_licenciada", "status", "classe_estacao", "polarizacao",
            "angulo_elevacao_graus", "angulo_meia_potencia_graus", "frente_costa_db", "cod_tipo_antena",
            "cod_equipamento_antena", "cod_equipamento_transmissor", "potencia_dbm", "azimute_valido",
            "elevacao_valida");

        var estacoesVistas = new HashSet<string>(StringComparer.Ordinal);
        var emissoesVistas = new HashSet<string>(StringComparer.Ordinal);

        var zips = Directory.GetFiles(filesDir, "*.zip")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
        if (zips.Count == 0)
            throw new FileNotFoundException($"Nenhum ZIP encontrado em '{filesDir}'.");

        long lidas = 0, rejeitadas = 0, emissoesDuplicadas = 0;

        foreach (var zip in zips)
        {
            using var archive = ZipFile.OpenRead(zip);
            var entrada = archive.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"'{Path.GetFileName(zip)}' não contém CSV.");

            using var reader = new StreamReader(entrada.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            using var linhas = CsvReader.Read(reader).GetEnumerator();
            if (!linhas.MoveNext())
                throw new InvalidDataException($"'{Path.GetFileName(zip)}' está vazio (sem cabeçalho).");
            var idx = MapearCabecalho(linhas.Current, zip);

            while (linhas.MoveNext())
            {
                var campos = linhas.Current;
                lidas++;
                string? Bruto(string col) => idx.TryGetValue(col, out var i) && i < campos.Length ? campos[i] : null;

                var fistel = Rules.Clean(Bruto("NumFistel"));
                var numEstacao = Rules.Clean(Bruto("NumEstacao"));
                var uf = Rules.Upper(Bruto("SiglaUf"));
                if (fistel is null || numEstacao is null || uf is null)
                {
                    rejeitadas++;
                    continue;
                }

                // ----- estação: uma linha por (uf, fistel, num_estacao); atributos da primeira ocorrência
                if (estacoesVistas.Add(string.Join(Sep, uf, fistel, numEstacao)))
                {
                    var entidade = Rules.NormalizeText(Bruto("NomeEntidade"));
                    var lat = Rules.ParseNumber(Bruto("Latitude"));
                    var lon = Rules.ParseNumber(Bruto("Longitude"));

                    pEst["fistel"].Value = fistel;
                    pEst["num_estacao"].Value = numEstacao;
                    pEst["uf"].Value = uf;
                    pEst["cod_municipio"].Value = Nz(Rules.Clean(Bruto("CodMunicipio")));
                    pEst["municipio"].Value = Nz(Rules.Clean(Bruto("Municipio.NomeMunicipio")));
                    pEst["endereco"].Value = Nz(Rules.NormalizeText(Bruto("EnderecoEstacao")));
                    pEst["latitude"].Value = Nz(Rules.ToDouble(lat));
                    pEst["longitude"].Value = Nz(Rules.ToDouble(lon));
                    pEst["coordenada_valida"].Value = Rules.CoordenadaValida(lat, lon) ? 1 : 0;
                    pEst["entidade"].Value = Nz(entidade);
                    pEst["operadora"].Value = Rules.Operadora(entidade);
                    insEstacao.ExecuteNonQuery();
                }

                // ----- emissão
                var numAto = Rules.Clean(Bruto("NumAto"));
                var azimute = Rules.ParseNumber(Bruto("Azimute"));
                var freqTx = Rules.ParseNumber(Bruto("FreqTxMHz"));
                var freqRx = Rules.ParseNumber(Bruto("FreqRxMHz"));
                var tecnologia = Rules.Upper(Bruto("Tecnologia"));
                var designacao = Rules.Upper(Bruto("DesignacaoEmissao"));

                var chaveEmissao = string.Join(Sep,
                    uf, fistel, numEstacao, numAto ?? Nulo, ChaveNum(azimute), ChaveNum(freqTx),
                    ChaveNum(freqRx), designacao ?? Nulo, tecnologia ?? Nulo);
                if (!emissoesVistas.Add(chaveEmissao))
                {
                    emissoesDuplicadas++;
                    continue;
                }

                var potenciaW = Rules.ParseNumber(Bruto("PotenciaTransmissorWatts"));
                var dataLic = Rules.ParseDate(Bruto("DataLicenciamento"));
                var dataPrim = Rules.ParseDate(Bruto("DataPrimeiroLicenciamento"));
                var dataVal = Rules.ParseDate(Bruto("DataValidade"));
                var elevacao = Rules.ParseNumber(Bruto("AnguloElevacao"));
                var potenciaDbm = Rules.PotenciaDbm(potenciaW);

                pEmi["uf"].Value = uf;
                pEmi["fistel"].Value = fistel;
                pEmi["num_estacao"].Value = numEstacao;
                pEmi["num_ato"].Value = Nz(numAto);
                pEmi["tecnologia"].Value = Nz(tecnologia);
                pEmi["geracao"].Value = Rules.Geracao(tecnologia);
                pEmi["designacao_emissao"].Value = Nz(designacao);
                pEmi["largura_banda_khz"].Value = Nz(Rules.LarguraBandaKhz(designacao));
                pEmi["freq_tx_mhz"].Value = Nz(Rules.ToDouble(freqTx));
                pEmi["freq_rx_mhz"].Value = Nz(Rules.ToDouble(freqRx));
                pEmi["duplex_mhz"].Value = Nz(Rules.ToDouble(Rules.Duplex(freqTx, freqRx)));
                pEmi["azimute"].Value = Nz(Rules.ToDouble(azimute));
                pEmi["potencia_w"].Value = Nz(Rules.ToDouble(potenciaW));
                pEmi["ganho_antena"].Value = Nz(Rules.ToDouble(Rules.ParseNumber(Bruto("GanhoAntena"))));
                pEmi["altura_antena_m"].Value = Nz(Rules.ToDouble(Rules.ParseNumber(Bruto("AlturaAntena"))));
                pEmi["data_licenciamento"].Value = Nz(Rules.DateText(dataLic));
                pEmi["data_primeiro_licenciamento"].Value = Nz(Rules.DateText(dataPrim));
                pEmi["data_validade"].Value = Nz(Rules.DateText(dataVal));
                pEmi["vencida"].Value = Rules.Vencida(dataVal) ? 1 : 0;
                pEmi["anos_licenciada"].Value = Nz(Rules.ToDouble(Rules.AnosLicenciada(dataPrim)));
                pEmi["status"].Value = Nz(Rules.Clean(Bruto("Status.state")));
                pEmi["classe_estacao"].Value = Nz(Rules.Clean(Bruto("CodTipoClasseEstacao")));
                pEmi["polarizacao"].Value = Nz(Rules.Upper(Bruto("Polarizacao")));
                pEmi["angulo_elevacao_graus"].Value = Nz(Rules.ToDouble(elevacao));
                pEmi["angulo_meia_potencia_graus"].Value = Nz(Rules.ToDouble(Rules.ParseNumber(Bruto("AnguloMeiaPotenciaAntena"))));
                pEmi["frente_costa_db"].Value = Nz(Rules.ToDouble(Rules.ParseNumber(Bruto("FrenteCostaAntena"))));
                pEmi["cod_tipo_antena"].Value = Nz(Rules.Clean(Bruto("CodTipoAntena")));
                pEmi["cod_equipamento_antena"].Value = Nz(Rules.Clean(Bruto("CodEquipamentoAntena")));
                pEmi["cod_equipamento_transmissor"].Value = Nz(Rules.Clean(Bruto("CodEquipamentoTransmissor")));
                pEmi["potencia_dbm"].Value = Nz(Rules.ToDouble(potenciaDbm));
                pEmi["azimute_valido"].Value = Nz(Rules.Entre(azimute, 0m, 360m));
                pEmi["elevacao_valida"].Value = Nz(Rules.Entre(elevacao, -90m, 90m));
                insEmissao.ExecuteNonQuery();
            }

            Console.WriteLine($"{Path.GetFileName(zip)}: processado.");
        }

        tx.Commit();

        Console.WriteLine($"Linhas lidas: {lidas}; rejeitadas (sem FISTEL/estação/UF): {rejeitadas}; " +
                          $"emissões duplicadas não carregadas: {emissoesDuplicadas}.");
    }

    private static Dictionary<string, int> MapearCabecalho(string[] cabecalho, string zip)
    {
        var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < cabecalho.Length; i++)
        {
            var nome = cabecalho[i].Trim();
            if (nome.Length > 0) idx.TryAdd(nome, i);
        }

        var faltando = Colunas.Where(c => !idx.ContainsKey(c)).ToList();
        if (faltando.Count > 0)
            throw new InvalidDataException(
                $"'{Path.GetFileName(zip)}': colunas ausentes no cabeçalho: {string.Join(", ", faltando)}.");
        return idx;
    }

    /// <summary>Chave canônica de número para deduplicação (40 e 40.0 são iguais; NULL = NULL).</summary>
    private static string ChaveNum(decimal? v) =>
        v is null ? Nulo : Rules.ToDouble(v.Value).ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private static object Nz(string? v) => (object?)v ?? DBNull.Value;

    private static object Nz(double? v) => v.HasValue ? v.Value : DBNull.Value;

    private static object Nz(int? v) => v.HasValue ? v.Value : DBNull.Value;

    private static Dictionary<string, SqliteParameter> AddParams(SqliteCommand cmd, params string[] nomes)
    {
        var dic = new Dictionary<string, SqliteParameter>(StringComparer.Ordinal);
        foreach (var n in nomes)
        {
            var p = new SqliteParameter { ParameterName = "$" + n };
            cmd.Parameters.Add(p);
            dic[n] = p;
        }
        return dic;
    }
}
