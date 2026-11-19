using System.IO.Compression;
using System.Text;

sealed class EtlPipeline
{
    readonly string _filesDir;
    readonly Database _db;
    readonly HashSet<(string, string, string)> _stations = new();
    readonly HashSet<(ulong, ulong)> _emissions = new();

    public long RowsRead, RowsRejected, Stations, Emissions, Duplicates;

    public EtlPipeline(string filesDir, Database db) { _filesDir = filesDir; _db = db; }

    public void Run()
    {
        var zips = Directory.GetFiles(_filesDir, "*.zip").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToList();
        if (zips.Count == 0) throw new InvalidOperationException($"Nenhum ZIP encontrado em {_filesDir}");
        foreach (var zip in zips)
        {
            using var za = ZipFile.OpenRead(zip);
            var entry = za.Entries.FirstOrDefault(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                ?? za.Entries.First(e => e.Length > 0);
            using var st = entry.Open();
            using var sr = new StreamReader(st, new UTF8Encoding(false), true, 1 << 20);
            ProcessFile(sr);
            Console.WriteLine($"{Path.GetFileName(zip)}: lidas={RowsRead} estacoes={Stations} emissoes={Emissions}");
        }
        _db.Commit();
    }

    void ProcessFile(TextReader reader)
    {
        var csv = new CsvReader(reader);
        var f = new List<string>(48);
        if (!csv.ReadRecord(f)) return;
        var idx = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < f.Count; i++) idx[f[i].Trim().TrimStart('\uFEFF')] = i;
        int Col(string n) => idx.TryGetValue(n, out var i) ? i : throw new InvalidDataException($"Coluna ausente: {n}");
        int cState = Col("Status.state"), cEnt = Col("NomeEntidade"), cFis = Col("NumFistel"), cAto = Col("NumAto"),
            cEst = Col("NumEstacao"), cEnd = Col("EnderecoEstacao"), cUf = Col("SiglaUf"), cCodMun = Col("CodMunicipio"),
            cDes = Col("DesignacaoEmissao"), cTec = Col("Tecnologia"), cTx = Col("FreqTxMHz"), cRx = Col("FreqRxMHz"),
            cAz = Col("Azimute"), cClasse = Col("CodTipoClasseEstacao"), cTipoAnt = Col("CodTipoAntena"),
            cEqAnt = Col("CodEquipamentoAntena"), cGanho = Col("GanhoAntena"), cFC = Col("FrenteCostaAntena"),
            cMeia = Col("AnguloMeiaPotenciaAntena"), cElev = Col("AnguloElevacao"), cPol = Col("Polarizacao"),
            cAlt = Col("AlturaAntena"), cEqTx = Col("CodEquipamentoTransmissor"), cPot = Col("PotenciaTransmissorWatts"),
            cLat = Col("Latitude"), cLon = Col("Longitude"), cDL = Col("DataLicenciamento"),
            cDPL = Col("DataPrimeiroLicenciamento"), cDV = Col("DataValidade"), cMun = Col("Municipio.NomeMunicipio");
        int need = idx.Values.Max() + 1;

        while (csv.ReadRecord(f))
        {
            if (f.Count == 1 && f[0].Length == 0) continue;
            RowsRead++;
            while (f.Count < need) f.Add("");

            var fistel = Cleaner.Text(f[cFis]);
            var numEst = Cleaner.Text(f[cEst]);
            var uf = Cleaner.Upper(Cleaner.Text(f[cUf]));
            if (fistel == null || numEst == null || uf == null) { RowsRejected++; continue; }

            var skey = (uf, fistel, numEst);
            if (_stations.Add(skey))
            {
                var entidade = Cleaner.Normalize(f[cEnt]);
                var lat = Cleaner.Number(f[cLat]);
                var lon = Cleaner.Number(f[cLon]);
                _db.InsertEstacao(new object?[]
                {
                    fistel, numEst, uf, Cleaner.Text(f[cCodMun]), Cleaner.Text(f[cMun]), Cleaner.Normalize(f[cEnd]),
                    lat, lon, Derivations.CoordenadaValida(lat, lon) ? 1 : 0, entidade, Derivations.Operadora(entidade)
                });
                Stations++;
            }

            var numAto = Cleaner.Text(f[cAto]);
            var tec = Cleaner.Upper(Cleaner.Text(f[cTec]));
            var des = Cleaner.Upper(Cleaner.Text(f[cDes]));
            var tx = Cleaner.Number(f[cTx]);
            var rx = Cleaner.Number(f[cRx]);
            var az = Cleaner.Number(f[cAz]);

            if (!_emissions.Add(Hash(uf, fistel, numEst, numAto, az, tx, rx, des, tec))) { Duplicates++; continue; }

            var pot = Cleaner.Number(f[cPot]);
            var elev = Cleaner.Number(f[cElev]);
            var dl = Cleaner.Date(f[cDL]);
            var dpl = Cleaner.Date(f[cDPL]);
            var dv = Cleaner.Date(f[cDV]);
            var azOk = Derivations.Between(az, 0, 360);
            var elOk = Derivations.Between(elev, -90, 90);

            _db.InsertEmissao(new object?[]
            {
                uf, fistel, numEst, numAto, tec, Derivations.Geracao(tec), des, Derivations.LarguraBandaKhz(des),
                tx, rx, Derivations.Duplex(tx, rx), az, pot, Cleaner.Number(f[cGanho]), Cleaner.Number(f[cAlt]),
                dl, dpl, dv, Derivations.Vencida(dv) ? 1 : 0, Derivations.AnosLicenciada(dpl),
                Cleaner.Text(f[cState]), Cleaner.Text(f[cClasse]), Cleaner.Upper(Cleaner.Text(f[cPol])), elev,
                Cleaner.Number(f[cMeia]), Cleaner.Number(f[cFC]), Cleaner.Text(f[cTipoAnt]),
                Cleaner.Text(f[cEqAnt]), Cleaner.Text(f[cEqTx]), Derivations.PotenciaDbm(pot),
                azOk.HasValue ? (azOk.Value ? 1 : 0) : null, elOk.HasValue ? (elOk.Value ? 1 : 0) : null
            });
            Emissions++;
        }
    }

    static (ulong, ulong) Hash(string uf, string fistel, string est, string? ato, double? az, double? tx, double? rx, string? des, string? tec)
    {
        var sb = new StringBuilder(128);
        void A(string? s) { if (s == null) sb.Append('\u0001'); else sb.Append(s); sb.Append('\u0000'); }
        void N(double? d) => A(d.HasValue ? d.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : null);
        A(uf); A(fistel); A(est); A(ato); N(az); N(tx); N(rx); A(des); A(tec);
        ulong h1 = 14695981039346656037UL, h2 = 0x9E3779B97F4A7C15UL;
        foreach (char c in sb.ToString())
        {
            h1 = (h1 ^ c) * 1099511628211UL;
            h2 = (h2 + c) * 0xFF51AFD7ED558CCDUL; h2 ^= h2 >> 29;
        }
        return (h1, h2);
    }
}
