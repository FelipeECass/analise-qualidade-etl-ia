using System.Data;
using AnatelEtl.Extract;
using AnatelEtl.Load;
using AnatelEtl.Models;
using AnatelEtl.Transform;

namespace AnatelEtl;

/// <summary>Orquestra Extract → Transform → Load para um arquivo ZIP de uma UF.</summary>
public sealed class EtlPipeline(IDbConnection connection, TimeProvider clock)
{
    private const int InsertBatchSize = 10_000;

    public EtlReport Run(string zipPath)
    {
        var report = new EtlReport();
        var transformer = new EmissaoTransformer(clock);

        var estacoes = new Dictionary<(string Uf, string Fistel, string NumEstacao), Estacao>();
        var emissoes = new List<Emissao>();
        var seen = new HashSet<EmissaoKey>();

        foreach (var raw in AnatelZipReader.Read(zipPath))
        {
            report.Increment(EtlReport.RowsRead);
            var outcome = transformer.Transform(raw);

            if (outcome.IsRejected)
            {
                report.Increment(EtlReport.RejectedPrefix + outcome.RejectReason);
                continue;
            }

            Collect(outcome.Estacao!, outcome.Emissao!, estacoes, emissoes, seen, report);
        }

        Load(estacoes.Values, emissoes, report);
        return report;
    }

    private static void Collect(
        Estacao estacao,
        Emissao emissao,
        Dictionary<(string Uf, string Fistel, string NumEstacao), Estacao> estacoes,
        List<Emissao> emissoes,
        HashSet<EmissaoKey> seen,
        EtlReport report)
    {
        if (estacoes.TryAdd(estacao.Key, estacao) && !estacao.CoordenadaValida)
        {
            report.Increment(EtlReport.InvalidCoordinates);
        }

        if (!seen.Add(emissao.Key))
        {
            report.Increment(EtlReport.DuplicateEmissions);
            return;
        }

        emissoes.Add(emissao);
        CountEmissionFlags(emissao, report);
    }

    private static void CountEmissionFlags(Emissao emissao, EtlReport report)
    {
        if (emissao.Vencida)
        {
            report.Increment(EtlReport.ExpiredLicenses);
        }

        if (emissao.AzimuteValido == false)
        {
            report.Increment(EtlReport.InvalidAzimuth);
        }

        if (emissao.ElevacaoValida == false)
        {
            report.Increment(EtlReport.InvalidElevation);
        }

        if (emissao.DuplexCoerente == false)
        {
            report.Increment(EtlReport.InconsistentDuplex);
        }
    }

    private void Load(IReadOnlyCollection<Estacao> estacoes, List<Emissao> emissoes, EtlReport report)
    {
        using var transaction = connection.BeginTransaction();
        SqliteSchema.EnsureCreated(connection, transaction);

        var repository = new EmissaoRepository(connection, transaction);
        foreach (var uf in estacoes.Select(e => e.Uf).Distinct())
        {
            repository.DeleteByUf(uf);
        }

        // Estações antes das emissões, por causa da chave estrangeira.
        repository.InsertEstacoes(estacoes);
        foreach (var batch in emissoes.Chunk(InsertBatchSize))
        {
            repository.InsertEmissoes(batch);
        }

        transaction.Commit();
        report.Increment(EtlReport.StationsLoaded, estacoes.Count);
        report.Increment(EtlReport.EmissionsLoaded, emissoes.Count);
    }
}
