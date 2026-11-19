"""Custo estimado de cada geracao a precos de lista da API (USD por milhao de tokens, consulta em 14/11/2026).
Anthropic: por requisicao, a partir dos logs JSONL do Claude Code (o Haiku 5.5 cobra a tarifa maior
quando o prompt da requisicao passa de 100 mil tokens). OpenAI: totais de tokens_metricas_todos.csv."""
import json, glob, os, csv
import pandas as pd
B = os.path.expanduser("~/mnt/TCC")
# entrada, escrita de cache 5 min, escrita 1 h, leitura de cache, saida
P = {"haiku": (0.10, 0.125, 0.20, 0.01, 0.50), "haiku_longo": (0.50, 0.625, 1.00, 0.05, 2.50),
     "sonnet": (2.00, 2.50, 4.00, 0.10, 10.00), "opus": (4.00, 5.00, 8.00, 0.20, 20.00)}
PO = {"luna": (0.10, 0.01, 0.50), "terra": (1.00, 0.05, 5.00), "sol": (2.00, 0.10, 10.00)}
t = pd.read_csv(f"{B}/tokens_metricas_todos.csv", sep=";", encoding="utf-8-sig")
t = t[t.esforco == "medium"]
out = []
for _, r in t.iterrows():
    m, cond, run = r.modelo, r.prompt, r.execucao
    if r.empresa == "Anthropic":
        msgs = {}
        for f in glob.glob(f"{B}/logs/{m}/medium/{cond}/{run}/projects/**/*.jsonl", recursive=True):
            for l in open(f, encoding="utf-8"):
                o = json.loads(l); g = o.get("message") or {}
                if o.get("type") == "assistant" and g.get("usage") and g.get("id"): msgs[g["id"]] = g["usage"]
        custo, longas, tok = 0.0, 0, [0, 0, 0, 0]
        for u in msgs.values():
            cc = u.get("cache_creation") or {}
            w5, w1 = cc.get("ephemeral_5m_input_tokens", 0), cc.get("ephemeral_1h_input_tokens", 0)
            if not cc: w5 = u.get("cache_creation_input_tokens", 0)
            i, rd, o_ = u.get("input_tokens", 0), u.get("cache_read_input_tokens", 0), u.get("output_tokens", 0)
            k = m
            if m == "haiku" and i + w5 + w1 + rd > 100_000: k, longas = "haiku_longo", longas + 1
            p = P[k]; custo += (i*p[0] + w5*p[1] + w1*p[2] + rd*p[3] + o_*p[4]) / 1e6
            tok = [tok[0]+i, tok[1]+w5+w1, tok[2]+rd, tok[3]+o_]
        confere = tok == [r.input_tokens, r.cache_write_tokens, r.cache_read_tokens, r.output_tokens]
        out.append(dict(modelo=m, condicao=cond, execucao=run, requisicoes=len(msgs), requisicoes_acima_100k=longas, custo_usd=round(custo, 4), confere_csv=confere))
    else:
        p = PO[m]; custo = (r.input_tokens*p[0] + r.cache_read_tokens*p[1] + r.output_tokens*p[2]) / 1e6
        out.append(dict(modelo=m, condicao=cond, execucao=run, requisicoes=r.chamadas_api, requisicoes_acima_100k="", custo_usd=round(custo, 4), confere_csv=""))
with open("custo_estimado.csv", "w", newline="") as f:
    w = csv.DictWriter(f, fieldnames=list(out[0]), delimiter=";"); w.writeheader(); w.writerows(out)
d = pd.DataFrame(out); print(d.to_string())
print(d.groupby("modelo").custo_usd.agg(["sum","mean","std","min","max"]).round(3))
print(d.groupby(["modelo","condicao"]).custo_usd.mean().unstack().round(3))
