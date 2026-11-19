"""Extrai do base.db as linhas de emissao cujo duplex_mhz difere da regra da especificacao
(freq_tx - freq_rx em aritmetica decimal exata, 3 casas, metade para longe do zero).
Gera duplex_corrigido.db com essas linhas (todas as colunas) e a coluna duplex_novo."""
import sqlite3, sys
from decimal import Decimal, ROUND_HALF_UP
base, saida = sys.argv[1], sys.argv[2]
con = sqlite3.connect(saida)
con.execute(f"ATTACH DATABASE 'file:{base}?mode=ro' AS g")
cols = [r[1] for r in con.execute("PRAGMA g.table_info(emissao)")]
con.execute("DROP TABLE IF EXISTS d")
con.execute("CREATE TABLE d AS SELECT rowid AS grid, * FROM g.emissao WHERE 0")
con.execute("ALTER TABLE d ADD COLUMN duplex_novo REAL")
q = """SELECT rowid, * FROM g.emissao WHERE freq_tx_mhz IS NOT NULL AND freq_rx_mhz IS NOT NULL
AND abs(round((freq_tx_mhz-freq_rx_mhz)*1000,0) - (freq_tx_mhz-freq_rx_mhz)*1000) BETWEEN 0.4999 AND 0.5001"""
itx, irx, idp = cols.index('freq_tx_mhz')+1, cols.index('freq_rx_mhz')+1, cols.index('duplex_mhz')+1
rows = []
for r in con.execute(q):
    c = float((Decimal(repr(r[itx])) - Decimal(repr(r[irx]))).quantize(Decimal('0.001'), rounding=ROUND_HALF_UP))
    if c != r[idp]: rows.append(tuple(r) + (c,))
con.executemany(f"INSERT INTO d VALUES ({','.join('?'*(len(cols)+2))})", rows)
con.commit()
print("linhas corrigidas:", len(rows))
