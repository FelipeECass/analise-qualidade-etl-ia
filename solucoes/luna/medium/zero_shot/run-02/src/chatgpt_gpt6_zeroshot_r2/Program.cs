using System.Text.Json;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Data.Sqlite;

const string Root = "/workspace";
const string DbPath = Root + "/db/chatgpt_gpt6_zeroshot_r2.db";
const string DdlPath = Root + "/src/DDL.sql";
DateOnly today = new(2026, 10, 15);

try
{
    Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
    if (File.Exists(DbPath)) File.Delete(DbPath);
    using var db = new SqliteConnection($"Data Source={DbPath}");
    db.Open();
    db.CreateFunction("regexp", (string pattern, string? value) => value != null && Regex.IsMatch(value, pattern));
    db.CreateFunction("unused", () => 0);
    db.ExecuteNonQuery(File.ReadAllText(DdlPath));
    using var tx = db.BeginTransaction();
    var stationSeen = new HashSet<(string uf,string fistel,string num)>();
    var emissionSeen = new HashSet<string>();
    using var st = db.CreateCommand(); st.Transaction=tx;
    st.CommandText="INSERT INTO estacao(fistel,num_estacao,uf,cod_municipio,municipio,endereco,latitude,longitude,coordenada_valida,entidade,operadora) VALUES($f,$n,$u,$cm,$m,$e,$lat,$lon,$cv,$ent,$op)";
    using var em = db.CreateCommand(); em.Transaction=tx;
    em.CommandText="INSERT INTO emissao(uf,fistel,num_estacao,num_ato,tecnologia,geracao,designacao_emissao,largura_banda_khz,freq_tx_mhz,freq_rx_mhz,duplex_mhz,azimute,potencia_w,ganho_antena,altura_antena_m,data_licenciamento,data_primeiro_licenciamento,data_validade,vencida,anos_licenciada,status,classe_estacao,polarizacao,angulo_elevacao_graus,angulo_meia_potencia_graus,frente_costa_db,cod_tipo_antena,cod_equipamento_antena,cod_equipamento_transmissor,potencia_dbm,azimute_valido,elevacao_valida) VALUES($u,$f,$n,$ato,$tec,$ger,$des,$larg,$tx,$rx,$dup,$azi,$pot,$gan,$alt,$dl,$dp,$dv,$ven,$anos,$status,$classe,$pol,$el,$meia,$fc,$cta,$cea,$cet,$dbm,$av,$ev)";
    foreach (var zipPath in Directory.GetFiles(Root+"/files", "*.zip").OrderBy(x=>x,StringComparer.Ordinal))
    {
        using var zip=ZipFile.OpenRead(zipPath);
        var entry=zip.Entries.FirstOrDefault(x=>x.Length>0) ?? throw new InvalidDataException($"ZIP vazio: {zipPath}");
        using var reader=new StreamReader(entry.Open(),new UTF8Encoding(false,true),true);
        string? headerLine=reader.ReadLine(); if(headerLine==null) continue;
        var headers=Parse(headerLine).Select(x=>x.Trim()).ToArray();
        var ix=headers.Select((v,i)=>(v,i)).ToDictionary(x=>x.v,x=>x.i,StringComparer.Ordinal);
        string? line;
        while((line=reader.ReadLine())!=null)
        {
            var row=Parse(line);
            string? Raw(string name) => ix.TryGetValue(name,out int i)&&i<row.Count ? Clean(row[i]) : null;
            var f=Raw("NumFistel"); var n=Raw("NumEstacao"); var u=Raw("SiglaUf")?.ToUpperInvariant();
            if(f==null||n==null||u==null) continue;
            string? Num(string c)=>Number(Raw(c));
            string? date(string c)=>Date(Raw(c));
            var ent=Normalize(Raw("NomeEntidade")); var addr=Normalize(Raw("EnderecoEstacao"));
            var lat=Num("Latitude"); var lon=Num("Longitude"); var tech=Raw("Tecnologia")?.ToUpperInvariant(); var des=Raw("DesignacaoEmissao")?.ToUpperInvariant();
            string? op=Operator(ent);
            var key=(u,f,n);
            if(stationSeen.Add(key))
            {
                var cv=Coordinate(lat,lon);
                Bind(st,"$f",f,"$n",n,"$u",u,"$cm",Raw("CodMunicipio"),"$m",Raw("Municipio.NomeMunicipio"),"$e",addr,"$lat",lat,"$lon",lon,"$cv",cv,"$ent",ent,"$op",op);
                st.ExecuteNonQuery();
            }
            var ato=Raw("NumAto"); var azi=Num("Azimute"); var txf=Num("FreqTxMHz"); var rxf=Num("FreqRxMHz");
            var dedup=JsonSerializer.Serialize(new[]{u,f,n,ato,azi,txf,rxf,des,tech});
            if(!emissionSeen.Add(dedup)) continue;
            var power=Num("PotenciaTransmissorWatts"); var elev=Num("AnguloElevacao"); var prim=date("DataPrimeiroLicenciamento"); var validity=date("DataValidade");
            var duplex=txf!=null&&rxf!=null?Fmt(ParseNum(txf)!.Value-ParseNum(rxf)!.Value,3):null;
            var dbm=power!=null&&ParseNum(power)>0?Fmt(10*Math.Log10(ParseNum(power)!.Value*1000),1):null;
            var years=prim!=null?Fmt((today.DayNumber-DateOnly.ParseExact(prim,"yyyy-MM-dd").DayNumber)/365.25,1):null;
            Bind(em,"$u",u,"$f",f,"$n",n,"$ato",ato,"$tec",tech,"$ger",Generation(tech),"$des",des,"$larg",Bandwidth(des),"$tx",txf,"$rx",rxf,"$dup",duplex,"$azi",azi,"$pot",power,"$gan",Num("GanhoAntena"),"$alt",Num("AlturaAntena"),"$dl",date("DataLicenciamento"),"$dp",prim,"$dv",validity,"$ven",validity!=null&&DateOnly.ParseExact(validity,"yyyy-MM-dd")<today?1:0,"$anos",years,"$status",Raw("Status.state"),"$classe",Raw("CodTipoClasseEstacao"),"$pol",Raw("Polarizacao")?.ToUpperInvariant(),"$el",elev,"$meia",Num("AnguloMeiaPotenciaAntena"),"$fc",Num("FrenteCostaAntena"),"$cta",Raw("CodTipoAntena"),"$cea",Raw("CodEquipamentoAntena"),"$cet",Raw("CodEquipamentoTransmissor"),"$dbm",dbm,"$av",azi==null?null:ParseNum(azi)>=0&&ParseNum(azi)<=360?1:0,"$ev",elev==null?null:ParseNum(elev)>=-90&&ParseNum(elev)<=90?1:0);
            em.ExecuteNonQuery();
        }
    }
    tx.Commit();
}
catch(Exception ex){ Console.Error.WriteLine(ex.Message); Environment.ExitCode=1; }

