"""Junta o JSON da ultima iteracao de cada geracao (Comparador, contra o base.db original)
com a reavaliacao do duplex_mhz (reavaliacao_duplex.tsv) e gera corretude_final.csv
e divergencias_coluna.csv com os valores contra o gabarito corrigido."""
import json, glob, os, re, csv, collections
R = os.path.expanduser("~/mnt/TCC/resultados")
re_av = {l.split("\t")[0].split("/db/")[0]: list(map(int, l.split("\t")[1:6])) for l in open("reavaliacao_duplex.tsv")}
rows, colrows, shas = [], [], set()
for modelo in ["haiku","sonnet","opus","luna","terra","sol"]:
    for cond in ["completo","zero_shot"]:
        for run in [1,2,3]:
            d = f"{R}/{modelo}/medium/{cond}/run-0{run}"
            js = sorted(glob.glob(f"{d}/*/*_i*.json"), key=lambda p: int(re.search(r"_i(\d+)\.json$", p).group(1)))
            iters = [json.load(open(p, encoding="utf-8-sig")) for p in js]
            ult = iters[-1]; res = ult["resultado"]; shas.add(res["gabaritoSha256"])
            nome = f"{modelo}/medium/{cond}/run-0{run}"
            tot, ok_old, ok_new, dok_old, dok_new = re_av[nome]
            lg, exc = res["linhasGabarito"], res["excedentes"]
            ac_new = res["acertos"] + (ok_new - ok_old)
            den = lg + exc
            primeira_aceita = next(i+1 for i,j in enumerate(iters) if j["resultado"]["aceita"])
            rows.append(dict(modelo=modelo, condicao=cond, rodada=run, iteracoes=len(iters), iteracao_aceita=primeira_aceita,
                taxa_original=res["taxa"], divergentes_original=res["divergentes"],
                taxa_corrigida=ac_new/den, divergentes_corrigido=res["divergentes"] - (ok_new - ok_old),
                ausentes=res["ausentes"], excedentes=exc, aceita=ac_new/den >= 0.98,
                duplex_padrao=("decimal exato" if dok_new==tot else "igual ao gabarito antigo" if dok_old==tot else f"{dok_new}/{tot} corretas")))
            for t in res["tabelas"]:
                for c in t["colunas"]:
                    n = c["divergencias"]
                    if c["coluna"] == "duplex_mhz": n = n - (tot - dok_old) + (tot - dok_new)
                    if n: colrows.append(dict(modelo=modelo, condicao=cond, rodada=run, tabela=t["tabela"], coluna=c["coluna"], divergencias=n))
            if not any(c["coluna"]=="duplex_mhz" for t in res["tabelas"] for c in t["colunas"]) and tot-dok_new:
                colrows.append(dict(modelo=modelo, condicao=cond, rodada=run, tabela="emissao", coluna="duplex_mhz", divergencias=tot-dok_new))
for nome, data in [("corretude_final.csv", rows), ("divergencias_coluna.csv", colrows)]:
    with open(nome, "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(data[0]), delimiter=";"); w.writeheader(); w.writerows(data)
print("geracoes", len(rows), "sha do gabarito distintos:", shas)
for r in rows: print(r["modelo"], r["condicao"], r["rodada"], r["iteracoes"], r["iteracao_aceita"], f'{r["taxa_original"]:.6f}', r["divergentes_original"], f'{r["taxa_corrigida"]:.6f}', r["divergentes_corrigido"], r["duplex_padrao"])
