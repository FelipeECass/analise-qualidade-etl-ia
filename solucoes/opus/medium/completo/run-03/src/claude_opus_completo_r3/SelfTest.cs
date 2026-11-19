using System.Globalization;

namespace Etl;

/// <summary>Verificação dos exemplos do enunciado e de casos de borda (executar com --selftest).</summary>
public static class SelfTest
{
    private const string Header = "Status.state|NomeEntidade|NumFistel|NumServico|NumAto|NumEstacao|EnderecoEstacao|EndComplemento|SiglaUf|CodMunicipio|DesignacaoEmissao|Tecnologia|tipoTecnologia|meioAcesso|FreqTxMHz|FreqRxMHz|Azimute|CodTipoClasseEstacao|ClassInfraFisica|CompartilhamentoInfraFisica|CodTipoAntena|CodEquipamentoAntena|GanhoAntena|FrenteCostaAntena|AnguloMeiaPotenciaAntena|AnguloElevacao|Polarizacao|AlturaAntena|CodEquipamentoTransmissor|PotenciaTransmissorWatts|Latitude|Longitude|CodDebitoTFI|DataLicenciamento|DataPrimeiroLicenciamento|NumRede|_id|DataValidade|NumFistelAssociado|NomeEntidadeAssociado|Municipio.NomeMunicipio";

