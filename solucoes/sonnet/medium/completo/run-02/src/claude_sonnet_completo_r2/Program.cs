using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Etl;

public static class Program
{
    private const string WinRoot = @"D:\Projects\TCC";
    private const string Name = "claude_sonnet_completo_r2";

    static string FindRoot()
    {
        if (Directory.Exists(Path.Combine(WinRoot, "files"))) return WinRoot;
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var d = new DirectoryInfo(start);
            while (d != null)
            {
                if (Directory.Exists(Path.Combine(d.FullName, "files")) && File.Exists(Path.Combine(d.FullName, "src", "DDL.sql")))
                    return d.FullName;
                d = d.Parent;
            }
        }
        throw new DirectoryNotFoundException("Pasta raiz do projeto (com files/ e src/DDL.sql) não encontrada.");
    }

    public static int Main()
    {
        try { Run(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine("ERRO: " + ex); return 1; }
    }

    static string? Get(List<string> rec, int idx) => idx >= 0 && idx < rec.Count ? Cleaner.Text(rec[idx]) : null;

    static void Run()
    {
        var root = FindRoot();
        var dbPath = Path.Combine(root, "db", Name + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        foreach (var suf in new[] { "", "-journal", "-wal", "-shm" }) if (File.Exists(dbPath + suf)) File.Delete(dbPath + suf);
        var ddl = File.ReadAllText(Path.Combine(root, "src", "DDL.sql"));
        var zips = Directory.GetFiles(Path.Combine(root, "files"), "*.zip").OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (zips.Count == 0) throw new FileNotFoundException("Nenhum ZIP em files/.");

        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using (var c = conn.CreateCommand()) { c.CommandText = "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF;"; c.ExecuteNonQuery(); }
        using (var c = conn.CreateCommand()) { c.CommandText = ddl; c.ExecuteNonQuery(); }

        using var tx = conn.BeginTransaction();
        using var insE = conn.CreateCommand();
        insE.Transaction = tx;
        insE.CommandText = "INSERT INTO estacao (fistel,num_estacao,uf,cod_municipio,municipio,endereco,latitude,longitude,coordenada_valida,entidade,operadora) VALUES ($a,$b,$c,$d,$e,$f,$g,$h,$i,$j,$k)";
        var pE = new SqliteParameter[11];
        foreach (var (n, i) in "abcdefghijk".Select((ch, i) => ("$" + ch, i))) { pE[i] = insE.CreateParameter(); pE[i].ParameterName = n; insE.Parameters.Add(pE[i]); }
        insE.Prepare();

        string[] cols = { "uf","fistel","num_estacao","num_ato","tecnologia","geracao","designacao_emissao","largura_banda_khz","freq_tx_mhz","freq_rx_mhz","duplex_mhz","azimute","potencia_w","ganho_antena","altura_antena_m","data_licenciamento","data_primeiro_licenciamento","data_validade","vencida","anos_licenciada","status","classe_estacao","polarizacao","angulo_elevacao_graus","angulo_meia_potencia_graus","frente_costa_db","cod_tipo_antena","cod_equipamento_antena","cod_equipamento_transmissor","potencia_dbm","azimute_valido","elevacao_valida" };
        using var insM = conn.CreateCommand();
        insM.Transaction = tx;
        insM.CommandText = $"INSERT INTO emissao ({string.Join(",", cols)}) VALUES ({string.Join(",", cols.Select((_, i) => "$p" + i))})";
        var pM = new SqliteParameter[cols.Length];
        for (int i = 0; i < cols.Length; i++) { pM[i] = insM.CreateParameter(); pM[i].ParameterName = "$p" + i; insM.Parameters.Add(pM[i]); }
        insM.Prepare();

        var stations = new HashSet<string>();
        var emissions = new HashSet<string>();
        long nRead = 0, nRejected = 0, nSt = 0, nEm = 0, nDup = 0;
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        static object V(object? o) => o ?? DBNull.Value;
        static object? D(double? d) => d;
        static string? Dt(DateTime? d) => d?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        static object? B(bool b) => b ? 1L : 0L;

        foreach (var zip in zips)
        {
            using var za = ZipFile.OpenRead(zip);
            var entry = za.Entries.FirstOrDefault(e => e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException("ZIP sem CSV: " + zip);
            using var stream = entry.Open();
            using var sr = new StreamReader(stream, new UTF8Encoding(false), true, 1 << 16);
            var csv = new CsvReader(sr);
            var header = csv.ReadRecord() ?? throw new InvalidDataException("CSV vazio: " + zip);
            var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Count; i++)
            {
                var h = header[i].Trim().TrimStart('\uFEFF');
                idx.TryAdd(h, i);
            }
            int Ix(string n) => idx.TryGetValue(n, out var v) ? v : -1;
            foreach (var req in new[] { "NumFistel", "NumEstacao", "SiglaUf" })
                if (Ix(req) < 0) throw new InvalidDataException($"Coluna {req} ausente em {zip}");
            int iFistel = Ix("NumFistel"), iEst = Ix("NumEstacao"), iUf = Ix("SiglaUf"), iAto = Ix("NumAto"),
                iCodMun = Ix("CodMunicipio"), iMun = Ix("Municipio.NomeMunicipio"), iEnt = Ix("NomeEntidade"),
                iEnd = Ix("EnderecoEstacao"), iLat = Ix("Latitude"), iLon = Ix("Longitude"), iTec = Ix("Tecnologia"),
                iDes = Ix("DesignacaoEmissao"), iTx = Ix("FreqTxMHz"), iRx = Ix("FreqRxMHz"), iAz = Ix("Azimute"),
                iPot = Ix("PotenciaTransmissorWatts"), iGan = Ix("GanhoAntena"), iAlt = Ix("AlturaAntena"),
                iEl = Ix("AnguloElevacao"), iMp = Ix("AnguloMeiaPotenciaAntena"), iFc = Ix("FrenteCostaAntena"),
                iPol = Ix("Polarizacao"), iCls = Ix("CodTipoClasseEstacao"), iTa = Ix("CodTipoAntena"),
                iEa = Ix("CodEquipamentoAntena"), iEt = Ix("CodEquipamentoTransmissor"), iSt = Ix("Status.state"),
                iDl = Ix("DataLicenciamento"), iDp = Ix("DataPrimeiroLicenciamento"), iDv = Ix("DataValidade");

            List<string>? rec;
            while ((rec = csv.ReadRecord()) != null)
            {
                if (rec.Count == 0) continue;
                nRead++;
                var fistel = Get(rec, iFistel);
                var numEst = Get(rec, iEst);
                var uf = Cleaner.Upper(Get(rec, iUf));
                if (fistel == null || numEst == null || uf == null) { nRejected++; continue; }

                var skey = uf + "\u0001" + fistel + "\u0001" + numEst;
                if (stations.Add(skey))
                {
                    var ent = Cleaner.Normalize(Get(rec, iEnt));
                    var lat = Cleaner.Number(Get(rec, iLat));
                    var lon = Cleaner.Number(Get(rec, iLon));
                    pE[0].Value = fistel; pE[1].Value = numEst; pE[2].Value = uf;
                    pE[3].Value = V(Get(rec, iCodMun)); pE[4].Value = V(Get(rec, iMun));
                    pE[5].Value = V(Cleaner.Normalize(Get(rec, iEnd)));
                    pE[6].Value = V(D(lat)); pE[7].Value = V(D(lon));
                    pE[8].Value = Derivations.CoordenadaValida(lat, lon) ? 1L : 0L;
                    pE[9].Value = V(ent); pE[10].Value = Derivations.Operadora(ent);
                    insE.ExecuteNonQuery(); nSt++;
                }

                var ato = Get(rec, iAto);
                var tec = Cleaner.Upper(Get(rec, iTec));
                var des = Cleaner.Upper(Get(rec, iDes));
                var ftx = Cleaner.Number(Get(rec, iTx));
                var rx = Cleaner.Number(Get(rec, iRx));
                var az = Cleaner.Number(Get(rec, iAz));
                var ekey = string.Join("\u0001", skey, ato ?? "\u0002", tec ?? "\u0002", des ?? "\u0002",
                    ftx?.ToString("R", inv) ?? "\u0002", rx?.ToString("R", inv) ?? "\u0002", az?.ToString("R", inv) ?? "\u0002");
                if (!emissions.Add(ekey)) { nDup++; continue; }

                var pot = Cleaner.Number(Get(rec, iPot));
                var el = Cleaner.Number(Get(rec, iEl));
                var dl = Cleaner.Date(Get(rec, iDl));
                var dp = Cleaner.Date(Get(rec, iDp));
                var dv = Cleaner.Date(Get(rec, iDv));
                object?[] vals =
                {
                    uf, fistel, numEst, ato, tec, Derivations.Geracao(tec), des, D(Derivations.LarguraBandaKhz(des)),
                    D(ftx), D(rx), D(Derivations.Duplex(ftx, rx)), D(az), D(pot),
                    D(Cleaner.Number(Get(rec, iGan))), D(Cleaner.Number(Get(rec, iAlt))),
                    Dt(dl), Dt(dp), Dt(dv), B(Derivations.Vencida(dv)), D(Derivations.AnosLicenciada(dp)),
                    Get(rec, iSt), Get(rec, iCls), Cleaner.Upper(Get(rec, iPol)),
                    D(el), D(Cleaner.Number(Get(rec, iMp))), D(Cleaner.Number(Get(rec, iFc))),
                    Get(rec, iTa), Get(rec, iEa), Get(rec, iEt),
                    D(Derivations.PotenciaDbm(pot)),
                    Derivations.Range(az, 0, 360) is int a ? (long)a : null,
                    Derivations.Range(el, -90, 90) is int e ? (long)e : null,
                };
                for (int i = 0; i < vals.Length; i++) pM[i].Value = V(vals[i]);
                insM.ExecuteNonQuery(); nEm++;
            }
            Console.WriteLine($"{Path.GetFileName(zip)}: ok (estacoes={nSt}, emissoes={nEm})");
        }
        tx.Commit();
        Console.WriteLine($"Lidas={nRead} rejeitadas={nRejected} estacoes={nSt} emissoes={nEm} duplicadas={nDup}");
    }
}
