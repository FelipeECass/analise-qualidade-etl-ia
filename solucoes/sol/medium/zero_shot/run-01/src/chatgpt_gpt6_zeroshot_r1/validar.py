"""Teste de integração exclusivamente com dados sintéticos; não compara a base real."""
import csv
import io
from pathlib import Path
import shutil
import sqlite3
import subprocess
import tempfile
import zipfile

PROJECT = Path(__file__).resolve().parent
ROOT = PROJECT.parent.parent
STATES = 'AC AL AM AP BA CE DF ES GO MA MG MS MT PA PB PE PI PR RJ RN RO RR RS SC SE SP TO'.split()
HEADERS = '''NumFistel NumEstacao NumAto SiglaUf CodMunicipio Municipio.NomeMunicipio NomeEntidade EnderecoEstacao Latitude Longitude Tecnologia DesignacaoEmissao FreqTxMHz FreqRxMHz Azimute PotenciaTransmissorWatts GanhoAntena AlturaAntena AnguloElevacao AnguloMeiaPotenciaAntena FrenteCostaAntena Polarizacao CodTipoClasseEstacao CodTipoAntena CodEquipamentoAntena CodEquipamentoTransmissor Status.state DataLicenciamento DataPrimeiroLicenciamento DataValidade'''.split()[::-1]


def record(**changes):
    result = dict.fromkeys(HEADERS, '')
    result.update(NumFistel=' 00123 ', NumEstacao=' 0007 ', SiglaUf=' ac ', NumAto='0009',
                  NomeEntidade=' Telefônica  Brasil S.A. ', EnderecoEstacao=' Rua "Água" | 10\nCentro ',
                  CodMunicipio='001200', **{'Municipio.NomeMunicipio': ' São José '},
                  Latitude='-33,75', Longitude='-73.99', Tecnologia=' lte ', DesignacaoEmissao='5m00g7w',
                  FreqTxMHz='55,0', FreqRxMHz='18.000', Azimute='360', PotenciaTransmissorWatts='0.1',
                  AnguloElevacao='-90', DataLicenciamento='2026-10-15', DataPrimeiroLicenciamento='2025-10-15',
                  DataValidade='2026-10-14', Polarizacao=' x ', CodTipoAntena='00760')
    result.update(changes)
    return result


