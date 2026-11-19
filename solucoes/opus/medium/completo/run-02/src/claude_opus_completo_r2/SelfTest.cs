using System.Globalization;

namespace AnatelEtl;

/// <summary>Autoteste com os exemplos da especificação e casos de borda (executar com --selftest).</summary>
public static class SelfTest
{
    private const string Header = "Status.state|NomeEntidade|NumFistel|NumServico|NumAto|NumEstacao|EnderecoEstacao|EndComplemento|SiglaUf|CodMunicipio|DesignacaoEmissao|Tecnologia|tipoTecnologia|meioAcesso|FreqTxMHz|FreqRxMHz|Azimute|CodTipoClasseEstacao|ClassInfraFisica|CompartilhamentoInfraFisica|CodTipoAntena|CodEquipamentoAntena|GanhoAntena|FrenteCostaAntena|AnguloMeiaPotenciaAntena|AnguloElevacao|Polarizacao|AlturaAntena|CodEquipamentoTransmissor|PotenciaTransmissorWatts|Latitude|Longitude|CodDebitoTFI|DataLicenciamento|DataPrimeiroLicenciamento|NumRede|_id|DataValidade|NumFistelAssociado|NomeEntidadeAssociado|Municipio.NomeMunicipio";

    private static readonly (string Row, string Est, string Emi)[] Cases =
    {
        ("\"LIC-LIC-01\"|\"TELEFONICA BRASIL S.A.\"|\"50409146285\"|\"010\"|\"48412023\"|\"442820500\"|\"AVENIDA CENTENÁRIO. LOTE 277\"|\"\"|\"RR\"|\"1400100\"|\"5M00G7W\"|\"WCDMA\"|\"\"|\"\"|\"2160\"|\"1970\"|\"120\"|\"FB\"|\"\"|\"NA\"|\"760\"|\"007690501705\"|\"15\"|\"30\"|\"87\"|\"6\"|\"X\"|\"53\"|\"008041101882\"|\"40\"|\"2.79944\"|\"-60.71028\"|\"G\"|\"2023-08-21\"|\"1999-11-23\"|\"\"|\"4d5c019fa88de\"|\"2038-04-30\"|\"\"|\"\"|\"Boa Vista\"",
         "50409146285|442820500|RR|1400100|Boa Vista|AVENIDA CENTENARIO LOTE 277|2.79944|-60.71028|1|TELEFONICA BRASIL S A|VIVO",
         "RR|50409146285|442820500|48412023|WCDMA|3G|5M00G7W|5000|2160|1970|190|120|40|15|53|2023-08-21|1999-11-23|2038-04-30|0|26.9|LIC-LIC-01|FB|X|6|87|30|760|007690501705|008041101882|46|1|1"),
        ("\"LIC-LIC-01\"|\"MINISTÉRIO DA JUSTIÇA - DEPARTAMENTO DE POLICIA ROD. FEDERAL\"|\"50408478667\"|\"023\"|\"19742012\"|\"1003969574\"|\"BR 174  km 600 SN  AMAJARI\"|\"\"|\"RR\"|\"1400027\"|\"25K0D7WDT\"|\"\"|\"\"|\"\"|\"380.65\"|\"390.65\"|\"\"|\"FB\"|\"\"|\"\"|\"86\"|\"016381309110\"|\"8.1\"|\"\"|\"\"|\"\"|\"V\"|\"55.0\"|\"033761101152\"|\"10.000\"|\"3.6021944444443\"|\"-60.961194444444\"|\"A\"|\"2017-05-16\"|\"2017-05-16\"|\"3500\"|\"62db2001630eb\"|\"2032-04-11\"|\"\"|\"\"|\"Amajari\"",
         "50408478667|1003969574|RR|1400027|Amajari|BR 174 KM 600 SN AMAJARI|3.6021944444443|-60.961194444444|1|MINISTERIO DA JUSTICA DEPARTAMENTO DE POLICIA ROD FEDERAL|OUTRAS",
         "RR|50408478667|1003969574|19742012|NULL|N/D|25K0D7WDT|25|380.65|390.65|-10|NULL|10|8.1|55|2017-05-16|2017-05-16|2032-04-11|0|9.4|LIC-LIC-01|FB|V|NULL|NULL|NULL|86|016381309110|033761101152|40|NULL|NULL"),
        ("\"LIC-LIC-01\"|\"CLARO S.A.\"|\"50418766738\"|\"019\"|\"123122021\"|\"1013264735\"|\"R ALBINO TAVARES\"|\"\"|\"RR\"|\"1400027\"|\"56M0D7W\"|\"\"|\"\"|\"\"|\"6905\"|\"6565\"|\"138\"|\"FX\"|\"\"|\"\"|\"655\"|\"030791200324\"|\"38.8\"|\"65\"|\"1.8\"|\"-0.12\"|\"H\"|\"88\"|\"008241102620\"|\"1.2589\"|\"3.65333\"|\"-61.41733\"|\"C\"|\"2022-07-08\"|\"2022-07-08\"|\"508808\"|\"4310d24800bff24c\"|\"2039-11-28\"|\"\"|\"\"|\"Amajari\"",
         "50418766738|1013264735|RR|1400027|Amajari|R ALBINO TAVARES|3.65333|-61.41733|1|CLARO S A|CLARO",
         "RR|50418766738|1013264735|123122021|NULL|N/D|56M0D7W|56000|6905|6565|340|138|1.2589|38.8|88|2022-07-08|2022-07-08|2039-11-28|0|4.3|LIC-LIC-01|FX|H|-0.12|1.8|65|655|030791200324|008241102620|31|1|1"),
        ("\"LIC-LIC-01\"|\"JNET TECNOLOGIA E TELECOMUNICACAO LTDA\"|\"50442919867\"|\"019\"|\"118512022\"|\"1014179294\"|\"Acesso pelo Rio Envirá.\"|\"Região de Floresta\"|\"AC\"|\"1200302\"|\"40M0D7W\"|\"\"|\"\"|\"\"|\"6700\"|\"7040\"|\"205,79\"|\"FX\"|\"\"|\"\"|\"655\"|\"006801702593\"|\"36.7\"|\"57\"|\"3.2\"|\"-0.1\"|\"x\"|\"24\"|\"018871701397\"|\"0.199\"|\"-7.975978\"|\"-70.252103\"|\"C\"|\"2022-09-01\"|\"2022-09-01\"|\"1\"|\"c825c5f80a312db7\"|\"2042-08-10\"|\"\"|\"\"|\"Feijó\"",
         "50442919867|1014179294|AC|1200302|Feijó|ACESSO PELO RIO ENVIRA|-7.975978|-70.252103|1|JNET TECNOLOGIA E TELECOMUNICACAO LTDA|OUTRAS",
         "AC|50442919867|1014179294|118512022|NULL|N/D|40M0D7W|40000|6700|7040|-340|205.79|0.199|36.7|24|2022-09-01|2022-09-01|2042-08-10|0|4.1|LIC-LIC-01|FX|X|-0.1|3.2|57|655|006801702593|018871701397|23|1|1"),
    };

