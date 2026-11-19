using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Data;
using Microsoft.Data.Sqlite;

internal static class Program
{
    static readonly DateTime Today = new(2026, 10, 15);
    static string Clean(string? s) { s = s?.Trim(); return string.IsNullOrEmpty(s) ? null! : s; }
    static string? Upper(string? s) => Clean(s)?.ToUpperInvariant();
    static double? Number(string? s)
    {
        s = Clean(s); if (s == null) return null;
        s = s.Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
    }
    static string? Date(string? s) => DateTime.TryParseExact(Clean(s), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("yyyy-MM-dd") : null;
    static string? Normalize(string? s)
    {
        s = Clean(s); if (s == null) return null;
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var b = new StringBuilder();
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                b.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ');
        var result = Regex.Replace(b.ToString(), @"\s+", " ").Trim(); return result.Length == 0 ? null : result;
    }
    static string Generation(string? t) => t?.ToUpperInvariant() switch { "GSM" or "CDMA" => "2G", "WCDMA" or "WDCMA" => "3G", "LTE" => "4G", "NR" => "5G", null or "" => "N/D", _ => "OUTRA" };
    static string Operator(string? e)
    {
        if (e == null) return "OUTRAS";
        bool has(string x) => Regex.IsMatch(e, $@"\b{Regex.Escape(x)}\b");
        if (has("TELEFONICA") || has("VIVO")) return "VIVO";
        if (has("CLARO") || has("EMBRATEL")) return "CLARO";
        if (Regex.IsMatch(e, @"^TIM\b")) return "TIM";
        if (Regex.IsMatch(e, @"^OI\b") || has("TELEMAR") || Regex.IsMatch(e, @"\bBRASIL TELECOM\b")) return "OI";
        if (has("ALGAR")) return "ALGAR";
        if (has("SERCOMTEL")) return "SERCOMTEL";
        if (has("BRISANET")) return "BRISANET";
        return "OUTRAS";
    }
    static double? Bandwidth(string? s)
    {
        if (s == null || s.Length < 3) return null;
        var m = Regex.Match(s[..Math.Min(4, s.Length)], @"^(\d+)([HKMG])(\d*)$");
        if (!m.Success || !double.TryParse(m.Groups[1].Value + "." + (m.Groups[3].Value.Length == 0 ? "0" : m.Groups[3].Value), CultureInfo.InvariantCulture, out var n)) return null;
        var f = m.Groups[2].Value[0] switch { 'H' => .001, 'K' => 1, 'M' => 1000, 'G' => 1_000_000, _ => 0 };
        return n * f;
    }
    static double? Round(double? x, int digits) => x is null ? null : Math.Round(x.Value, digits, MidpointRounding.AwayFromZero);
    static IEnumerable<string[]> Csv(TextReader r)
    {
        var row = new List<string>(); var field = new StringBuilder(); bool quoted = false; int ch;
        while ((ch = r.Read()) != -1)
        {
            char c = (char)ch;
            if (quoted)
            {
                if (c == '"') { if (r.Peek() == '"') { r.Read(); field.Append('"'); } else quoted = false; }
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == '|') { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n') { row.Add(field.ToString().TrimEnd('\r')); field.Clear(); yield return row.ToArray(); row.Clear(); }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString().TrimEnd('\r')); yield return row.ToArray(); }
    }
    static object Db(object? x) => x ?? DBNull.Value;
    static void Main()
    {
        try { Run(); }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    }
    static void Run()
    {
        var project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../"));
        var root = Path.GetFullPath(Path.Combine(project, "../.."));
        var ddl = File.ReadAllText(Path.Combine(root, "src/DDL.sql"));
        var dbDir = Path.Combine(root, "db"); Directory.CreateDirectory(dbDir);
        var db = Path.Combine(dbDir, "chatgpt_gpt6_zeroshot_r1.db"); if (File.Exists(db)) File.Delete(db);
        using var con = new SqliteConnection($"Data Source={db}"); con.Open();
        using (var cmd = con.CreateCommand()) { cmd.CommandText = "PRAGMA foreign_keys=ON;" + ddl; cmd.ExecuteNonQuery(); }
        var stationSeen = new HashSet<string>(); var emissionSeen = new HashSet<string>();
        using var tx = con.BeginTransaction();
        foreach (var zip in Directory.GetFiles(Path.Combine(root, "files"), "*.zip").OrderBy(x => x, StringComparer.Ordinal))
        {
            using var archive = ZipFile.OpenRead(zip);
            var entry = archive.Entries.FirstOrDefault(x => !string.IsNullOrEmpty(x.Name)) ?? throw new InvalidDataException($"ZIP vazio: {zip}");
            using var stream = entry.Open(); using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
            using var rows = Csv(reader).GetEnumerator(); if (!rows.MoveNext()) continue;
            var heads = rows.Current.Select(x => x.Trim()).ToArray(); var idx = heads.Select((h,i)=>(h,i)).ToDictionary(x=>x.h,x=>x.i,StringComparer.Ordinal);
            string? Get(string[] r,string h) => idx.TryGetValue(h,out var i) && i<r.Length ? Clean(r[i]) : null;
            using var stationCmd = con.CreateCommand(); stationCmd.Transaction=tx; stationCmd.CommandText="INSERT INTO estacao (fistel,num_estacao,uf,cod_municipio,municipio,endereco,latitude,longitude,coordenada_valida,entidade,operadora) VALUES ($f,$n,$u,$cm,$m,$en,$la,$lo,$cv,$e,$o)";
            using var emCmd = con.CreateCommand(); emCmd.Transaction=tx; emCmd.CommandText="INSERT INTO emissao (uf,fistel,num_estacao,num_ato,tecnologia,geracao,designacao_emissao,largura_banda_khz,freq_tx_mhz,freq_rx_mhz,duplex_mhz,azimute,potencia_w,ganho_antena,altura_antena_m,data_licenciamento,data_primeiro_licenciamento,data_validade,vencida,anos_licenciada,status,classe_estacao,polarizacao,angulo_elevacao_graus,angulo_meia_potencia_graus,frente_costa_db,cod_tipo_antena,cod_equipamento_antena,cod_equipamento_transmissor,potencia_dbm,azimute_valido,elevacao_valida) VALUES ($u,$f,$n,$ato,$tec,$g,$des,$bw,$ft,$fr,$du,$az,$pw,$ga,$ha,$dl,$dp,$dv,$ve,$al,$st,$cl,$po,$ae,$amp,$fc,$cta,$cea,$cet,$pdb,$av,$ev)";
            while(rows.MoveNext())
            {
                var r=rows.Current; string? f=Get(r,"NumFistel"), n=Get(r,"NumEstacao"), u=Upper(Get(r,"SiglaUf")); if(f==null||n==null||u==null) continue;
                string? entidade=Normalize(Get(r,"NomeEntidade")), endereco=Normalize(Get(r,"EnderecoEstacao"));
                double? lat=Number(Get(r,"Latitude")), lon=Number(Get(r,"Longitude"));
                var sk=string.Join("\u001f",u,f,n);
                if(stationSeen.Add(sk))
                {
                    bool cv=lat.HasValue&&lon.HasValue&&!(lat==0&&lon==0)&&lat>=-33.75&&lat<=5.28&&lon>=-73.99&&lon<=-28.83;
                    Set(stationCmd,"$f",f);Set(stationCmd,"$n",n);Set(stationCmd,"$u",u);Set(stationCmd,"$cm",Get(r,"CodMunicipio"));Set(stationCmd,"$m",Get(r,"Municipio.NomeMunicipio"));Set(stationCmd,"$en",endereco);Set(stationCmd,"$la",lat);Set(stationCmd,"$lo",lon);Set(stationCmd,"$cv",cv?1:0);Set(stationCmd,"$e",entidade);Set(stationCmd,"$o",Operator(entidade));stationCmd.ExecuteNonQuery();
                }
                string? ato=Get(r,"NumAto"), tec=Upper(Get(r,"Tecnologia")), des=Upper(Get(r,"DesignacaoEmissao"));
                double? az=Number(Get(r,"Azimute")), ft=Number(Get(r,"FreqTxMHz")), fr=Number(Get(r,"FreqRxMHz"));
                var ek=string.Join("\u001f",new[]{u,f,n,ato??"\0",az?.ToString("R",CultureInfo.InvariantCulture)??"\0",ft?.ToString("R",CultureInfo.InvariantCulture)??"\0",fr?.ToString("R",CultureInfo.InvariantCulture)??"\0",des??"\0",tec??"\0"});
                if(!emissionSeen.Add(ek)) continue;
                var pw=Number(Get(r,"PotenciaTransmissorWatts")); var dp=Date(Get(r,"DataPrimeiroLicenciamento")); var dv=Date(Get(r,"DataValidade")); var dl=Date(Get(r,"DataLicenciamento")); var ae=Number(Get(r,"AnguloElevacao"));
                var diff=ft.HasValue&&fr.HasValue?Round(ft-fr,3):null;
                var pdb=pw>0?Round(10*Math.Log10(pw.Value*1000),1):null;
                var years=dp==null?null:Round((Today-DateTime.ParseExact(dp,"yyyy-MM-dd",CultureInfo.InvariantCulture)).TotalDays/365.25,1);
                object? av=az==null?null:az>=0&&az<=360?1:0, ev=ae==null?null:ae>=-90&&ae<=90?1:0;
                object?[] vals={u,f,n,ato,tec,Generation(tec),des,Bandwidth(des),ft,fr,diff,az,pw,Number(Get(r,"GanhoAntena")),Number(Get(r,"AlturaAntena")),dl,dp,dv,dv!=null&&DateTime.ParseExact(dv,"yyyy-MM-dd",CultureInfo.InvariantCulture)<Today?1:0,years,Get(r,"Status.state"),Get(r,"CodTipoClasseEstacao"),Upper(Get(r,"Polarizacao")),ae,Number(Get(r,"AnguloMeiaPotenciaAntena")),Number(Get(r,"FrenteCostaAntena")),Get(r,"CodTipoAntena"),Get(r,"CodEquipamentoAntena"),Get(r,"CodEquipamentoTransmissor"),pdb,av,ev};
                string[] names={"$u","$f","$n","$ato","$tec","$g","$des","$bw","$ft","$fr","$du","$az","$pw","$ga","$ha","$dl","$dp","$dv","$ve","$al","$st","$cl","$po","$ae","$amp","$fc","$cta","$cea","$cet","$pdb","$av","$ev"};
                for(int i=0;i<names.Length;i++) Set(emCmd,names[i],vals[i]); emCmd.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }
    static void Set(SqliteCommand c,string name,object? value)
    {
        var p=c.Parameters.Contains(name)?c.Parameters[name]:c.Parameters.Add(name,SqliteType.Text);
        p.Value=Db(value);
    }
}
