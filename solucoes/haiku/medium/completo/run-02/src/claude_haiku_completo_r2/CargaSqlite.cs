using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace ClaudeHaikuCompletoR2;

/// <summary>
/// Gravação no SQLite: cria o banco a partir do DDL, insere estações (uma por chave) e emissões
/// (sem duplicatas). Cada arquivo de UF é gravado em uma transação.
/// </summary>
internal sealed class CargaSqlite : IDisposable
{
    private static readonly string[] ParametrosEstacao =
    [
        "$fistel", "$num_estacao", "$uf", "$cod_municipio", "$municipio", "$endereco", "$latitude",
        "$longitude", "$coordenada_valida", "$entidade", "$operadora",
    ];

    private static readonly string[] ParametrosEmissao =
    [
        "$uf", "$fistel", "$num_estacao", "$num_ato", "$tecnologia", "$geracao", "$designacao_emissao",
        "$largura_banda_khz", "$freq_tx_mhz", "$freq_rx_mhz", "$duplex_mhz", "$azimute", "$potencia_w",
        "$ganho_antena", "$altura_antena_m", "$data_licenciamento", "$data_primeiro_licenciamento",
        "$data_validade", "$vencida", "$anos_licenciada", "$status", "$classe_estacao", "$polarizacao",
        "$angulo_elevacao_graus", "$angulo_meia_potencia_graus", "$frente_costa_db", "$cod_tipo_antena",
        "$cod_equipamento_antena", "$cod_equipamento_transmissor", "$potencia_dbm", "$azimute_valido",
        "$elevacao_valida",
    ];

    private readonly SqliteConnection _conexao;
    private readonly HashSet<(ulong, ulong)> _chavesEmissao = new();

    private SqliteTransaction? _transacao;
    private SqliteCommand? _insEstacao;
    private SqliteCommand? _insEmissao;

    public CargaSqlite(string caminhoBanco)
    {
        var pasta = Path.GetDirectoryName(caminhoBanco);
        if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);