static List<string> Parse(string line){var a=new List<string>();var s=new StringBuilder();bool q=false;for(int i=0;i<line.Length;i++){char c=line[i];if(c=='"'){if(q&&i+1<line.Length&&line[i+1]=='"'){s.Append('"');i++;}else q=!q;}else if(c=='|'&&!q){a.Add(s.ToString());s.Clear();}else s.Append(c);}a.Add(s.ToString());return a;}
static string? Clean(string? s){s=s?.Trim();return string.IsNullOrEmpty(s)?null:s;}
static string? Number(string? s){if(s==null)return null;s=s.Replace(',','.');if(double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out var x)&&double.IsFinite(x))return x.ToString("R",CultureInfo.InvariantCulture);return null;}
static double? ParseNum(string? s)=>s==null?null:double.Parse(s,CultureInfo.InvariantCulture);
static string? Date(string? s)=>DateOnly.TryParseExact(s,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)?d.ToString("yyyy-MM-dd"):null;
static string? Normalize(string? s){if(s==null)return null;var d=s.Normalize(NormalizationForm.FormD);var b=new StringBuilder();foreach(char c in d){var cat=CharUnicodeInfo.GetUnicodeCategory(c);if(cat==UnicodeCategory.NonSpacingMark||cat==UnicodeCategory.SpacingCombiningMark||cat==UnicodeCategory.EnclosingMark)continue;b.Append(char.IsLetterOrDigit(c)?char.ToUpperInvariant(c):' ');}var result=Regex.Replace(b.ToString(),@"\s+"," ").Trim();return result.Length==0?null:result;}
static string? Operator(string? s){if(s==null)return "OUTRAS";bool has(string w)=>Regex.IsMatch(s,@"\b"+w+@"\b");if(has("TELEFONICA")||has("VIVO"))return "VIVO";if(has("CLARO")||has("EMBRATEL"))return "CLARO";if(Regex.IsMatch(s,@"^TIM\b"))return "TIM";if(Regex.IsMatch(s,@"^OI\b")||has("TELEMAR")||Regex.IsMatch(s,@"\bBRASIL TELECOM\b"))return "OI";if(has("ALGAR"))return "ALGAR";if(has("SERCOMTEL"))return "SERCOMTEL";if(has("BRISANET"))return "BRISANET";return "OUTRAS";}
static string Generation(string? s)=>s switch{"GSM" or "CDMA"=>"2G","WCDMA" or "WDCMA"=>"3G","LTE"=>"4G","NR"=>"5G",null=>"N/D",_=>"OUTRA"};
static string? Bandwidth(string? s){if(s==null||s.Length<2)return null;var p=s[..Math.Min(4,s.Length)];int i=0;while(i<p.Length&&char.IsDigit(p[i]))i++;if(i==0||i>=p.Length||!"HKMG".Contains(p[i]))return null;var rest=p[(i+1)..];if(rest.Any(c=>!char.IsDigit(c)))return null;var numeric=p[..i]+(rest.Length>0?"."+rest:"");if(!double.TryParse(numeric,NumberStyles.Float,CultureInfo.InvariantCulture,out var v))return null;double factor=p[i] switch{'H'=>.001,'K'=>1,'M'=>1000,'G'=>1000000,_=>0};return Fmt(v*factor,6);}
static string Fmt(double n,int digits)=>Math.Round(n,digits,MidpointRounding.ToEven).ToString("0."+new string('#',digits),CultureInfo.InvariantCulture);
static int Coordinate(string? a,string? b){if(a==null||b==null)return 0;var x=ParseNum(a)!.Value;var y=ParseNum(b)!.Value;return !(x==0&&y==0)&&x>=-33.75&&x<=5.28&&y>=-73.99&&y<=-28.83?1:0;}
static void Bind(SqliteCommand c,params object?[] pairs){c.Parameters.Clear();for(int i=0;i<pairs.Length;i+=2)c.Parameters.AddWithValue((string)pairs[i]!,pairs[i+1]??DBNull.Value);}

static class DbExtensions { public static void ExecuteNonQuery(this SqliteConnection c,string sql){using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.ExecuteNonQuery();} }
