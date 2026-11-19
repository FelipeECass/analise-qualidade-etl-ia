"""Para cada banco de solucao, pareia as linhas corrigidas do gabarito (duplex_corrigido.db)
pela mesma chave e regra do Comparador (primeira ocorrencia, NULL IS NULL) e conta quantas
linhas estavam certas antes e ficam certas depois da correcao. Saida: TSV por solucao."""
import sqlite3, sys, time
dup, *sols = sys.argv[1:]
CH = ["uf","fistel","num_estacao","num_ato","azimute","freq_tx_mhz","freq_rx_mhz","designacao_emissao","tecnologia"]
for sol in sols:
    t = time.time()
    con = sqlite3.connect(":memory:")
    con.execute(f"ATTACH DATABASE 'file:{dup}?mode=ro' AS g")
    con.execute(f"ATTACH DATABASE 'file:{sol}?mode=ro' AS s")
    cols = [r[1] for r in con.execute("PRAGMA s.table_info(emissao)") if r[1] != "id"]
    on = " AND ".join(f"s.{c} IS d.{c}" for c in CH)
    con.execute(f"CREATE TEMP TABLE m AS SELECT d.grid, min(s.rowid) AS srid FROM g.d d JOIN s.emissao s ON {on} GROUP BY d.grid")
    iguais = lambda dup_col: " AND ".join(f"b.{c} IS a.{c}" if c != "duplex_mhz" else f"b.duplex_mhz IS a.{dup_col}" for c in cols)
    r = con.execute(f"""SELECT count(*), coalesce(sum({iguais('duplex_mhz')}),0), coalesce(sum({iguais('duplex_novo')}),0),
        coalesce(sum(b.duplex_mhz IS a.duplex_mhz),0), coalesce(sum(b.duplex_mhz IS a.duplex_novo),0)
        FROM temp.m p JOIN g.d a ON a.grid = p.grid JOIN s.emissao b ON b.rowid = p.srid""").fetchone()
    nome = sol
    print(nome, *r, round(time.time()-t), sep="\t", flush=True)