    private static readonly (string Line, string Est, string Emi)[] Cases =
    {
        ("\"LIC-LIC-01\"|\"TELEFONICA BRASIL S.A.\"|\"50409146285\"|\"010\"|\"48412023\"|\"442820500\"|\"AVENIDA CENTENÁRIO. LOTE 277\"|\"\"|\"RR\"|\"1400100\"|\"5M00G7W\"|\"WCDMA\"|\"\"|\"\"|\"2160\"|\"1970\"|\"120\"|\"FB\"|\"\"|\"NA\"|\"760\"|\"007690501705\"|\"15\"|\"30\"|\"87\"|\"6\"|\"X\"|\"53\"|\"008041101882\"|\"40\"|\"2.79944\"|\"-60.71028\"|\"G\"|\"2023-08-21\"|\"1999-11-23\"|\"\"|\"4d5c019fa88de\"|\"2038-04-30\"|\"\"|\"\"|\"Boa Vista\"",
         "fistel=50409146285;num_estacao=442820500;uf=RR;cod_municipio=1400100;municipio=Boa Vista;endereco=AVENIDA CENTENARIO LOTE 277;latitude=2.79944;longitude=-60.71028;coordenada_valida=1;entidade=TELEFONICA BRASIL S A;operadora=VIVO",
         "uf=RR;fistel=50409146285;num_estacao=442820500;num_ato=48412023;tecnologia=WCDMA;geracao=3G;designacao_emissao=5M00G7W;largura_banda_khz=5000.0;freq_tx_mhz=2160.0;freq_rx_mhz=1970.0;duplex_mhz=190.0;azimute=120.0;potencia_w=40.0;ganho_antena=15.0;altura_antena_m=53.0;data_licenciamento=2023-08-21;data_primeiro_licenciamento=1999-11-23;data_validade=2038-04-30;vencida=0;anos_licenciada=26.9;status=LIC-LIC-01;classe_estacao=FB;polarizacao=X;angulo_elevacao_graus=6.0;angulo_meia_potencia_graus=87.0;frente_costa_db=30.0;cod_tipo_antena=760;cod_equipamento_antena=007690501705;cod_equipamento_transmissor=008041101882;potencia_dbm=46.0;azimute_valido=1;elevacao_valida=1"),
        ("\"LIC-LIC-01\"|\"MINISTÉRIO DA JUSTIÇA - DEPARTAMENTO DE POLICIA ROD. FEDERAL\"|\"50408478667\"|\"023\"|\"19742012\"|\"1003969574\"|\"BR 174  km 600 SN  AMAJARI\"|\"\"|\"RR\"|\"1400027\"|\"25K0D7WDT\"|\"\"|\"\"|\"\"|\"380.65\"|\"390.65\"|\"\"|\"FB\"|\"\"|\"\"|\"86\"|\"016381309110\"|\"8.1\"|\"\"|\"\"|\"\"|\"V\"|\"55.0\"|\"033761101152\"|\"10.000\"|\"3.6021944444443\"|\"-60.961194444444\"|\"A\"|\"2017-05-16\"|\"2017-05-16\"|\"3500\"|\"62db2001630eb\"|\"2032-04-11\"|\"\"|\"\"|\"Amajari\"",
         "fistel=50408478667;num_estacao=1003969574;uf=RR;cod_municipio=1400027;municipio=Amajari;endereco=BR 174 KM 600 SN AMAJARI;latitude=3.6021944444443;longitude=-60.961194444444;coordenada_valida=1;entidade=MINISTERIO DA JUSTICA DEPARTAMENTO DE POLICIA ROD FEDERAL;operadora=OUTRAS",
         "uf=RR;fistel=50408478667;num_estacao=1003969574;num_ato=19742012;tecnologia=NULL;geracao=N/D;designacao_emissao=25K0D7WDT;largura_banda_khz=25.0;freq_tx_mhz=380.65;freq_rx_mhz=390.65;duplex_mhz=-10.0;azimute=NULL;potencia_w=10.0;ganho_antena=8.1;altura_antena_m=55.0;data_licenciamento=2017-05-16;data_primeiro_licenciamento=2017-05-16;data_validade=2032-04-11;vencida=0;anos_licenciada=9.4;status=LIC-LIC-01;classe_estacao=FB;polarizacao=V;angulo_elevacao_graus=NULL;angulo_meia_potencia_graus=NULL;frente_costa_db=NULL;cod_tipo_antena=86;cod_equipamento_antena=016381309110;cod_equipamento_transmissor=033761101152;potencia_dbm=40.0;azimute_valido=NULL;elevacao_valida=NULL"),
        ("\"LIC-LIC-01\"|\"CLARO S.A.\"|\"50418766738\"|\"019\"|\"123122021\"|\"1013264735\"|\"R ALBINO TAVARES\"|\"\"|\"RR\"|\"1400027\"|\"56M0D7W\"|\"\"|\"\"|\"\"|\"6905\"|\"6565\"|\"138\"|\"FX\"|\"\"|\"\"|\"655\"|\"030791200324\"|\"38.8\"|\"65\"|\"1.8\"|\"-0.12\"|\"H\"|\"88\"|\"008241102620\"|\"1.2589\"|\"3.65333\"|\"-61.41733\"|\"C\"|\"2022-07-08\"|\"2022-07-08\"|\"508808\"|\"4310d24800bff24c\"|\"2039-11-28\"|\"\"|\"\"|\"Amajari\"",
         "fistel=50418766738;num_estacao=1013264735;uf=RR;cod_municipio=1400027;municipio=Amajari;endereco=R ALBINO TAVARES;latitude=3.65333;longitude=-61.41733;coordenada_valida=1;entidade=CLARO S A;operadora=CLARO",
         "uf=RR;fistel=50418766738;num_estacao=1013264735;num_ato=123122021;tecnologia=NULL;geracao=N/D;designacao_emissao=56M0D7W;largura_banda_khz=56000.0;freq_tx_mhz=6905.0;freq_rx_mhz=6565.0;duplex_mhz=340.0;azimute=138.0;potencia_w=1.2589;ganho_antena=38.8;altura_antena_m=88.0;data_licenciamento=2022-07-08;data_primeiro_licenciamento=2022-07-08;data_validade=2039-11-28;vencida=0;anos_licenciada=4.3;status=LIC-LIC-01;classe_estacao=FX;polarizacao=H;angulo_elevacao_graus=-0.12;angulo_meia_potencia_graus=1.8;frente_costa_db=65.0;cod_tipo_antena=655;cod_equipamento_antena=030791200324;cod_equipamento_transmissor=008241102620;potencia_dbm=31.0;azimute_valido=1;elevacao_valida=1"),
        ("\"LIC-LIC-01\"|\"JNET TECNOLOGIA E TELECOMUNICACAO LTDA\"|\"50442919867\"|\"019\"|\"118512022\"|\"1014179294\"|\"Acesso pelo Rio Envirá.\"|\"Região de Floresta\"|\"AC\"|\"1200302\"|\"40M0D7W\"|\"\"|\"\"|\"\"|\"6700\"|\"7040\"|\"205,79\"|\"FX\"|\"\"|\"\"|\"655\"|\"006801702593\"|\"36.7\"|\"57\"|\"3.2\"|\"-0.1\"|\"x\"|\"24\"|\"018871701397\"|\"0.199\"|\"-7.975978\"|\"-70.252103\"|\"C\"|\"2022-09-01\"|\"2022-09-01\"|\"1\"|\"c825c5f80a312db7\"|\"2042-08-10\"|\"\"|\"\"|\"Feijó\"",
         "fistel=50442919867;num_estacao=1014179294;uf=AC;cod_municipio=1200302;municipio=Feijó;endereco=ACESSO PELO RIO ENVIRA;latitude=-7.975978;longitude=-70.252103;coordenada_valida=1;entidade=JNET TECNOLOGIA E TELECOMUNICACAO LTDA;operadora=OUTRAS",
         "uf=AC;fistel=50442919867;num_estacao=1014179294;num_ato=118512022;tecnologia=NULL;geracao=N/D;designacao_emissao=40M0D7W;largura_banda_khz=40000.0;freq_tx_mhz=6700.0;freq_rx_mhz=7040.0;duplex_mhz=-340.0;azimute=205.79;potencia_w=0.199;ganho_antena=36.7;altura_antena_m=24.0;data_licenciamento=2022-09-01;data_primeiro_licenciamento=2022-09-01;data_validade=2042-08-10;vencida=0;anos_licenciada=4.1;status=LIC-LIC-01;classe_estacao=FX;polarizacao=X;angulo_elevacao_graus=-0.1;angulo_meia_potencia_graus=3.2;frente_costa_db=57.0;cod_tipo_antena=655;cod_equipamento_antena=006801702593;cod_equipamento_transmissor=018871701397;potencia_dbm=23.0;azimute_valido=1;elevacao_valida=1"),
    };

