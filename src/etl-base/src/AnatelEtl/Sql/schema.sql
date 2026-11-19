-- Esquema do banco SQLite do ETL da Anatel (licenciamento).
-- Uma estação (torre) tem N emissões (setor/frequência).
-- A UF faz parte da chave: a mesma combinação fistel/num_estacao existe em UFs diferentes.

CREATE TABLE IF NOT EXISTS estacao (
    fistel            TEXT    NOT NULL,
    num_estacao       TEXT    NOT NULL,
    uf                TEXT    NOT NULL,
    cod_municipio     TEXT,
    municipio         TEXT,
    endereco          TEXT,
    latitude          REAL,
    longitude         REAL,
    coordenada_valida INTEGER NOT NULL,
    entidade          TEXT,
    operadora         TEXT    NOT NULL,
    PRIMARY KEY (uf, fistel, num_estacao)
);

CREATE TABLE IF NOT EXISTS emissao (
    id                          INTEGER PRIMARY KEY AUTOINCREMENT,
    uf                          TEXT    NOT NULL,
    fistel                      TEXT    NOT NULL,
    num_estacao                 TEXT    NOT NULL,
    num_ato                     TEXT,
    tecnologia                  TEXT,
    geracao                     TEXT    NOT NULL,
    designacao_emissao          TEXT,
    largura_banda_khz           REAL,
    freq_tx_mhz                 REAL,
    freq_rx_mhz                 REAL,
    duplex_mhz                  REAL,
    faixa_frequencia            TEXT    NOT NULL,
    azimute                     REAL,
    potencia_w                  REAL,
    ganho_antena                REAL,
    altura_antena_m             REAL,
    data_licenciamento          TEXT,
    data_primeiro_licenciamento TEXT,
    data_validade               TEXT,
    vencida                     INTEGER NOT NULL,
    anos_licenciada             REAL,
    -- Dados de rádio/antena e homologação (valores originais, sem transformação)
    status                      TEXT,
    classe_estacao              TEXT,
    polarizacao                 TEXT,
    angulo_elevacao_graus       REAL,
    angulo_meia_potencia_graus  REAL,
    frente_costa_db             REAL,
    cod_tipo_antena             TEXT,
    cod_equipamento_antena      TEXT,
    cod_equipamento_transmissor TEXT,
    -- Derivados/validações técnicas (NULL = sem dado suficiente para decidir)
    potencia_dbm                REAL,
    azimute_valido              INTEGER,
    elevacao_valida             INTEGER,
    duplex_coerente             INTEGER,
    FOREIGN KEY (uf, fistel, num_estacao) REFERENCES estacao (uf, fistel, num_estacao)
);

CREATE INDEX IF NOT EXISTS ix_emissao_estacao ON emissao (uf, fistel, num_estacao);
CREATE INDEX IF NOT EXISTS ix_emissao_geracao ON emissao (geracao);