    private static int _fail;

    public static int Run()
    {
        var hdr = Parse(Header);
        var t = new Transformer(hdr);
        foreach (var (rowText, est, emi) in Cases)
        {
            var row = Parse(rowText);
            if (!t.TryGetKey(row, out var uf, out var f, out var n)) { Fail("linha rejeitada"); continue; }
            var e = t.BuildEstacao(row, uf, f, n);
            Compare("estacao", est, new object?[] { e.Fistel, e.NumEstacao, e.Uf, e.CodMunicipio, e.Municipio, e.Endereco, e.Latitude, e.Longitude, e.CoordenadaValida, e.Entidade, e.Operadora });
            var m = t.BuildEmissao(row, uf, f, n);
            Compare("emissao", emi, new object?[] { m.Uf, m.Fistel, m.NumEstacao, m.NumAto, m.Tecnologia, m.Geracao, m.DesignacaoEmissao, m.LarguraBandaKhz, m.FreqTxMhz, m.FreqRxMhz, m.DuplexMhz, m.Azimute, m.PotenciaW, m.GanhoAntena, m.AlturaAntenaM, m.DataLicenciamento, m.DataPrimeiroLicenciamento, m.DataValidade, m.Vencida, m.AnosLicenciada, m.Status, m.ClasseEstacao, m.Polarizacao, m.AnguloElevacaoGraus, m.AnguloMeiaPotenciaGraus, m.FrenteCostaDb, m.CodTipoAntena, m.CodEquipamentoAntena, m.CodEquipamentoTransmissor, m.PotenciaDbm, m.AzimuteValido, m.ElevacaoValida });
        }

        // Números
        Eq("num 18.000", Cleaning.Number("18.000"), 18.0);
        Eq("num ,5", Cleaning.Number(" -,5 "), -0.5);
        Eq("num e", Cleaning.Number("-1.5e-3"), -0.0015);
        foreach (var bad in new[] { "NA", "nan", "NaN", "inf", "-Infinity", "1,5.3", "10 W", "2023-01-01", "--1", "- 1", "", "  ", ",", "1.2.", "1e400" })
            Eq($"num '{bad}'", Cleaning.Number(bad), null);
        // Datas
        Eq("data ok", Cleaning.Date(" 2023-08-21 ")?.ToString("yyyy-MM-dd"), "2023-08-21");
        foreach (var bad in new[] { "2023-02-30", "2023-08-21 00:00:00.0", "21/08/2023", "NA", "-", "2023-8-21" })
            Eq($"data '{bad}'", Cleaning.Date(bad)?.ToString(), null);
        // Normalização
        Eq("norm", Cleaning.Normalize("Telefônica  Brasil S.A."), "TELEFONICA BRASIL S A");
        Eq("norm vazio", Cleaning.Normalize(" .- "), null);
        // Designação
        Eq("bw 200K", Derivations.LarguraBandaKhz("200KG7W"), 200.0);
        Eq("bw 2M50", Derivations.LarguraBandaKhz("2M50F9W"), 2500.0);
        Eq("bw 10M0", Derivations.LarguraBandaKhz("10M0G7W"), 10000.0);
        Eq("bw 1G60", Derivations.LarguraBandaKhz("1G60D7W"), 1600000.0);
        Eq("bw 100H", Derivations.LarguraBandaKhz("100HA1A"), 0.1);
        foreach (var bad in new[] { "16KF3E", "M500", "5MO0", "5M0K", "1234", "", "ABCD" })
            Eq($"bw '{bad}'", Derivations.LarguraBandaKhz(bad), null);
        // Arredondamento
        Eq("round 2.25", Derivations.Round(2.25, 1), 2.3);
        Eq("round -2.25", Derivations.Round(-2.25, 1), -2.3);
        Eq("round 0.15", Derivations.Round(0.15, 1), 0.2);
        // Operadora
        Eq("op tim", Derivations.Operadora("TIM S A"), "TIM");
        Eq("op estimativa", Derivations.Operadora("ESTIMATIVA TIM"), "OUTRAS");
        Eq("op oi", Derivations.Operadora("OI S A EM RECUPERACAO JUDICIAL"), "OI");
        Eq("op bt", Derivations.Operadora("XYZ BRASIL TELECOM SA"), "OI");
        Eq("op telemar", Derivations.Operadora("TELEMAR NORTE LESTE"), "OI");
        Eq("op claro", Derivations.Operadora("EMBRATEL TVSAT"), "CLARO");
        Eq("op vivo>claro", Derivations.Operadora("VIVO CLARO"), "VIVO");
        Eq("op null", Derivations.Operadora(null), "OUTRAS");
        // Geração
        Eq("ger", Derivations.Geracao("wdcma"), "3G");
        Eq("ger outra", Derivations.Geracao("LTE (TDD)"), "OUTRA");
        // Coordenada
        Eq("coord 0,0", Derivations.CoordenadaValida(0, 0), false);
        Eq("coord borda", Derivations.CoordenadaValida(-33.75, -28.83), true);
        // CSV com aspas internas
        var r = Parse("\"a\"\"b\"|\"\"|\"c|d\"");
        Eq("csv", string.Join("¦", r), "a\"b¦¦c|d");

        Console.WriteLine(_fail == 0 ? "SELFTEST OK" : $"SELFTEST FALHOU: {_fail}");
        return _fail == 0 ? 0 : 1;
    }

    private static string[] Parse(string line) => new CsvReader(new StringReader(line), '|').ReadRecord()!;

    private static void Compare(string what, string expected, object?[] actual)
    {
        var exp = expected.Split('|');
        if (exp.Length != actual.Length) { Fail($"{what}: nº de colunas {exp.Length} vs {actual.Length}"); return; }
        for (int i = 0; i < exp.Length; i++)
        {
            var a = actual[i];
            string? ex = exp[i] == "NULL" ? null : exp[i];
            bool ok = a switch
            {
                null => ex == null,
                double d => ex != null && d == double.Parse(ex, CultureInfo.InvariantCulture),
                bool b => ex == (b ? "1" : "0"),
                DateOnly dt => ex == dt.ToString("yyyy-MM-dd"),
                _ => ex == a.ToString(),
            };
            if (!ok) Fail($"{what}[{i}]: esperado '{ex ?? "NULL"}', obtido '{a ?? "NULL"}'");
        }
    }

    private static void Eq<T>(string what, T actual, T expected)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected)) Fail($"{what}: esperado '{expected}', obtido '{actual}'");
    }

    private static void Fail(string msg) { _fail++; Console.WriteLine("FALHA " + msg); }
}