        _conexao = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = caminhoBanco,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        _conexao.Open();
    }

    public long EstacoesInseridas { get; private set; }
    public long EmissoesInseridas { get; private set; }
    public long EmissoesDuplicadas { get; private set; }

    /// <summary>Executa o DDL exatamente como ele está no arquivo.</summary>
    public void CriarEsquema(string ddl)
    {
        using var cmd = _conexao.CreateCommand();
        cmd.CommandText = ddl;
        cmd.ExecuteNonQuery();
    }

    public void IniciarLote()
    {
        _transacao = _conexao.BeginTransaction();

        _insEstacao = _conexao.CreateCommand();
        _insEstacao.Transaction = _transacao;
        _insEstacao.CommandText = """
            INSERT OR IGNORE INTO estacao
                (fistel, num_estacao, uf, cod_municipio, municipio, endereco, latitude, longitude,
                 coordenada_valida, entidade, operadora)
            VALUES
                ($fistel, $num_estacao, $uf, $cod_municipio, $municipio, $endereco, $latitude, $longitude,
                 $coordenada_valida, $entidade, $operadora)
            """;
        AdicionarParametros(_insEstacao, ParametrosEstacao);

        _insEmissao = _conexao.CreateCommand();
        _insEmissao.Transaction = _transacao;
        _insEmissao.CommandText = """
            INSERT INTO emissao
                (uf, fistel, num_estacao, num_ato, tecnologia, geracao, designacao_emissao,
                 largura_banda_khz, freq_tx_mhz, freq_rx_mhz, duplex_mhz, azimute, potencia_w,
                 ganho_antena, altura_antena_m, data_licenciamento, data_primeiro_licenciamento,
                 data_validade, vencida, anos_licenciada, status, classe_estacao, polarizacao,
                 angulo_elevacao_graus, angulo_meia_potencia_graus, frente_costa_db, cod_tipo_antena,
                 cod_equipamento_antena, cod_equipamento_transmissor, potencia_dbm, azimute_valido,
                 elevacao_valida)
            VALUES
                ($uf, $fistel, $num_estacao, $num_ato, $tecnologia, $geracao, $designacao_emissao,
                 $largura_banda_khz, $freq_tx_mhz, $freq_rx_mhz, $duplex_mhz, $azimute, $potencia_w,
                 $ganho_antena, $altura_antena_m, $data_licenciamento, $data_primeiro_licenciamento,
                 $data_validade, $vencida, $anos_licenciada, $status, $classe_estacao, $polarizacao,
                 $angulo_elevacao_graus, $angulo_meia_potencia_graus, $frente_costa_db, $cod_tipo_antena,
                 $cod_equipamento_antena, $cod_equipamento_transmissor, $potencia_dbm, $azimute_valido,
                 $elevacao_valida)
            """;
        AdicionarParametros(_insEmissao, ParametrosEmissao);
    }

    /// <summary>
    /// Grava a linha: insere a estação se ela ainda não existe (vale o primeiro registro) e a emissão
    /// se não for duplicata.
    /// </summary>
    public void Gravar(LinhaTratada linha)
    {
        var e = linha.Estacao;
        var p = _insEstacao!.Parameters;
        p["$fistel"].Value = e.Fistel;
        p["$num_estacao"].Value = e.NumEstacao;
        p["$uf"].Value = e.Uf;
        p["$cod_municipio"].Value = Valor(e.CodMunicipio);
        p["$municipio"].Value = Valor(e.Municipio);
        p["$endereco"].Value = Valor(e.Endereco);
        p["$latitude"].Value = Valor(e.Latitude);
        p["$longitude"].Value = Valor(e.Longitude);
        p["$coordenada_valida"].Value = e.CoordenadaValida ? 1 : 0;
        p["$entidade"].Value = Valor(e.Entidade);
        p["$operadora"].Value = e.Operadora;
        if (_insEstacao.ExecuteNonQuery() > 0) EstacoesInseridas++;

        var m = linha.Emissao;
        if (!_chavesEmissao.Add(ChaveDuplicidade(m)))
        {
            EmissoesDuplicadas++;
            return;
        }

        var q = _insEmissao!.Parameters;
        q["$uf"].Value = m.Uf;
        q["$fistel"].Value = m.Fistel;
        q["$num_estacao"].Value = m.NumEstacao;
        q["$num_ato"].Value = Valor(m.NumAto);
        q["$tecnologia"].Value = Valor(m.Tecnologia);
        q["$geracao"].Value = m.Geracao;
        q["$designacao_emissao"].Value = Valor(m.DesignacaoEmissao);
        q["$largura_banda_khz"].Value = Valor(m.LarguraBandaKhz);
        q["$freq_tx_mhz"].Value = Valor(m.FreqTxMhz);
        q["$freq_rx_mhz"].Value = Valor(m.FreqRxMhz);
        q["$duplex_mhz"].Value = Valor(m.DuplexMhz);
        q["$azimute"].Value = Valor(m.Azimute);
        q["$potencia_w"].Value = Valor(m.PotenciaW);
        q["$ganho_antena"].Value = Valor(m.GanhoAntena);
        q["$altura_antena_m"].Value = Valor(m.AlturaAntenaM);
        q["$data_licenciamento"].Value = Valor(Texto(m.DataLicenciamento));
        q["$data_primeiro_licenciamento"].Value = Valor(Texto(m.DataPrimeiroLicenciamento));
        q["$data_validade"].Value = Valor(Texto(m.DataValidade));
        q["$vencida"].Value = m.Vencida ? 1 : 0;
        q["$anos_licenciada"].Value = Valor(m.AnosLicenciada);
        q["$status"].Value = Valor(m.Status);
        q["$classe_estacao"].Value = Valor(m.ClasseEstacao);
        q["$polarizacao"].Value = Valor(m.Polarizacao);
        q["$angulo_elevacao_graus"].Value = Valor(m.AnguloElevacaoGraus);
        q["$angulo_meia_potencia_graus"].Value = Valor(m.AnguloMeiaPotenciaGraus);
        q["$frente_costa_db"].Value = Valor(m.FrenteCostaDb);
        q["$cod_tipo_antena"].Value = Valor(m.CodTipoAntena);
        q["$cod_equipamento_antena"].Value = Valor(m.CodEquipamentoAntena);
        q["$cod_equipamento_transmissor"].Value = Valor(m.CodEquipamentoTransmissor);
        q["$potencia_dbm"].Value = Valor(m.PotenciaDbm);
        q["$azimute_valido"].Value = Valor(m.AzimuteValido);
        q["$elevacao_valida"].Value = Valor(m.ElevacaoValida);
        _insEmissao.ExecuteNonQuery();
        EmissoesInseridas++;
    }

    public void ConfirmarLote()
    {
        _transacao!.Commit();
        LiberarLote();
    }

    public void Dispose()
    {
        LiberarLote();
        _conexao.Dispose();
    }

    private void LiberarLote()
    {
        _insEstacao?.Dispose();
        _insEmissao?.Dispose();
        _transacao?.Dispose();
        _insEstacao = null;
        _insEmissao = null;
        _transacao = null;
    }

    private static void AdicionarParametros(SqliteCommand cmd, string[] nomes)
    {
        foreach (var nome in nomes)
        {
            cmd.Parameters.AddWithValue(nome, DBNull.Value);
        }
    }

    private static object Valor(object? valor) => valor ?? DBNull.Value;

    private static string? Texto(DateOnly? data) =>
        data?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Chave de duplicidade da emissão (seção 6): uf, fistel, num_estacao, num_ato, azimute, freq_tx,
    /// freq_rx, designacao_emissao e tecnologia, com NULL igual a NULL. Guardada como hash de 128 bits
    /// para não manter as strings em memória (colisão na prática impossível para ~8 milhões de linhas).
    /// </summary>
    private static (ulong, ulong) ChaveDuplicidade(Emissao e)
    {
        var sb = new StringBuilder(128);
        sb.Append(e.Uf).Append('\u001F')
          .Append(e.Fistel).Append('\u001F')
          .Append(e.NumEstacao).Append('\u001F');
        AnexarTexto(sb, e.NumAto);
        AnexarNumero(sb, e.Azimute);
        AnexarNumero(sb, e.FreqTxMhz);
        AnexarNumero(sb, e.FreqRxMhz);
        AnexarTexto(sb, e.DesignacaoEmissao);
        AnexarTexto(sb, e.Tecnologia);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()), hash);
        return (BitConverter.ToUInt64(hash[..8]), BitConverter.ToUInt64(hash[8..16]));
    }

    private static void AnexarTexto(StringBuilder sb, string? valor)
    {
        if (valor is null) sb.Append('\u0000');
        else sb.Append('\u0001').Append(valor);
        sb.Append('\u001F');
    }

    private static void AnexarNumero(StringBuilder sb, double? valor)
    {
        if (valor is null) sb.Append('\u0000');
        else sb.Append('\u0001').Append((valor.Value + 0.0).ToString("R", CultureInfo.InvariantCulture));
        sb.Append('\u001F');
    }
}