    private static int _fail;

    public static int Run()
    {
        var header = Parse(Header);
        var tr = new Transformer(header);
        foreach (var (line, est, emi) in Cases)
        {
            var row = Parse(line);
            var k = tr.StationKey(row) ?? throw new Exception("rejeitada");
            var e = tr.BuildEstacao(row, k);
            Compare("estacao", est, new Dictionary<string, object?>
            {
                ["fistel"] = e.Fistel, ["num_estacao"] = e.NumEstacao, ["uf"] = e.Uf,
                ["cod_municipio"] = e.CodMunicipio, ["municipio"] = e.Municipio, ["endereco"] = e.Endereco,
                ["latitude"] = e.Latitude, ["longitude"] = e.Longitude, ["coordenada_valida"] = e.CoordenadaValida,
                ["entidade"] = e.Entidade, ["operadora"] = e.Operadora,
            });
            var m = tr.BuildEmissao(row, k);
            Compare("emissao", emi, new Dictionary<string, object?>
            {
                ["uf"] = m.Uf, ["fistel"] = m.Fistel, ["num_estacao"] = m.NumEstacao, ["num_ato"] = m.NumAto,
                ["tecnologia"] = m.Tecnologia, ["geracao"] = m.Geracao, ["designacao_emissao"] = m.DesignacaoEmissao,
                ["largura_banda_khz"] = m.LarguraBandaKhz, ["freq_tx_mhz"] = m.FreqTxMhz, ["freq_rx_mhz"] = m.FreqRxMhz,
                ["duplex_mhz"] = m.DuplexMhz, ["azimute"] = m.Azimute, ["potencia_w"] = m.PotenciaW,
                ["ganho_antena"] = m.GanhoAntena, ["altura_antena_m"] = m.AlturaAntenaM,
                ["data_licenciamento"] = m.DataLicenciamento, ["data_primeiro_licenciamento"] = m.DataPrimeiroLicenciamento,
                ["data_validade"] = m.DataValidade, ["vencida"] = m.Vencida, ["anos_licenciada"] = m.AnosLicenciada,
                ["status"] = m.Status, ["classe_estacao"] = m.ClasseEstacao, ["polarizacao"] = m.Polarizacao,
                ["angulo_elevacao_graus"] = m.AnguloElevacaoGraus, ["angulo_meia_potencia_graus"] = m.AnguloMeiaPotenciaGraus,
                ["frente_costa_db"] = m.FrenteCostaDb, ["cod_tipo_antena"] = m.CodTipoAntena,
                ["cod_equipamento_antena"] = m.CodEquipamentoAntena, ["cod_equipamento_transmissor"] = m.CodEquipamentoTransmissor,
                ["potencia_dbm"] = m.PotenciaDbm, ["azimute_valido"] = m.AzimuteValido, ["elevacao_valida"] = m.ElevacaoValida,
            });
        }

        // Casos de borda
        Eq("num 18.000", Cleaning.Number("18.000"), 18.0);
        Eq("num 55,0", Cleaning.Number(" 55,0 "), 55.0);
        Eq("num .5", Cleaning.Number(".5"), 0.5);
        Eq("num -.5", Cleaning.Number("-.5"), -0.5);
        Eq("num sci", Cleaning.Number("-5.6e-05"), -5.6e-05);
        foreach (var bad in new[] { "NA", "NaN", "nan", "NAN", "Infinity", "-inf", "#N/A", "-", ",", "14,70.0", "1900-03-04", "45 W", "--9.9", "1.7/2.2", "", "  ", "1e400" })
            Eq("num invalido " + bad, Cleaning.Number(bad), null);
        Eq("data ok", Cleaning.Date("2023-08-21"), new DateOnly(2023, 8, 21));
        foreach (var bad in new[] { "2019-09-20 12:18:58.810", "-", "NA", "2023-02-30", "2023-1-05", "21/08/2023" })
            Eq("data invalida " + bad, Cleaning.Date(bad), null);
        Eq("norm", Cleaning.Normalize("Telefônica  Brasil S.A."), "TELEFONICA BRASIL S A");
        Eq("norm vazio", Cleaning.Normalize(" .-. "), null);
        Eq("bw 200KG7W", Derivations.LarguraBandaKhz("200KG7W"), 200.0);
        Eq("bw 2M50F9W", Derivations.LarguraBandaKhz("2M50F9W"), 2500.0);
        Eq("bw 10M0G7W", Derivations.LarguraBandaKhz("10M0G7W"), 10000.0);
        Eq("bw 100H", Derivations.LarguraBandaKhz("100HA1A"), 0.1);
        Eq("bw 1G60", Derivations.LarguraBandaKhz("1G60D7W"), 1600000.0);
        foreach (var bad in new[] { "11KF3D", "7M0D7W", "40MOD7W", "40M", "TORRE RTK", "M100", "5M0G7W" })
            Eq("bw invalido " + bad, Derivations.LarguraBandaKhz(bad), null);
        Eq("op TIM", Derivations.Operadora("TIM S A"), "TIM");
        Eq("op ESTIMATIVA", Derivations.Operadora("ESTIMATIVA TIM"), "OUTRAS");
        Eq("op OI", Derivations.Operadora("OI S A EM RECUPERACAO JUDICIAL"), "OI");
        Eq("op OIAPOQUE", Derivations.Operadora("CONDOMINIO DO SHOPPING OIAPOQUE"), "OUTRAS");
        Eq("op RIO CLARO", Derivations.Operadora("MUNICIPIO DE RIO CLARO"), "CLARO");
        Eq("op BT", Derivations.Operadora("COMPUNET DO BRASIL TELECOM LTDA"), "OI");
        Eq("op null", Derivations.Operadora(null), "OUTRAS");
        Eq("op ALGAR", Derivations.Operadora("ALGAR TELECOM S A"), "ALGAR");
        Eq("ger wdcma", Derivations.Geracao("wdcma"), "3G");
        Eq("ger LTE (TDD)", Derivations.Geracao("LTE (TDD)"), "OUTRA");
        Eq("round -2.25", Derivations.Round(-2.25, 1), -2.3);
        Eq("round 2.25", Derivations.Round(2.25, 1), 2.3);
        Eq("az 360", Derivations.AzimuteValido(360), true);
        Eq("az 361", Derivations.AzimuteValido(361), false);
        Eq("el -91", Derivations.ElevacaoValida(-91), false);
        Eq("coord 0,0", Derivations.CoordenadaValida(0, 0), false);
        Eq("coord borda", Derivations.CoordenadaValida(-33.75, -28.83), true);
        Eq("dbm 0", Derivations.PotenciaDbm(0), null);
        Eq("vencida", Derivations.Vencida(new DateOnly(2026, 10, 14)), true);
        Eq("nao vencida hoje", Derivations.Vencida(new DateOnly(2026, 10, 15)), false);
        var csv = new CsvReader(new StringReader("\"a|b\"|\"x\"\"y\"|\"\"\r\n"));
        var r = csv.Read()!;
        Eq("csv", string.Join("¦", r), "a|b¦x\"y¦");

        Console.WriteLine(_fail == 0 ? "SELFTEST OK" : $"SELFTEST FALHOU: {_fail}");
        return _fail == 0 ? 0 : 1;
    }

    private static string[] Parse(string line) => new CsvReader(new StringReader(line)).Read()!;

    private static void Compare(string table, string expected, Dictionary<string, object?> actual)
    {
        var exp = expected.Split(';').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        if (exp.Count != actual.Count) { _fail++; Console.WriteLine($"{table}: nº de colunas difere"); }
        foreach (var (col, ev) in exp)
        {
            var av = actual[col];
            bool ok = av switch
            {
                null => ev == "NULL",
                double d => ev != "NULL" && double.Parse(ev, CultureInfo.InvariantCulture) == d,
                bool b => ev == (b ? "1" : "0"),
                DateOnly dt => ev == dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => ev == av.ToString(),
            };
            if (!ok) { _fail++; Console.WriteLine($"{table}.{col}: esperado {ev}, obtido {av ?? "NULL"}"); }
        }
    }

    private static void Eq<T>(string name, T actual, T expected)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            _fail++;
            Console.WriteLine($"{name}: esperado {expected?.ToString() ?? "NULL"}, obtido {actual?.ToString() ?? "NULL"}");
        }
    }
}
