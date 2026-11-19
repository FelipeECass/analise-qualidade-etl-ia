"""Testes de integração sintéticos; não compara a base real com resultado esperado.
Executar após dotnet build -c Release: python3 verificar.py
"""
import csv
import io
import pathlib
import shutil
import sqlite3
import subprocess
import tempfile
import zipfile

PROJECT = pathlib.Path(__file__).resolve().parent
NAME = PROJECT.name
ROOT = PROJECT.parent.parent
UFS = 'AC AL AM AP BA CE DF ES GO MA MG MS MT PA PB PE PI PR RJ RN RO RR RS SC SE SP TO'.split()
COLUMNS = ['NumFistel','NumEstacao','NumAto','SiglaUf','CodMunicipio','Municipio.NomeMunicipio','NomeEntidade','EnderecoEstacao','Latitude','Longitude','Tecnologia','DesignacaoEmissao','FreqTxMHz','FreqRxMHz','Azimute','PotenciaTransmissorWatts','GanhoAntena','AlturaAntena','AnguloElevacao','AnguloMeiaPotenciaAntena','FrenteCostaAntena','Polarizacao','CodTipoClasseEstacao','CodTipoAntena','CodEquipamentoAntena','CodEquipamentoTransmissor','Status.state','DataLicenciamento','DataPrimeiroLicenciamento','DataValidade'][::-1]
checks = 0

def check(condition, message):
    global checks
    assert condition, message
    checks += 1

def row(number, **kwargs):
    result = dict(NumFistel=' 00123 ', NumEstacao=str(number), SiglaUf=' ac ', NumAto=' 0007 ',
                  NomeEntidade=' Telefônica  Brasil S.A. ', EnderecoEstacao=' Rua "A" | São\nPaulo ',
                  CodMunicipio='0001', **{'Municipio.NomeMunicipio':' São Paulo '},
                  Latitude='-33,75', Longitude='-73.99', Tecnologia=' lte ', DesignacaoEmissao='2m50F9W',
                  FreqTxMHz='55.0', FreqRxMHz='18.000', Azimute='360', PotenciaTransmissorWatts='10.000',
                  AnguloElevacao='-90', Polarizacao='x', CodEquipamentoAntena='00009',
                  DataLicenciamento='2023-08-21', DataPrimeiroLicenciamento='1999-11-23', DataValidade='2026-10-15')
    result.update(kwargs)
    return result

