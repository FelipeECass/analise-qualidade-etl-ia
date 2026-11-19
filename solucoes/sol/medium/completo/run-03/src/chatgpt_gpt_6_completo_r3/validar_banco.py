"""Audita esquema e invariantes da saída real, sem resultado esperado externo."""
import json
import pathlib
import sqlite3
import sys

project=pathlib.Path(__file__).resolve().parent
root=project.parent.parent
path=root/'db'/f'{project.name}.db'
con=sqlite3.connect(f'file:{path}?mode=ro',uri=True)
con.execute('PRAGMA temp_store=MEMORY')
reference=sqlite3.connect(':memory:')
reference.executescript((root/'src'/'DDL.sql').read_bytes().decode('utf-8-sig'))

def schema(c):
    return c.execute("SELECT type,name,tbl_name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name").fetchall()
assert schema(con)==schema(reference),'Esquema diverge do DDL'
assert con.execute('PRAGMA integrity_check').fetchall()==[('ok',)],'Integridade'
assert not con.execute('PRAGMA foreign_key_check').fetchall(),'Referências órfãs'
assert con.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name").fetchall()==[('emissao',),('estacao',)]
checks={
 'estacao': {
  'chave_vazia': "trim(uf)='' OR trim(fistel)='' OR trim(num_estacao)=''",
  'coordenada_valida': 'coordenada_valida != CASE WHEN latitude IS NOT NULL AND longitude IS NOT NULL AND (latitude!=0 OR longitude!=0) AND latitude BETWEEN -33.75 AND 5.28 AND longitude BETWEEN -73.99 AND -28.83 THEN 1 ELSE 0 END',
  'operadora_desconhecida': "operadora NOT IN ('VIVO','CLARO','TIM','OI','ALGAR','SERCOMTEL','BRISANET','OUTRAS')"
 },
 'emissao': {
  'chave_vazia': "trim(uf)='' OR trim(fistel)='' OR trim(num_estacao)=''",
  'geracao': "geracao != CASE WHEN tecnologia IS NULL THEN 'N/D' WHEN tecnologia IN ('GSM','CDMA') THEN '2G' WHEN tecnologia IN ('WCDMA','WDCMA') THEN '3G' WHEN tecnologia='LTE' THEN '4G' WHEN tecnologia='NR' THEN '5G' ELSE 'OUTRA' END",
  'azimute_valido': 'azimute_valido IS NOT CASE WHEN azimute IS NULL THEN NULL WHEN azimute BETWEEN 0 AND 360 THEN 1 ELSE 0 END',
  'elevacao_valida': 'elevacao_valida IS NOT CASE WHEN angulo_elevacao_graus IS NULL THEN NULL WHEN angulo_elevacao_graus BETWEEN -90 AND 90 THEN 1 ELSE 0 END',
  'vencida': "vencida != CASE WHEN data_validade < '2026-10-15' THEN 1 ELSE 0 END",
  'anos_licenciada': "(anos_licenciada IS NULL) != (data_primeiro_licenciamento IS NULL) OR abs(anos_licenciada - round((julianday('2026-10-15')-julianday(data_primeiro_licenciamento))/365.25,1))>0.000001",
  'duplex': '(duplex_mhz IS NULL) != (freq_tx_mhz IS NULL OR freq_rx_mhz IS NULL) OR abs(duplex_mhz-(freq_tx_mhz-freq_rx_mhz))>0.0005001',
  'potencia_dbm': '(potencia_dbm IS NULL) != (potencia_w IS NULL OR potencia_w<=0) OR abs(potencia_dbm-10*(log10(potencia_w)+3))>0.0500001',
  'largura_banda_negativa': 'largura_banda_khz<0',
 }
}
report={'banco':str(path),'tamanho_bytes':path.stat().st_size,'integridade':'ok','esquema':'idêntico ao DDL.sql','foreign_keys':'ok','tabelas':{}}
for table, conditions in checks.items():
    text_conditions=[]
    date_conditions=[]
    for _,name,kind,*_ in con.execute(f'PRAGMA table_info({table})'):
        if kind=='TEXT':
            text_conditions.append(f'("{name}" IS NOT NULL AND ("{name}" != trim("{name}") OR "{name}"=\'\'))')
        if name.startswith('data_'):
            date_conditions.append(f'("{name}" IS NOT NULL AND (length("{name}")!=10 OR date("{name}",\'+0 days\') IS NOT "{name}"))')
    conditions['textos_nao_limpos']=' OR '.join(text_conditions)
    if date_conditions: conditions['datas_invalidas']=' OR '.join(date_conditions)
    names=list(conditions)
    expressions=','.join(f'coalesce(sum(CASE WHEN {conditions[n]} THEN 1 ELSE 0 END),0)' for n in names)
    data=con.execute(f'SELECT count(*),{expressions} FROM {table}').fetchone()
    violations=dict(zip(names,data[1:]))
    report['tabelas'][table]={'linhas':data[0],'violacoes':violations}
    assert not any(violations.values()),violations
key='uf,fistel,num_estacao,num_ato,azimute,freq_tx_mhz,freq_rx_mhz,designacao_emissao,tecnologia'
duplicates=con.execute(f'SELECT count(*) FROM (SELECT 1 FROM emissao GROUP BY {key} HAVING count(*)>1)').fetchone()[0]
report['grupos_duplicados_emissao']=duplicates
assert duplicates==0,'Emissões duplicadas'
con.close()
reference.close()
text=json.dumps(report,ensure_ascii=False,indent=2)
(project/'validacao_banco.json').write_text(text+'\n')
print(text)
