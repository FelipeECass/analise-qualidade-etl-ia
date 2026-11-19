"""Aplica no base.db a correcao do duplex_mhz (valores em duplex_corrigido.db, tabela d, que guarda
tambem a linha original para reverter). Uso: 06_atualizar_base.py <base.db> duplex_corrigido.db"""
import sqlite3, sys
base, dup = sys.argv[1:]
con = sqlite3.connect(base)
con.execute("PRAGMA journal_mode = TRUNCATE")  # a pasta pode nao permitir apagar o -journal
con.execute(f"ATTACH DATABASE 'file:{dup}?mode=ro' AS c")
n = con.execute("UPDATE emissao SET duplex_mhz = (SELECT duplex_novo FROM c.d WHERE c.d.grid = emissao.rowid) WHERE rowid IN (SELECT grid FROM c.d)").rowcount
con.commit()
print("linhas atualizadas:", n)
