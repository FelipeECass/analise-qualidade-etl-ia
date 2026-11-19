using System.Data;
using AnatelEtl.Models;
using Dapper;

namespace AnatelEtl.Load;

/// <summary>Grava estações e emissões com Dapper, sempre com SQL parametrizado.</summary>
public sealed class EmissaoRepository(IDbConnection connection, IDbTransaction? transaction = null)
{
    private const string DateFormat = "yyyy-MM-dd";

    private const string DeleteEmissoesSql = "DELETE FROM emissao WHERE uf = @uf;";

    private const string DeleteEstacoesSql = "DELETE FROM estacao WHERE uf = @uf;";

    private const string InsertEstacaoSql = """
        INSERT INTO estacao
            (fistel, num_estacao, uf, cod_municipio, municipio, endereco,
             latitude, longitude, coordenada_valida, entidade, operadora)
        VALUES
            (@Fistel, @NumEstacao, @Uf, @CodMunicipio, @Municipio, @Endereco,
             @Latitude, @Longitude, @CoordenadaValida, @Entidade, @Operadora);
        """;

    private const string InsertEmissaoSql = """
        INSERT INTO emissao
            (uf, fistel, num_estacao, num_ato, tecnologia, geracao, designacao_emissao,
             largura_banda_khz, freq_tx_mhz, freq_rx_mhz, duplex_mhz, faixa_frequencia,
             azimute, potencia_w, ganho_antena, altura_antena_m,
             data_licenciamento, data_primeiro_licenciamento, data_validade, vencida, anos_licenciada,
             status, classe_estacao, polarizacao, angulo_elevacao_graus, angulo_meia_potencia_graus,
             frente_costa_db, cod_tipo_antena, cod_equipamento_antena, cod_equipamento_transmissor,
             potencia_dbm, azimute_valido, elevacao_valida, duplex_coerente)
        VALUES
            (@Uf, @Fistel, @NumEstacao, @NumAto, @Tecnologia, @Geracao, @DesignacaoEmissao,
             @LarguraBandaKhz, @FreqTxMhz, @FreqRxMhz, @DuplexMhz, @FaixaFrequencia,
             @Azimute, @PotenciaW, @GanhoAntena, @AlturaAntenaM,
             @DataLicenciamento, @DataPrimeiroLicenciamento, @DataValidade, @Vencida, @AnosLicenciada,
             @Status, @ClasseEstacao, @Polarizacao, @AnguloElevacaoGraus, @AnguloMeiaPotenciaGraus,
             @FrenteCostaDb, @CodTipoAntena, @CodEquipamentoAntena, @CodEquipamentoTransmissor,
             @PotenciaDbm, @AzimuteValido, @ElevacaoValida, @DuplexCoerente);
        """;

    /// <summary>Remove os dados da UF para que a carga possa ser repetida (idempotente).</summary>
    public void DeleteByUf(string uf)
    {
        connection.Execute(DeleteEmissoesSql, new { uf }, transaction);
        connection.Execute(DeleteEstacoesSql, new { uf }, transaction);
    }

    public int InsertEstacoes(IEnumerable<Estacao> estacoes) =>
        connection.Execute(InsertEstacaoSql, estacoes, transaction);

    public int InsertEmissoes(IEnumerable<Emissao> emissoes) =>
        connection.Execute(InsertEmissaoSql, emissoes.Select(ToParameters), transaction);

    // Datas viram texto ISO-8601 (formato nativo de data no SQLite).
    private static object ToParameters(Emissao e) => new
    {
        e.Uf,
        e.Fistel,
        e.NumEstacao,
        e.NumAto,
        e.Tecnologia,
        e.Geracao,
        e.DesignacaoEmissao,
        e.LarguraBandaKhz,
        e.FreqTxMhz,
        e.FreqRxMhz,
        e.DuplexMhz,
        e.FaixaFrequencia,
        e.Azimute,
        e.PotenciaW,
        e.GanhoAntena,
        e.AlturaAntenaM,
        DataLicenciamento = e.DataLicenciamento?.ToString(DateFormat),
        DataPrimeiroLicenciamento = e.DataPrimeiroLicenciamento?.ToString(DateFormat),
        DataValidade = e.DataValidade?.ToString(DateFormat),
        e.Vencida,
        e.AnosLicenciada,
        e.Status,
        e.ClasseEstacao,
        e.Polarizacao,
        e.AnguloElevacaoGraus,
        e.AnguloMeiaPotenciaGraus,
        e.FrenteCostaDb,
        e.CodTipoAntena,
        e.CodEquipamentoAntena,
        e.CodEquipamentoTransmissor,
        e.PotenciaDbm,
        e.AzimuteValido,
        e.ElevacaoValida,
        e.DuplexCoerente,
    };
}
