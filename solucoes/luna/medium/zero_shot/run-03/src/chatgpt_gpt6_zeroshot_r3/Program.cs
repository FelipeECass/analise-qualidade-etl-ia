using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

const string Root = "/workspace";
const string DbPath = Root + "/db/chatgpt_gpt6_zeroshot_r3.db";
var reference = new DateOnly(2026, 10, 15);
try
{
    Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
    if (File.Exists(DbPath)) File.Delete(DbPath);
    using var connection = new SqliteConnection($"Data Source={DbPath}");
    connection.Open();
    using (var pragma = connection.CreateCommand()) { pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF;"; pragma.ExecuteNonQuery(); }
    using (var ddl = connection.CreateCommand()) { ddl.CommandText = File.ReadAllText(Root + "/src/DDL.sql"); ddl.ExecuteNonQuery(); }
    using var transaction = connection.BeginTransaction();
    using var stationCmd = connection.CreateCommand(); stationCmd.Transaction = transaction;
    stationCmd.CommandText = "INSERT INTO estacao(fistel,num_estacao,uf,cod_municipio,municipio,endereco,latitude,longitude,coordenada_valida,entidade,operadora) VALUES($f,$n,$u,$cm,$m,$end,$lat,$lon,$cv,$ent,$op)";
    using var emissionCmd = connection.CreateCommand(); emissionCmd.Transaction = transaction;
    emissionCmd.CommandText = "INSERT INTO emissao(uf,fistel,num_estacao,num_ato,tecnologia,geracao,designacao_emissao,largura_banda_khz,freq_tx_mhz,freq_rx_mhz,duplex_mhz,azimute,potencia_w,ganho_antena,altura_antena_m,data_licenciamento,data_primeiro_licenciamento,data_validade,vencida,anos_licenciada,status,classe_estacao,polarizacao,angulo_elevacao_graus,angulo_meia_potencia_graus,frente_costa_db,cod_tipo_antena,cod_equipamento_antena,cod_equipamento_transmissor,potencia_dbm,azimute_valido,elevacao_valida) VALUES($u,$f,$n,$ato,$tec,$gen,$des,$bw,$tx,$rx,$dup,$az,$pw,$gain,$height,$dl,$dp,$dv,$expired,$years,$status,$class,$pol,$elev,$half,$fc,$ant,$eqant,$eqtx,$dbm,$azv,$ev)";
    string[] stationNames = ["$f","$n","$u","$cm","$m","$end","$lat","$lon","$cv","$ent","$op"];
    foreach (var p in stationNames) stationCmd.Parameters.Add(p, p is "$lat" or "$lon" ? SqliteType.Real : SqliteType.Text);
    using var emCount = emissionCmd;
    var emParams = new[] {"$u","$f","$n","$ato","$tec","$gen","$des","$bw","$tx","$rx","$dup","$az","$pw","$gain","$height","$dl","$dp","$dv","$expired","$years","$status","$class","$pol","$elev","$half","$fc","$ant","$eqant","$eqtx","$dbm","$azv","$ev"};
    foreach (var p in emParams) emissionCmd.Parameters.Add(p, SqliteType.Text);
    var stations = new HashSet<string>(StringComparer.Ordinal);
    var emissions = new HashSet<string>(StringComparer.Ordinal);
    foreach (var zipPath in Directory.GetFiles(Root + "/files", "*.zip").OrderBy(x => x, StringComparer.Ordinal))
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.Single(e => !string.IsNullOrEmpty(e.Name));
        using var sr = new StreamReader(entry.Open(), new UTF8Encoding(false, true));
        var headerLine = sr.ReadLine() ?? throw new InvalidDataException($"CSV vazio: {zipPath}");
        var headers = ParseLine(headerLine).Select((v,i)=>(v,i)).ToDictionary(x=>x.v,x=>x.i,StringComparer.Ordinal);
        string[] required = ["NumFistel","NumEstacao","SiglaUf","NumAto","CodMunicipio","Municipio.NomeMunicipio","NomeEntidade","EnderecoEstacao","Latitude","Longitude","Tecnologia","DesignacaoEmissao","FreqTxMHz","FreqRxMHz","Azimute","PotenciaTransmissorWatts","GanhoAntena","AlturaAntena","AnguloElevacao","AnguloMeiaPotenciaAntena","FrenteCostaAntena","Polarizacao","CodTipoClasseEstacao","CodTipoAntena","CodEquipamentoAntena","CodEquipamentoTransmissor","Status.state","DataLicenciamento","DataPrimeiroLicenciamento","DataValidade"];
        foreach (var h in required) if (!headers.ContainsKey(h)) throw new InvalidDataException($"Coluna ausente {h} em {zipPath}");
        int ix(string name) => headers[name];
        string? get(string[] row, string name) => ix(name) < row.Length ? Clean(row[ix(name)]) : null;
        string? txt(string[] row,string name) => get(row,name);
        string? norm(string[] row,string name) => Normalize(get(row,name));
        double? num(string[] row,string name) => Number(get(row,name));
        string? date(string[] row,string name) => Date(get(row,name));
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            var row = ParseLine(line);
            var f=txt(row,"NumFistel"); var n=txt(row,"NumEstacao"); var u=txt(row,"SiglaUf")?.ToUpperInvariant();
            if (f is null || n is null || u is null) continue;
            var cm=txt(row,"CodMunicipio"); var municipio=txt(row,"Municipio.NomeMunicipio");
            var endereco=norm(row,"EnderecoEstacao"); var lat=num(row,"Latitude"); var lon=num(row,"Longitude"); var entidade=norm(row,"NomeEntidade");
            bool coord=lat.HasValue && lon.HasValue && !(lat==0 && lon==0) && lat>=-33.75 && lat<=5.28 && lon>=-73.99 && lon<=-28.83;
            var operadora=Operator(entidade);
            var stationKey=Key(u,f,n);
            if (stations.Add(stationKey))
            {
                object?[] values=[f,n,u,cm,municipio,endereco,lat,lon,coord?1:0,entidade,operadora];
                Set(stationCmd,stationNames,values); stationCmd.ExecuteNonQuery();
            }
            var ato=txt(row,"NumAto"); var tec=txt(row,"Tecnologia")?.ToUpperInvariant(); var gen=Generation(tec);
            var des=txt(row,"DesignacaoEmissao")?.ToUpperInvariant();
            var bw=Bandwidth(des); var tx=num(row,"FreqTxMHz"); var rx=num(row,"FreqRxMHz"); var az=num(row,"Azimute"); var pw=num(row,"PotenciaTransmissorWatts");
            var gain=num(row,"GanhoAntena"); var height=num(row,"AlturaAntena"); var elev=num(row,"AnguloElevacao"); var half=num(row,"AnguloMeiaPotenciaAntena"); var fc=num(row,"FrenteCostaAntena");
            var status=txt(row,"Status.state"); var cls=txt(row,"CodTipoClasseEstacao"); var pol=txt(row,"Polarizacao")?.ToUpperInvariant();
            var ant=txt(row,"CodTipoAntena"); var eqant=txt(row,"CodEquipamentoAntena"); var eqtx=txt(row,"CodEquipamentoTransmissor");
            var dl=date(row,"DataLicenciamento"); var dp=date(row,"DataPrimeiroLicenciamento"); var dv=date(row,"DataValidade");
            double? dup=tx.HasValue&&rx.HasValue ? RoundDuplex(tx.Value-rx.Value,3) : null;
            double? dbm=pw.HasValue&&pw>0 ? Round(10*Math.Log10(pw.Value*1000),1) : null;
            double? years=dp is null ? null : Round((reference.DayNumber-DateOnly.ParseExact(dp,"yyyy-MM-dd",CultureInfo.InvariantCulture).DayNumber)/365.25,1);
            int? azv=az is null?null:(az>=0&&az<=360?1:0); int? ev=elev is null?null:(elev>=-90&&elev<=90?1:0);
            var dedupKey=Key(u,f,n,ato,az,tx,rx,des,tec);
            if (!emissions.Add(dedupKey)) continue;
            object?[] evs=[u,f,n,ato,tec,gen,des,bw,tx,rx,dup,az,pw,gain,height,dl,dp,dv,dv is not null&&DateOnly.ParseExact(dv,"yyyy-MM-dd",CultureInfo.InvariantCulture)<reference?1:0,years,status,cls,pol,elev,half,fc,ant,eqant,eqtx,dbm,azv,ev];
            Set(emissionCmd,emParams,evs); emissionCmd.ExecuteNonQuery();
        }
    }
    transaction.Commit();
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode=1; }