def write_zip(root, uf, records):
    stream = io.StringIO(newline='')
    writer = csv.writer(stream, delimiter='|', quoting=csv.QUOTE_ALL, lineterminator='\r\n')
    writer.writerow(COLUMNS)
    for record in records:
        writer.writerow([record.get(c, '') for c in COLUMNS])
    with zipfile.ZipFile(root/'files'/f'{uf}.zip', 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr('nome_independente.csv', '\ufeff'+stream.getvalue())

with tempfile.TemporaryDirectory(prefix='verificacao_', dir=PROJECT) as tmp:
    root = pathlib.Path(tmp)
    (root/'files').mkdir()
    (root/'src').mkdir()
    shutil.copy2(ROOT/'src'/'DDL.sql', root/'src'/'DDL.sql')
    shutil.copytree(PROJECT/'bin'/'Release'/'net10.0', root/'app')
    records = [row('0001'), row('0001', FreqTxMHz='55', Polarizacao='H', NomeEntidade='CLARO'),
               row('0001', FreqTxMHz='56'), row('0001', SiglaUf='RR'),
               row('reject', NumFistel='  '), row(' ', NumFistel='1'), row('reject2', SiglaUf='')]
    records += [row('null', Azimute='NA', FreqTxMHz='NaN', FreqRxMHz='infinity', Tecnologia='', NumAto='',
                    Latitude='0', Longitude='0', DesignacaoEmissao='H123', PotenciaTransmissorWatts='0',
                    AnguloElevacao='', DataPrimeiroLicenciamento='2020-02-30', DataValidade='2026-10-14'),
                row('null', Azimute='', FreqTxMHz='', FreqRxMHz='', Tecnologia='', NumAto='', DesignacaoEmissao='H123')]
    operators = [('VIVO CLARO','VIVO'), ('ESTIMATIVA','OUTRAS'), ('TIM S A','TIM'), ('X TIM','OUTRAS'),
                 ('OI S A','OI'), ('X OI','OUTRAS'), ('X TELEMAR','OI'), ('X BRASIL TELECOM','OI'),
                 ('X ALGAR','ALGAR'), ('SERCOMTEL','SERCOMTEL'), ('BRISANET','BRISANET'), ('EMBRATEL','CLARO'), ('!!!','OUTRAS')]
    for i, (entity, _) in enumerate(operators): records.append(row(f'op{i}', NomeEntidade=entity))
    for i, (designation, _) in enumerate([('5M00G7W',5000),('10M0G7W',10000),('200KG7W',200),('2M50F9W',2500),('123H',.123),('1G25',1250000),('1234',None),('2MK0',None),('2M',None)]):
        records.append(row(f'band{i}', DesignacaoEmissao=designation))
    for i, tech in enumerate(['gsm','cdma','wcdma','wdcma','lte','nr','','other']): records.append(row(f'gen{i}',Tecnologia=tech))
    records.extend([row('round',FreqTxMHz='1.0005',FreqRxMHz='0',PotenciaTransmissorWatts='-1',AnguloElevacao='90.1',Azimute='-1'),
                    row('negative',FreqTxMHz='0',FreqRxMHz='1.0005',DataValidade='2026-10-16',DataLicenciamento='2020-1-01'),
                    row('future',DataPrimeiroLicenciamento='2027-10-15',Azimute='0',AnguloElevacao='90'),
                    row('badnum',FreqTxMHz='1,234.5',FreqRxMHz='1e999',Latitude='NaN',PotenciaTransmissorWatts='1e308')])
    for uf in UFS: write_zip(root, uf, records if uf=='AC' else [])
    # Cross-file duplicate must also be detected globally.
    write_zip(root,'TO',[row('0001', SiglaUf='ac')])
    result = subprocess.run(['dotnet',str(root/'app'/f'{NAME}.dll')],capture_output=True,text=True)
    check(result.returncode==0, result.stderr)
    db = root/'db'/f'{NAME}.db'
    con = sqlite3.connect(db)
    con.row_factory=sqlite3.Row
    def get(number,table='emissao',uf='AC'):
        return con.execute(f'SELECT * FROM {table} WHERE num_estacao=? AND uf=? ORDER BY rowid',(number,uf)).fetchone()
    check(con.execute('SELECT count(*) FROM emissao WHERE num_estacao="0001"').fetchone()[0]==3,'deduplicação numérica/UF')
    check(con.execute('SELECT count(*) FROM emissao WHERE num_estacao="null"').fetchone()[0]==1,'deduplicação NULL')
    e=get('0001');s=get('0001','estacao')
    for column,value in dict(fistel='00123',num_ato='0007',geracao='4G',largura_banda_khz=2500,duplex_mhz=37,potencia_w=10,potencia_dbm=40,vencida=0,anos_licenciada=26.9,polarizacao='X',cod_equipamento_antena='00009',azimute_valido=1,elevacao_valida=1).items():
        check(e[column]==value,f'{column}: {e[column]} != {value}')
    for column,value in dict(entidade='TELEFONICA BRASIL S A',operadora='VIVO',endereco='RUA A SAO PAULO',cod_municipio='0001',municipio='São Paulo',coordenada_valida=1).items():
        check(s[column]==value,f'estacao.{column}')
    e=get('null')
    for column in ['azimute','freq_tx_mhz','freq_rx_mhz','duplex_mhz','azimute_valido','elevacao_valida','potencia_dbm','largura_banda_khz','anos_licenciada']:
        check(e[column] is None, f'NULL: {column}')
    check(e['vencida']==1 and e['geracao']=='N/D','data fixa e geração nula')
    check(get('null','estacao')['coordenada_valida']==0,'coordenadas zero')
    for i, (_,expected) in enumerate(operators): check(get(f'op{i}','estacao')['operadora']==expected, f'operadora {i}')
    for i, expected in enumerate([5000,10000,200,2500,.123,1250000,None,None,None]): check(get(f'band{i}')['largura_banda_khz']==expected,f'banda {i}')
    for i, expected in enumerate(['2G','2G','3G','3G','4G','5G','N/D','OUTRA']): check(get(f'gen{i}')['geracao']==expected,f'geração {i}')
    check(get('round')['duplex_mhz']==1.001 and get('negative')['duplex_mhz']==-1.001,'metades longe de zero')
    check(get('round')['azimute_valido']==0 and get('round')['elevacao_valida']==0,'validações falsas')
    check(get('negative')['data_licenciamento'] is None,'data estrita')
    check(get('future')['anos_licenciada']==-1.0,'data futura')
    check(get('future')['azimute_valido']==1 and get('future')['elevacao_valida']==1,'limites inclusivos')
    check(get('badnum')['freq_tx_mhz'] is None and get('badnum')['freq_rx_mhz'] is None,'número inválido/overflow')
    check(get('badnum')['potencia_dbm']==3110,'logaritmo sem overflow intermediário')
    check(con.execute('PRAGMA integrity_check').fetchone()[0]=='ok','integridade')
    check(not con.execute('PRAGMA foreign_key_check').fetchall(),'chaves estrangeiras')
    snapshot=con.execute('SELECT count(*) FROM emissao').fetchone()[0]
    con.close()
    result=subprocess.run(['dotnet',str(root/'app'/f'{NAME}.dll')],capture_output=True,text=True)
    check(result.returncode!=0 and 'Banco já existe' in result.stderr and db.exists(),'proteção de banco existente')
    db.unlink()
    (root/'files'/'TO.zip').unlink()
    result=subprocess.run(['dotnet',str(root/'app'/f'{NAME}.dll')],capture_output=True,text=True)
    check(result.returncode!=0 and result.stderr and not db.exists(),'falha ZIP ausente')
    # Malformed data must fail with stderr and remove a partially populated database.
    with zipfile.ZipFile(root/'files'/'TO.zip','w') as z: z.writestr('bad.csv','CampoAusente\nvalor\n')
    result=subprocess.run(['dotnet',str(root/'app'/f'{NAME}.dll')],capture_output=True,text=True)
    check(result.returncode!=0 and 'coluna ausente' in result.stderr and not db.exists(),'falha limpa por cabeçalho inválido')
print(f'{checks} verificações sintéticas passaram; fixture removida.')
