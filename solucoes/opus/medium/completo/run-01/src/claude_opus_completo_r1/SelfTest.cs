using System.Globalization;

namespace AnatelEtl;

/// <summary>Autoteste com os exemplos da especificação (executar com --selftest).</summary>
public static class SelfTest
{
    private const string Header = "Status.state|NomeEntidade|NumFistel|NumServico|NumAto|NumEstacao|EnderecoEstacao|EndComplemento|SiglaUf|CodMunicipio|DesignacaoEmissao|Tecnologia|tipoTecnologia|meioAcesso|FreqTxMHz|FreqRxMHz|Azimute|CodTipoClasseEstacao|ClassInfraFisica|CompartilhamentoInfraFisica|CodTipoAntena|CodEquipamentoAntena|GanhoAntena|FrenteCostaAntena|AnguloMeiaPotenciaAntena|AnguloElevacao|Polarizacao|AlturaAntena|CodEquipamentoTransmissor|PotenciaTransmissorWatts|Latitude|Longitude|CodDebitoTFI|DataLicenciamento|DataPrimeiroLicenciamento|NumRede|_id|DataValidade|NumFistelAssociado|NomeEntidadeAssociado|Municipio.NomeMunicipio";

    private static readonly (string Line, string Est, string Emi)[] Cases =
    {
        ("\"LIC-LIC-01\"|\"TELEFONICA BRASIL S.A.\"|\"50409146285\"|\"010\"|\"48412023\"|\"442820500\"|\"AVENIDA CENTENÁRIO. LOTE 277\"|\"\"|\"RR\"|\"1400100\"|\"5M00G7W\"|\"WCDMA\"|\"\"|\"\"|\"2160\"|\"1970\"|\"120\"|\"FB\"|\"\"|\"NA\"|\"760\"|\"007690501705\"|\"15\"|\"30\"|\"87\"|\"6\"|\"X\"|\"53\"|\"008041101882\"|\"40\"|\"2.79944\"|\"-60.71028\"|\"G\"|\"2023-08-21\"|\"1999-11-23\"|\"\"|\"4d5c019fa88de\"|\"2038-04-30\"|\"\"|\"\"|\"Boa Vista\"",
         "50409146285|442820500|RR|1400100|Boa Vista|AVENIDA CENTENARIO LOTE 277|2.79944|-60.71028|1|TELEFONICA BRASIL S A|VIVO",
         "RR|50409146285|442820500|48412023|WCDMA|3G|5M00G7W|5000|2160|1970|190|120|40|15|53|2023-08-21|1999-11-23|2038-04-30|0|26.9|LIC-LIC-01|FB|X|6|87|30|760|007690501705|008041101882|46|1|1"),
        ("\"LIC-LIC-01\"|\"MINISTÉRIO DA JUSTIÇA - DEPARTAMENTO DE POLICIA ROD. FEDERAL\"|\"50408478667\"|\"023\"|\"19742012\"|\"1003969574\"|\"BR 174  km 600 SN  AMAJARI\"|\"\"|\"RR\"|\"1400027\"|\"25K0D7WDT\"|\"\"|\"\"|\"\"|\"380.65\"|\"390.65\"|\"\"|\"FB\"|\"\"|\"\"|\"86\"|\"016381309110\"|\"8.1\"|\"\"|\"\"|\"\"|\"V\"|\"55.0\"|\"033761101152\"|\"10.000\"|\"3.6021944444443\"|\"-60.961194444444\"|\"A\"|\"2017-05-16\"|\"2017-05-16\"|\"3500\"|\"62db2001630eb\"|\"2032-04-11\"|\"\"|\"\"|\"Amajari\"",
         "50408478667|1003969574|RR|1400027|Amajari|BR 174 KM 600 SN AMAJARI|3.6021944444443|-60.961194444444|1|MINISTERIO DA JUSTICA DEPARTAMENTO DE POLICIA ROD FEDERAL|OUTRAS",
         "RR|50408478667|1003969574|19742012|NULL|N/D|25K0D7WDT|25|380.65|390.65|-10|NULL|10|8.1|55|2017-05-16|2017-05-16|2032-04-11|0|9.4|LIC-LIC-01|FB|V|NULL|NULL|NULL|86|016381309110|033761101152|40|NULL|NULL"),
        ("\"LIC-LIC-01\"|\"CLARO S.A.\"|\"50418766738\"|\"019\"|\"123122021\"|\"1013264735\"|\"R ALBINO TAVARES\"|\"\"|\"RR\"|\"1400027\"|\"56M0D7W\"|\"\"|\"\"|\"\"|\"6845\"|\"6505\"|\"138\"|\"FX\"|\"\"|\"\"|\"655\"|\"030791200324\"|\"38.8\"|\"65\"|\"1.8\"|\"-0.12\"|\"V\"|\"88\"|\"008241102620\"|\"1.2589\"|\"3.65333\"|\"-61.41733\"|\"C\"|\"2022-07-08\"|\"2022-07-08\"|\"508808\"|\"4310d24800bff24a\"|\"2039-11-28\"|\"\"|\"\"|\"Amajari\"",
         "50418766738|1013264735|RR|1400027|Amajari|R ALBINO TAVARES|3.65333|-61.41733|1|CLARO S A|CLARO",
         "RR|50418766738|1013264735|123122021|NULL|N/D|56M0D7W|56000|6845|6505|340|138|1.2589|38.8|88|2022-07-08|2022-07-08|2039-11-28|0|4.3|LIC-LIC-01|FX|V|-0.12|1.8|65|655|030791200324|008241102620|31|1|1"),
        ("\"LIC-LIC-01\"|\"JNET TECNOLOGIA E TELECOMUNICACAO LTDA\"|\"50442919867\"|\"019\"|\"118512022\"|\"1014179294\"|\"Acesso pelo Rio Envirá.\"|\"Região de Floresta\"|\"AC\"|\"1200302\"|\"40M0D7W\"|\"\"|\"\"|\"\"|\"6700\"|\"7040\"|\"205,79\"|\"FX\"|\"\"|\"\"|\"655\"|\"006801702593\"|\"36.7\"|\"57\"|\"3.2\"|\"-0.1\"|\"x\"|\"24\"|\"018871701397\"|\"0.199\"|\"-7.975978\"|\"-70.252103\"|\"C\"|\"2022-09-01\"|\"2022-09-01\"|\"1\"|\"c825c5f80a312db7\"|\"2042-08-10\"|\"\"|\"\"|\"Feijó\"",
         "50442919867|1014179294|AC|1200302|Feijó|ACESSO PELO RIO ENVIRA|-7.975978|-70.252103|1|JNET TECNOLOGIA E TELECOMUNICACAO LTDA|OUTRAS",
         "AC|50442919867|1014179294|118512022|NULL|N/D|40M0D7W|40000|6700|7040|-340|205.79|0.199|36.7|24|2022-09-01|2022-09-01|2042-08-10|0|4.1|LIC-LIC-01|FX|X|-0.1|3.2|57|655|006801702593|018871701397|23|1|1"),
    };