static void Set(SqliteCommand cmd,string[] names,object?[] values) { for(int i=0;i<names.Length;i++) cmd.Parameters[names[i]].Value=values[i] ?? DBNull.Value; }
static string? Clean(string? s) { if(s is null)return null; s=s.Trim(); return s.Length==0?null:s; }
static double? Number(string? s) { if(s is null)return null; s=s.Replace(',','.'); if(double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out var d)&&double.IsFinite(d))return d; return null; }
static string? Date(string? s) => s is not null&&DateOnly.TryParseExact(s,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)?d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture):null;
static double Round(double x,int digits)=>Math.Round(x,digits,MidpointRounding.AwayFromZero);
static double RoundDuplex(double x,int digits)=> (double)Math.Round((decimal)x,digits,MidpointRounding.ToEven);
static string? Normalize(string? s) { if(s is null)return null; var d=s.Normalize(NormalizationForm.FormD); var b=new StringBuilder(); foreach(var c in d) { if(CharUnicodeInfo.GetUnicodeCategory(c)==UnicodeCategory.NonSpacingMark)continue; b.Append(char.IsLetterOrDigit(c)?char.ToUpperInvariant(c):' '); } var result=Regex.Replace(b.ToString(),"\\s+"," ").Trim(); return result.Length==0?null:result; }
static string Operator(string? e) { if(e is null)return "OUTRAS"; bool word(string x)=>Regex.IsMatch(e,@"\b"+Regex.Escape(x)+@"\b"); if(word("TELEFONICA")||word("VIVO"))return "VIVO"; if(word("CLARO")||word("EMBRATEL"))return "CLARO"; if(Regex.IsMatch(e,@"^TIM\b"))return "TIM"; if(Regex.IsMatch(e,@"^OI\b")||word("TELEMAR")||Regex.IsMatch(e,@"\bBRASIL TELECOM\b"))return "OI"; if(word("ALGAR"))return "ALGAR"; if(word("SERCOMTEL"))return "SERCOMTEL"; if(word("BRISANET"))return "BRISANET"; return "OUTRAS"; }
static string Generation(string? t)=>t switch {"GSM" or "CDMA"=>"2G","WCDMA" or "WDCMA"=>"3G","LTE"=>"4G","NR"=>"5G",null=>"N/D",_=>"OUTRA"};
static double? Bandwidth(string? s) { if(s is null||s.Length<3)return null; var a=s.AsSpan(0,Math.Min(4,s.Length)); int split=-1; for(int i=1;i<a.Length;i++)if(a[i] is 'H' or 'K' or 'M' or 'G'){split=i;break;} if(split<0)return null; for(int i=0;i<split;i++)if(!char.IsAsciiDigit(a[i]))return null; for(int i=split+1;i<a.Length;i++)if(!char.IsAsciiDigit(a[i]))return null; if(!double.TryParse(a[..split],NumberStyles.None,CultureInfo.InvariantCulture,out var whole))return null; var fracText=a[(split+1)..].ToString(); double frac=0; if(fracText.Length>0&&!double.TryParse("0."+fracText,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out frac))return null; var factor=a[split] switch {'H'=>.001,'K'=>1d,'M'=>1000d,'G'=>1000000d,_=>0d}; return (whole+frac)*factor; }
static string Key(params object?[] values) { var b=new StringBuilder(); foreach(var v in values) { string s=v switch {null=>"N",double d=>"D"+d.ToString("R",CultureInfo.InvariantCulture),_=>"S"+Convert.ToString(v,CultureInfo.InvariantCulture)}; b.Append(s.Length).Append(':').Append(s); } return b.ToString(); }
static string[] ParseLine(string line) { var fields=new List<string>(); var b=new StringBuilder(); bool quoted=false; for(int i=0;i<line.Length;i++){char c=line[i]; if(quoted){if(c=='"'){if(i+1<line.Length&&line[i+1]=='"'){b.Append('"');i++;}else quoted=false;}else b.Append(c);}else if(c=='|' ){fields.Add(b.ToString());b.Clear();}else if(c=='"'&&b.Length==0)quoted=true;else b.Append(c);} fields.Add(b.ToString()); return fields.ToArray(); }
