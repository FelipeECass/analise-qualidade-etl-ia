"""Verificação de integração com dados sintéticos; não compara a base real."""
import csv
import io
import pathlib
import shutil
import sqlite3
import subprocess
import tempfile
import zipfile

PROJECT = pathlib.Path(__file__).resolve().parent
ROOT = PROJECT.parent.parent
NAME = PROJECT.name
STATES = 'AC AL AM AP BA CE DF ES GO MA MG MS MT PA PB PE PI PR RJ RN RO RR RS SC SE SP TO'.split()
FIELDS = 'NumFistel NumEstacao NumAto SiglaUf CodMunicipio Municipio.NomeMunicipio NomeEntidade EnderecoEstacao Latitude Longitude Tecnologia DesignacaoEmissao FreqTxMHz FreqRxMHz Azimute PotenciaTransmissorWatts GanhoAntena AlturaAntena AnguloElevacao AnguloMeiaPotenciaAntena FrenteCostaAntena Polarizacao CodTipoClasseEstacao CodTipoAntena CodEquipamentoAntena CodEquipamentoTransmissor Status.state DataLicenciamento DataPrimeiroLicenciamento DataValidade'.split()
# Ordem diferente da entrada real para verificar o mapeamento pelo cabeçalho.
FIELDS.reverse()
BASE = dict.fromkeys(FIELDS, '')
BASE.update(NumFistel=' 00123 ', NumEstacao=' 0007 ', NumAto=' 0009 ', SiglaUf=' ac ',
            CodMunicipio='00100', **{'Municipio.NomeMunicipio': 'São "João"|Centro\r\nZona'},
            NomeEntidade='Telefônica  Brasil S.A.', EnderecoEstacao=' Rua São-José, 10 ',
            Latitude='-33,75', Longitude='-73.99', Tecnologia=' lte ', DesignacaoEmissao='5m00g7w',
            FreqTxMHz='18.000', FreqRxMHz='18,0625', Azimute='360', PotenciaTransmissorWatts='40',
            GanhoAntena='NA', AlturaAntena='NaN', AnguloElevacao='-90', AnguloMeiaPotenciaAntena='18.000',
            FrenteCostaAntena='infinity', Polarizacao=' x ', CodTipoClasseEstacao='001', CodTipoAntena='002',
            CodEquipamentoAntena='003', CodEquipamentoTransmissor='004', **{'Status.state': ' 001 '},
            DataLicenciamento='2026-02-30', DataPrimeiroLicenciamento='2027-10-15', DataValidade='2026-10-14')