    private static string F(object? v) => v switch
    {
        null => "NULL",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        bool b => b ? "1" : "0",
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => v.ToString()!,
    };

    public static int Run()
    {
        int failures = 0;
        var fields = new List<string>();
        var csv = new CsvReader(new StringReader(Header + "\r\n" + string.Join("\r\n", Cases.Select(c => c.Line)) + "\r\n"), '|');
        csv.ReadRecord(fields);
        var t = new Transformer(fields.ToList());
        foreach (var c in Cases)
        {
            csv.ReadRecord(fields);
            var key = t.Key(fields)!.Value;
            var e = t.BuildEstacao(fields, key);
            var m = t.BuildEmissao(fields, key);
            var est = string.Join("|", new object?[] { e.Fistel, e.NumEstacao, e.Uf, e.CodMunicipio, e.Municipio, e.Endereco, e.Latitude, e.Longitude, e.CoordenadaValida, e.Entidade, e.Operadora }.Select(F));
            var emi = string.Join("|", new object?[] { m.Uf, m.Fistel, m.NumEstacao, m.NumAto, m.Tecnologia, m.Geracao, m.DesignacaoEmissao, m.LarguraBandaKhz, m.FreqTxMhz, m.FreqRxMhz, m.DuplexMhz, m.Azimute, m.PotenciaW, m.GanhoAntena, m.AlturaAntenaM, m.DataLicenciamento, m.DataPrimeiroLicenciamento, m.DataValidade, m.Vencida, m.AnosLicenciada, m.Status, m.ClasseEstacao, m.Polarizacao, m.AnguloElevacaoGraus, m.AnguloMeiaPotenciaGraus, m.FrenteCostaDb, m.CodTipoAntena, m.CodEquipamentoAntena, m.CodEquipamentoTransmissor, m.PotenciaDbm, m.AzimuteValido, m.ElevacaoValida }.Select(F));
            failures += Check("estacao " + key, c.Est, est) + Check("emissao " + key, c.Emi, emi);
        }

        void Eq(string name, object? expected, object? actual) => failures += Check(name, F(expected), F(actual));
        Eq("num 18.000", 18.0, Cleaning.Number("18.000"));
        Eq("num 55,0", 55.0, Cleaning.Number(" 55,0 "));
        Eq("num .5", 0.5, Cleaning.Number(".5"));
        Eq("num -.5", -0.5, Cleaning.Number("-.5"));
        Eq("num NaN", null, Cleaning.Number("NaN"));
        Eq("num Infinity", null, Cleaning.Number("Infinity"));
        Eq("num 1e999", null, Cleaning.Number("1e999"));
        Eq("num 1,2.3", null, Cleaning.Number("1,2.3"));
        Eq("num 10 W", null, Cleaning.Number("10 W"));
        Eq("num NA", null, Cleaning.Number("NA"));
        Eq("date hora", null, Cleaning.Date("2031-09-08 00:00:00.000"));
        Eq("date inválida", null, Cleaning.Date("2023-02-30"));
        Eq("norm", "TELEFONICA BRASIL S A", Cleaning.Normalize("Telefônica  Brasil S.A."));
        Eq("norm vazio", null, Cleaning.Normalize(" - . "));
        Eq("bw 10M0", 10000.0, Derivations.LarguraBandaKhz("10M0G7W"));
        Eq("bw 200K", 200.0, Derivations.LarguraBandaKhz("200KG7W"));
        Eq("bw 2M50", 2500.0, Derivations.LarguraBandaKhz("2M50F9W"));
        Eq("bw 100H", 0.1, Derivations.LarguraBandaKhz("100HA3E"));
        Eq("bw M00", null, Derivations.LarguraBandaKhz("M00G7W"));
        Eq("bw 12KF", null, Derivations.LarguraBandaKhz("12KF3E"));
        Eq("round 2.25", 2.3, Derivations.Round(2.25, 1));
        Eq("round -2.25", -2.3, Derivations.Round(-2.25, 1));
        Eq("op TIM", "TIM", Derivations.Operadora("TIM S A"));
        Eq("op ESTIMATIVA", "OUTRAS", Derivations.Operadora("ESTIMATIVA TIM"));
        Eq("op OI", "OI", Derivations.Operadora("OI MOVEL S A"));
        Eq("op BRT", "OI", Derivations.Operadora("14 BRASIL TELECOM CELULAR S A"));
        Eq("op NULL", "OUTRAS", Derivations.Operadora(null));
        Eq("ger wdcma", "3G", Derivations.Geracao("wdcma"));
        Eq("ger 5G", "OUTRA", Derivations.Geracao("5G"));

        Console.WriteLine(failures == 0 ? "SELFTEST OK" : $"SELFTEST: {failures} falha(s)");
        return failures == 0 ? 0 : 1;
    }

    private static int Check(string name, string expected, string actual)
    {
        if (expected == actual) return 0;
        Console.WriteLine($"FALHA {name}\n  esperado: {expected}\n  obtido:   {actual}");
        return 1;
    }
}