with tempfile.TemporaryDirectory(prefix='validacao_', dir=PROJECT) as temporary:
    test_root = Path(temporary)
    (test_root / 'files').mkdir()
    (test_root / 'src').mkdir()
    shutil.copy2(ROOT / 'src/DDL.sql', test_root / 'src/DDL.sql')
    shutil.copytree(PROJECT / 'bin/Release/net10.0', test_root / 'app')
    rows = [record(), record(FreqTxMHz='55', FreqRxMHz='18', Azimute='360.0',
                             NomeEntidade='CLARO', Polarizacao='v', AnguloElevacao='91'),
            record(NumAto='0010', NomeEntidade='CLARO', Latitude='0', Longitude='0'),
            record(NumFistel=' '), record(NumEstacao=''), record(SiglaUf='')]
    nulls = record(NumEstacao='nulls', NumAto='', Azimute='NaN', FreqTxMHz='NA', FreqRxMHz='Infinity',
                   DesignacaoEmissao='', Tecnologia='', PotenciaTransmissorWatts='-1', AnguloElevacao='',
                   Latitude='0', Longitude='0', DataLicenciamento='2026-2-01',
                   DataPrimeiroLicenciamento='2026-02-30', DataValidade='15/10/2026', NomeEntidade='?!')
    rows += [nulls, dict(nulls, Azimute='', FreqTxMHz='', FreqRxMHz='', Polarizacao='V')]
    for i, (entity, operator) in enumerate([
            ('ESTIMATIVA TIM', 'OUTRAS'), ('TIM S.A.', 'TIM'), ('OI MÓVEL', 'OI'),
            ('empresa TELEMAR', 'OI'), ('empresa BRASIL TELECOM', 'OI'), ('ALGAR', 'ALGAR'),
            ('SERCOMTEL', 'SERCOMTEL'), ('BRISANET', 'BRISANET'), ('EMBRATEL', 'CLARO'),
            ('CLARO VIVO', 'VIVO'), ('BRASIL TELECOMUNICACOES', 'OUTRAS')]):
        rows.append(record(NumEstacao=f'op{i}', NomeEntidade=entity))
    bandwidths = [('5M00G7W', 5000), ('10M0G7W', 10000), ('200KG7W', 200),
                  ('2M50F9W', 2500), ('1H00', .001), ('1G00', 1_000_000),
                  ('M123', None), ('1234', None), ('1MKG', None), ('1M', None)]
    for i, (designation, _) in enumerate(bandwidths):
        rows.append(record(NumEstacao=f'bw{i}', DesignacaoEmissao=designation))
    technologies = [('GSM', '2G'), ('cdma', '2G'), ('WCDMA', '3G'), ('wdcma', '3G'),
                    ('LTE', '4G'), ('NR', '5G'), ('', 'N/D'), ('other', 'OUTRA')]
    for i, (technology, _) in enumerate(technologies):
        rows.append(record(NumEstacao=f'gen{i}', Tecnologia=technology))
    rows += [record(NumEstacao='invalid', Azimute='-0.1', AnguloElevacao='90.1',
                    Latitude='5.2801', Longitude='-28.83', DataValidade='2026-10-15',
                    FreqTxMHz='55', FreqRxMHz='55.0015'),
             record(NumEstacao='positive', FreqTxMHz='55.0015', FreqRxMHz='55',
                    Latitude='5.28', Longitude='-28.83', DataPrimeiroLicenciamento='2027-10-15')]
    for uf in STATES:
        content = io.StringIO(newline='')
        writer = csv.DictWriter(content, HEADERS, delimiter='|', quoting=csv.QUOTE_ALL)
        writer.writeheader()
        writer.writerows(rows if uf == 'AC' else [record(SiglaUf='AL'), record()] if uf == 'AL' else [])
        with zipfile.ZipFile(test_root / 'files' / f'{uf}.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
            archive.writestr('nome_arbitrario.csv', '\ufeff' + content.getvalue())

    command = ['dotnet', str(test_root / 'app/chatgpt_gpt6_zeroshot_r1.dll')]
    def run():
        result = subprocess.run(command, text=True, capture_output=True, cwd=test_root)
        assert result.returncode == 0, result.stderr
        return result

    run()
    db_path = test_root / 'db/chatgpt_gpt6_zeroshot_r1.db'
    with sqlite3.connect(db_path) as db:
        db.row_factory = sqlite3.Row
        station = db.execute("select * from estacao where uf='AC' and num_estacao='0007'").fetchone()
        assert station['entidade'] == 'TELEFONICA BRASIL S A' and station['operadora'] == 'VIVO'
        assert station['endereco'] == 'RUA AGUA 10 CENTRO' and station['municipio'] == 'São José'
        assert station['fistel'] == '00123' and station['cod_municipio'] == '001200'
        assert station['coordenada_valida'] == 1 and station['latitude'] == -33.75
        emission = db.execute("select * from emissao where uf='AC' and num_estacao='0007' order by id").fetchall()
        assert len(emission) == 2 and emission[0]['polarizacao'] == 'X'
        first = emission[0]
        assert (first['duplex_mhz'], first['potencia_dbm'], first['anos_licenciada']) == (37, 20, 1)
        assert (first['vencida'], first['azimute_valido'], first['elevacao_valida']) == (1, 1, 1)
        assert first['cod_tipo_antena'] == '00760'
        null = db.execute("select * from emissao where num_estacao='nulls'").fetchall()
        assert len(null) == 1
        for column in ['azimute', 'freq_tx_mhz', 'freq_rx_mhz', 'duplex_mhz', 'largura_banda_khz',
                       'potencia_dbm', 'azimute_valido', 'elevacao_valida', 'data_licenciamento',
                       'data_primeiro_licenciamento', 'data_validade', 'anos_licenciada']:
            assert null[0][column] is None, column
        assert null[0]['vencida'] == 0 and null[0]['geracao'] == 'N/D'
        for i, (_, operator) in enumerate([
                ('', 'OUTRAS'), ('', 'TIM'), ('', 'OI'), ('', 'OI'), ('', 'OI'), ('', 'ALGAR'),
                ('', 'SERCOMTEL'), ('', 'BRISANET'), ('', 'CLARO'), ('', 'VIVO'), ('', 'OUTRAS')]):
            assert db.execute('select operadora from estacao where num_estacao=?', (f'op{i}',)).fetchone()[0] == operator
        for i, (_, expected) in enumerate(bandwidths):
            assert db.execute('select largura_banda_khz from emissao where num_estacao=?', (f'bw{i}',)).fetchone()[0] == expected
        for i, (_, expected) in enumerate(technologies):
            assert db.execute('select geracao from emissao where num_estacao=?', (f'gen{i}',)).fetchone()[0] == expected
        invalid = db.execute("select * from emissao where num_estacao='invalid'").fetchone()
        assert (invalid['azimute_valido'], invalid['elevacao_valida'], invalid['duplex_mhz'], invalid['vencida']) == (0, 0, -.002, 0)
        assert db.execute("select duplex_mhz from emissao where num_estacao='positive'").fetchone()[0] == .002
        assert db.execute("select coordenada_valida from estacao where num_estacao='positive'").fetchone()[0] == 1
        assert db.execute("select coordenada_valida from estacao where num_estacao='invalid'").fetchone()[0] == 0
        assert db.execute("select anos_licenciada from emissao where num_estacao='positive'").fetchone()[0] == -1
        assert db.execute("select count(*) from estacao where num_estacao='0007'").fetchone()[0] == 2
        assert db.execute('pragma integrity_check').fetchone()[0] == 'ok'
        assert not db.execute('pragma foreign_key_check').fetchall()
        with sqlite3.connect(':memory:') as schema:
            schema.executescript((ROOT / 'src/DDL.sql').read_bytes().decode('utf-8-sig'))
            query = "select type,name,tbl_name,sql from sqlite_master order by type,name"
            assert [tuple(r) for r in db.execute(query)] == schema.execute(query).fetchall()
        counts = tuple(db.execute(f'select count(*) from {table}').fetchone()[0] for table in ['estacao', 'emissao'])
        assert counts == (34, 35), counts
    run()
    with sqlite3.connect(db_path) as db:
        assert tuple(db.execute(f'select count(*) from {table}').fetchone()[0] for table in ['estacao', 'emissao']) == counts
    (test_root / 'files/TO.zip').unlink()
    failure = subprocess.run(command, text=True, capture_output=True, cwd=test_root)
    assert failure.returncode != 0 and 'ZIP obrigatório' in failure.stderr
    print('Validação sintética aprovada: limpeza, derivados, limites, deduplicação, esquema, reexecução e falhas.')