with tempfile.TemporaryDirectory(prefix='verificacao_', dir=ROOT) as tmp:
    root = pathlib.Path(tmp)
    (root / 'src').mkdir()
    (root / 'files').mkdir()
    shutil.copyfile(ROOT / 'src' / 'DDL.sql', root / 'src' / 'DDL.sql')
    app = root / 'src' / NAME
    shutil.copytree(PROJECT / 'bin' / 'Release' / 'net10.0', app)
    rows = [BASE, dict(BASE, FreqTxMHz='18,0', Polarizacao='h', EnderecoEstacao='OUTRO')]
    rows += [dict(BASE, **{key: ' '}) for key in ('NumFistel', 'NumEstacao', 'SiglaUf')]
    rows += [dict(BASE, NumAto='0010', NomeEntidade='CLARO', Latitude='0')]
    cases = [
        ('GSM', '2G', 'VIVO CLARO', 'VIVO', '10M0G7W', 10000),
        ('CDMA', '2G', 'EMBRATEL TIM', 'CLARO', '200KG7W', 200),
        ('WCDMA', '3G', 'TIM S.A.', 'TIM', '2M50F9W', 2500),
        ('wdcma', '3G', 'OI MOVEL', 'OI', '1H25', .00125),
        ('NR', '5G', 'EMPRESA TELEMAR', 'OI', '1G00', 1000000),
        ('', 'N/D', 'BRASIL TELECOM S.A.', 'OI', 'M500', None),
        ('WI-FI', 'OUTRA', 'ALGAR', 'ALGAR', '12MK', None),
        ('LTE', '4G', 'SERCOMTEL', 'SERCOMTEL', '1234', None),
        ('NR', '5G', 'BRISANET', 'BRISANET', '20M', None),
        ('LTE', '4G', 'ESTIMATIVA TIM', 'OUTRAS', '', None),
        ('NR', '5G', '!!!', 'OUTRAS', '1K00', 1),
    ]
    for i, (tech, _, entity, _, designation, _) in enumerate(cases):
        rows.append(dict(BASE, NumEstacao=str(100+i), Tecnologia=tech, NomeEntidade=entity,
                         DesignacaoEmissao=designation, Azimute='361', AnguloElevacao='90.01',
                         PotenciaTransmissorWatts='0', DataValidade='2026-10-15',
                         DataPrimeiroLicenciamento='2026-10-15'))
    nulls = dict(BASE, NumEstacao='200', NumAto='', Azimute='NA', FreqTxMHz='NaN', FreqRxMHz='',
                 DesignacaoEmissao='', Tecnologia='', AnguloElevacao='', Latitude='0', Longitude='0',
                 DataValidade='2026-1-01', DataPrimeiroLicenciamento=' 2026-1-01 ')
    rows += [nulls, dict(nulls, Azimute='', FreqTxMHz='Infinity'), dict(nulls, Azimute='0')]
    for uf in STATES:
        content = io.StringIO(newline='')
        writer = csv.DictWriter(content, fieldnames=FIELDS, delimiter='|', quoting=csv.QUOTE_ALL, lineterminator='\r\n')
        writer.writeheader()
        writer.writerows(rows if uf == 'AC' else [BASE, dict(BASE, SiglaUf='AL')] if uf == 'AL' else [])
        with zipfile.ZipFile(root / 'files' / f'{uf}.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
            archive.writestr('nome_independente.csv', '\ufeff' + content.getvalue())
    command = ['dotnet', str(app / f'{NAME}.dll')]
    for run in range(2):
        result = subprocess.run(command, capture_output=True, text=True)
        assert result.returncode == 0, result.stderr
        with sqlite3.connect(root / 'db' / f'{NAME}.db') as db:
            db.row_factory = sqlite3.Row
            assert db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
            assert not db.execute('PRAGMA foreign_key_check').fetchall()
            assert {r[0] for r in db.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")} == {'estacao', 'emissao'}
            assert db.execute('SELECT count(*) FROM estacao').fetchone()[0] == 14
            assert db.execute('SELECT count(*) FROM emissao').fetchone()[0] == 16
            station = db.execute("SELECT * FROM estacao WHERE uf='AC' AND num_estacao='0007'").fetchone()
            for key, expected in dict(fistel='00123', cod_municipio='00100', entidade='TELEFONICA BRASIL S A',
                                     endereco='RUA SAO JOSE 10', operadora='VIVO', coordenada_valida=1,
                                     municipio='São "João"|Centro\r\nZona').items():
                assert station[key] == expected, (key, station[key], expected)
            first = db.execute('SELECT * FROM emissao WHERE id=1').fetchone()
            for key, expected in dict(num_ato='0009', freq_tx_mhz=18, largura_banda_khz=5000,
                                     duplex_mhz=-.063, potencia_dbm=46.0, azimute_valido=1, elevacao_valida=1,
                                     data_licenciamento=None, anos_licenciada=-1.0, vencida=1,
                                     ganho_antena=None, altura_antena_m=None, frente_costa_db=None,
                                     angulo_meia_potencia_graus=18, polarizacao='X', classe_estacao='001',
                                     cod_tipo_antena='002', cod_equipamento_antena='003',
                                     cod_equipamento_transmissor='004', status='001').items():
                assert first[key] == expected, (key, first[key], expected)
            for i, (_, gen, _, operator, _, bw) in enumerate(cases):
                emission = db.execute('SELECT * FROM emissao WHERE num_estacao=?', (str(100+i),)).fetchone()
                assert tuple(emission[k] for k in ('geracao', 'largura_banda_khz', 'vencida', 'anos_licenciada', 'azimute_valido', 'elevacao_valida', 'potencia_dbm')) == (gen, bw, 0, 0, 0, 0, None)
                assert db.execute('SELECT operadora FROM estacao WHERE num_estacao=?', (str(100+i),)).fetchone()[0] == operator
            missing = db.execute("SELECT * FROM emissao WHERE num_estacao='200' ORDER BY id").fetchall()
            assert len(missing) == 2
            assert missing[0]['azimute_valido'] is None and missing[1]['azimute_valido'] == 1
            assert missing[0]['elevacao_valida'] is None and missing[0]['duplex_mhz'] is None
            assert missing[0]['data_validade'] is None and missing[0]['anos_licenciada'] is None
            assert db.execute("SELECT coordenada_valida FROM estacao WHERE num_estacao='200'").fetchone()[0] == 0
    # Falhas devem ser não interativas, com retorno não zero e stderr.
    (root / 'files' / 'TO.zip').unlink()
    failure = subprocess.run(command, capture_output=True, text=True)
    assert failure.returncode != 0 and 'ZIP obrigatório' in failure.stderr
print('Verificação sintética aprovada: transformações, CSV, deduplicação, esquema, reexecução e falha.')
