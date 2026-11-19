using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

internal static class Program
{
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static int Main()
    {
        try { Run(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }

    private static void Run()
    {
        var project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var root = Path.GetFullPath(Path.Combine(project, ".."));
        var inputDir = Path.Combine(root, "files");
        var ddlPath = Path.Combine(root, "src", "DDL.sql");
        var outputDir = Path.Combine(root, "db");
        Directory.CreateDirectory(outputDir);
        var dbPath = Path.Combine(outputDir, "chatgpt_gpt_6_completo_r3.db");
        if (File.Exists(dbPath)) File.Delete(dbPath);

        using var db = new SqliteConnection($"Data Source={dbPath};Foreign Keys=True");
        db.Open();
        using (var ddl = db.CreateCommand()) { ddl.CommandText = File.ReadAllText(ddlPath); ddl.ExecuteNonQuery(); }
        using var tx = db.BeginTransaction();
        var stations = new HashSet<StationKey>();
        var emissions = new HashSet<EmissionKey>();
        using var stationCmd = db.CreateCommand(); stationCmd.Transaction = tx;
        stationCmd.CommandText = "INSERT INTO estacao (fistel,num_estacao,uf,cod_municipio,municipio,endereco,latitude,longitude,coordenada_valida,entidade,operadora) VALUES ($f,$n,$u,$cm,$m,$e,$lat,$lon,$cv,$ent,$op)";
        using var emissionCmd = db.CreateCommand(); emissionCmd.Transaction = tx;
        emissionCmd.CommandText = "INSERT INTO emissao (uf,fistel,num_estacao,num_ato,tecnologia,geracao,designacao_emissao,largura_banda_khz,freq_tx_mhz,freq_rx_mhz,duplex_mhz,azimute,potencia_w,ganho_antena,altura_antena_m,data_licenciamento,data_primeiro_licenciamento,data_validade,vencida,anos_licenciada,status,classe_estacao,polarizacao,angulo_elevacao_graus,angulo_meia_potencia_graus,frente_costa_db,cod_tipo_antena,cod_equipamento_antena,cod_equipamento_transmissor,potencia_dbm,azimute_valido,elevacao_valida) VALUES ($u,$f,$n,$ato,$tec,$gen,$des,$band,$txf,$rxf,$dup,$az,$pw,$gain,$height,$dl,$dpl,$dv,$expired,$years,$status,$class,$pol,$elev,$beam,$front,$ant,$eqant,$eqtx,$dbm,$azv,$elv)";

        foreach (var zipPath in Directory.EnumerateFiles(inputDir, "*.zip").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var csvEntries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();
            if (csvEntries.Length != 1) throw new InvalidDataException($"{Path.GetFileName(zipPath)} deve conter exatamente um CSV.");
            using var stream = csvEntries[0].Open();
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
            using var records = CsvRecords(reader).GetEnumerator();
            if (!records.MoveNext()) throw new InvalidDataException($"CSV vazio: {zipPath}");
            var header = records.Current.Select(Clean).ToArray();
            var ix = header.Select((name, i) => (name, i)).ToDictionary(x => x.name!, x => x.i, StringComparer.Ordinal);
            string[] required = ["Status.state","NomeEntidade","NumFistel","NumAto","NumEstacao","EnderecoEstacao","SiglaUf","CodMunicipio","DesignacaoEmissao","Tecnologia","FreqTxMHz","FreqRxMHz","Azimute","CodTipoClasseEstacao","CodTipoAntena","CodEquipamentoAntena","GanhoAntena","FrenteCostaAntena","AnguloMeiaPotenciaAntena","AnguloElevacao","Polarizacao","AlturaAntena","CodEquipamentoTransmissor","PotenciaTransmissorWatts","Latitude","Longitude","DataLicenciamento","DataPrimeiroLicenciamento","DataValidade","Municipio.NomeMunicipio"];
            foreach (var col in required) if (!ix.ContainsKey(col)) throw new InvalidDataException($"Coluna obrigatória ausente ({col}) em {zipPath}");
            while (records.MoveNext())
            {
                var row = records.Current;
                if (row.Length != header.Length) continue;
                string? Get(string name) => Clean(row[ix[name]]);
                var fistel = Get("NumFistel"); var num = Get("NumEstacao"); var uf = Get("SiglaUf")?.ToUpperInvariant();
                if (fistel is null || num is null || uf is null) continue;
                var ent = NormalizeText(Get("NomeEntidade")); var endereco = NormalizeText(Get("EnderecoEstacao"));
                var lat = Number(Get("Latitude")); var lon = Number(Get("Longitude"));
                var sk = new StationKey(uf, fistel, num);
                if (stations.Add(sk))
                {
                    var station = new object?[] { fistel,num,uf,Get("CodMunicipio"),Get("Municipio.NomeMunicipio"),endereco,lat,lon,CoordinateValid(lat,lon),ent,Operator(ent) };
                    Set(stationCmd, ["$f","$n","$u","$cm","$m","$e","$lat","$lon","$cv","$ent","$op"], station);
                    stationCmd.ExecuteNonQuery();
                }

                var ato = Get("NumAto"); var technology = Upper(Get("Tecnologia")); var designation = Upper(Get("DesignacaoEmissao"));
                var txf = Number(Get("FreqTxMHz")); var rxf = Number(Get("FreqRxMHz")); var az = Number(Get("Azimute"));
                var key = new EmissionKey(uf,fistel,num,ato,az,txf,rxf,designation,technology);
                if (!emissions.Add(key)) continue;
                var power = Number(Get("PotenciaTransmissorWatts")); var elevation = Number(Get("AnguloElevacao"));
                var dateLicense = DateValue(Get("DataLicenciamento")); var firstLicense = DateValue(Get("DataPrimeiroLicenciamento")); var expiry = DateValue(Get("DataValidade"));
                double? duplex = txf.HasValue && rxf.HasValue ? Round(txf.Value-rxf.Value,3) : null;
                double? dbm = power is > 0 ? Round(10*Math.Log10(power.Value*1000),1) : null;
                double? years = firstLicense.HasValue ? Round((Today.ToDateTime(TimeOnly.MinValue)-firstLicense.Value.ToDateTime(TimeOnly.MinValue)).TotalDays/365.25,1) : null;
                var vals = new object?[] { uf,fistel,num,ato,technology,Generation(technology),designation,Bandwidth(designation),txf,rxf,duplex,az,power,Number(Get("GanhoAntena")),Number(Get("AlturaAntena")),dateLicense?.ToString("yyyy-MM-dd",Inv),firstLicense?.ToString("yyyy-MM-dd",Inv),expiry?.ToString("yyyy-MM-dd",Inv),expiry.HasValue && expiry.Value<Today ? 1 : 0,years,Get("Status.state"),Get("CodTipoClasseEstacao"),Upper(Get("Polarizacao")),elevation,Number(Get("AnguloMeiaPotenciaAntena")),Number(Get("FrenteCostaAntena")),Get("CodTipoAntena"),Get("CodEquipamentoAntena"),Get("CodEquipamentoTransmissor"),dbm,Tri(az,0,360),Tri(elevation,-90,90) };
                var names = new[] {"$u","$f","$n","$ato","$tec","$gen","$des","$band","$txf","$rxf","$dup","$az","$pw","$gain","$height","$dl","$dpl","$dv","$expired","$years","$status","$class","$pol","$elev","$beam","$front","$ant","$eqant","$eqtx","$dbm","$azv","$elv"};
                Set(emissionCmd,names,vals); emissionCmd.ExecuteNonQuery();
            }
        }
        tx.Commit();
        using var check = db.CreateCommand(); check.CommandText = "PRAGMA foreign_key_check";
        using var result = check.ExecuteReader(); if (result.Read()) throw new InvalidDataException("Falha de integridade referencial no banco gerado.");
    }

    private static void Set(SqliteCommand cmd, string[] names, object?[] values)
    {
        cmd.Parameters.Clear(); for (var i=0;i<names.Length;i++) cmd.Parameters.AddWithValue(names[i], values[i] ?? DBNull.Value);
    }
    private static string? Clean(string? s) { var v=s?.Trim(); return string.IsNullOrEmpty(v)?null:v; }
    private static string? Upper(string? s) => Clean(s)?.ToUpperInvariant();
    private static double? Number(string? s)
    {
        s=Clean(s); if(s is null || s.Contains('.')&&s.Contains(',')) return null;
        s=s.Replace(',','.'); return double.TryParse(s,NumberStyles.Float,Inv,out var d)&&double.IsFinite(d)?d:null;
    }
    private static DateOnly? DateValue(string? s) => DateOnly.TryParseExact(Clean(s),"yyyy-MM-dd",Inv,DateTimeStyles.None,out var d)?d:null;
    private static string? NormalizeText(string? s)
    {
        if(s is null) return null;
        var decomposed=s.Normalize(NormalizationForm.FormD); var b=new StringBuilder();
        foreach(var c in decomposed) if(CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark) b.Append(char.IsLetterOrDigit(c)?char.ToUpperInvariant(c):' ');
        var result=Regex.Replace(b.ToString(),@"\s+"," ").Trim(); return result.Length==0?null:result;
    }
    private static string Operator(string? e)
    {
        if(e is null) return "OUTRAS";
        bool Has(string word)=>Regex.IsMatch(e,$@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(word)}(?![\p{{L}}\p{{N}}])");
        if(Has("TELEFONICA")||Has("VIVO")) return "VIVO";
        if(Has("CLARO")||Has("EMBRATEL")) return "CLARO";
        if(e.StartsWith("TIM",StringComparison.Ordinal) && (e.Length==3||!char.IsLetterOrDigit(e[3]))) return "TIM";
        if(e.StartsWith("OI",StringComparison.Ordinal) && (e.Length==2||!char.IsLetterOrDigit(e[2]))||Has("TELEMAR")||e.Contains("BRASIL TELECOM",StringComparison.Ordinal)) return "OI";
        if(Has("ALGAR")) return "ALGAR"; if(Has("SERCOMTEL")) return "SERCOMTEL"; if(Has("BRISANET")) return "BRISANET"; return "OUTRAS";
    }
    private static string Generation(string? t)=>t switch { null=>"N/D", "GSM" or "CDMA"=>"2G", "WCDMA" or "WDCMA"=>"3G", "LTE"=>"4G", "NR"=>"5G", _=>"OUTRA" };
    private static double? Bandwidth(string? d)
    {
        if(d is null||d.Length<4) return null; var s=d[..4]; var split=-1;
        for(int i=1;i<4;i++) if(s[i] is 'H' or 'K' or 'M' or 'G') { if(split>=0)return null; split=i; }
        if(split<1||!s[..split].All(char.IsAsciiDigit)||!s[(split+1)..].All(char.IsAsciiDigit)) return null;
        var number=s[..split]+(split==3?"":"."+s[(split+1)..]);
        if(!double.TryParse(number,NumberStyles.AllowDecimalPoint,Inv,out var n)) return null;
        return n*(s[split] switch {'H'=>0.001,'K'=>1d,'M'=>1000d,'G'=>1000000d,_=>0});
    }
    private static double Round(double x,int decimals)=>Math.Round(x,decimals,MidpointRounding.AwayFromZero);
    private static int CoordinateValid(double? lat,double? lon)=>lat.HasValue&&lon.HasValue&&!(lat==0&&lon==0)&&lat>=-33.75&&lat<=5.28&&lon>=-73.99&&lon<=-28.83?1:0;
    private static object Tri(double? n,double min,double max)=>n.HasValue?(n>=min&&n<=max?1:0):DBNull.Value;
    private readonly record struct StationKey(string Uf,string Fistel,string Num);
    private readonly record struct EmissionKey(string Uf,string Fistel,string Num,string? Ato,double? Az,double? Tx,double? Rx,string? Designation,string? Technology);

    private static IEnumerable<string[]> CsvRecords(TextReader reader)
    {
        var fields=new List<string>(); var field=new StringBuilder(); bool quoted=false, atStart=true; int raw;
        while((raw=reader.Read())!=-1)
        {
            char c=(char)raw;
            if(quoted)
            {
                if(c=='"') { if(reader.Peek()=='"'){reader.Read();field.Append('"');} else quoted=false; }
                else field.Append(c);
                continue;
            }
            if(c=='"'&&atStart){quoted=true;atStart=false;continue;}
            if(c=='|'){fields.Add(field.ToString());field.Clear();atStart=true;continue;}
            if(c=='\r'||c=='\n')
            {
                if(c=='\r'&&reader.Peek()=='\n')reader.Read();
                fields.Add(field.ToString());field.Clear();atStart=true;yield return fields.ToArray();fields.Clear();continue;
            }
            field.Append(c);atStart=false;
        }
        if(quoted) throw new InvalidDataException("Campo CSV com aspas não terminadas.");
        if(field.Length>0||fields.Count>0){fields.Add(field.ToString());yield return fields.ToArray();}
    }
}
