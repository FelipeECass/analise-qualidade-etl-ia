-- Extrai do gabarito as linhas dos 3 exemplos do few-shot.
-- Uso: sqlite3 "<caminho do gabarito>.db" ".read few_shot.sql"
-- Saída: few_shot_saida.txt, na pasta onde o comando foi executado.

.output few_shot_saida.txt
.mode line
.nullvalue NULL

-- Primeira emissão (menor id) e total de emissões de cada estação
DROP TABLE IF EXISTS temp.primeira;
CREATE TEMP TABLE primeira AS
SELECT uf, fistel, num_estacao, MIN(id) AS id, COUNT(*) AS n
FROM emissao
GROUP BY uf, fistel, num_estacao;

-- Emissões escolhidas para cada exemplo
DROP TABLE IF EXISTS temp.ex;
CREATE TEMP TABLE ex (exemplo INTEGER, id INTEGER);

-- Exemplo 1: caso típico (operadora reconhecida, validações = 1)
INSERT INTO ex
SELECT 1, p.id
FROM primeira p
JOIN emissao m ON m.id = p.id
JOIN estacao e ON e.uf = p.uf AND e.fistel = p.fistel AND e.num_estacao = p.num_estacao
WHERE e.operadora = 'VIVO'
  AND e.coordenada_valida = 1
  AND m.azimute_valido = 1
  AND m.elevacao_valida = 1
  AND m.potencia_dbm IS NOT NULL
ORDER BY p.id
LIMIT 1;

-- Exemplo 2: nulos e entidade fora das operadoras (OUTRAS)
INSERT INTO ex
SELECT 2, p.id
FROM primeira p
JOIN emissao m ON m.id = p.id
JOIN estacao e ON e.uf = p.uf AND e.fistel = p.fistel AND e.num_estacao = p.num_estacao
WHERE e.operadora = 'OUTRAS'
  AND m.azimute_valido IS NULL
ORDER BY p.id
LIMIT 1;

-- Exemplo 3: estação com exatamente 2 emissões (as duas)
INSERT INTO ex
SELECT 3, m.id
FROM emissao m
JOIN (SELECT uf, fistel, num_estacao FROM primeira WHERE n = 2 ORDER BY id LIMIT 1) s
  ON m.uf = s.uf AND m.fistel = s.fistel AND m.num_estacao = s.num_estacao;

-- Exemplo exibido em cada bloco
DROP TABLE IF EXISTS temp.atual;
CREATE TEMP TABLE atual (n INTEGER);

-- Chave para localizar a linha no CSV
DROP VIEW IF EXISTS temp.v_chave;
CREATE TEMP VIEW v_chave AS
SELECT m.uf, m.fistel, m.num_estacao, m.num_ato, m.freq_tx_mhz, m.azimute
FROM atual
JOIN ex ON ex.exemplo = atual.n
JOIN emissao m ON m.id = ex.id
ORDER BY m.id;

-- Linha esperada em estacao
DROP VIEW IF EXISTS temp.v_estacao;
CREATE TEMP VIEW v_estacao AS
SELECT DISTINCT
  e.uf, e.fistel, e.num_estacao, e.cod_municipio, e.municipio, e.endereco,
  e.latitude, e.longitude, e.coordenada_valida, e.entidade, e.operadora
FROM atual
JOIN ex ON ex.exemplo = atual.n
JOIN emissao m ON m.id = ex.id
JOIN estacao e ON e.uf = m.uf AND e.fistel = m.fistel AND e.num_estacao = m.num_estacao;

-- Linha(s) esperada(s) em emissao, sem a coluna id
DROP VIEW IF EXISTS temp.v_emissao;
CREATE TEMP VIEW v_emissao AS
SELECT
  m.uf, m.fistel, m.num_estacao, m.num_ato, m.status, m.classe_estacao,
  m.cod_tipo_antena, m.cod_equipamento_antena, m.cod_equipamento_transmissor,
  m.freq_tx_mhz, m.freq_rx_mhz, m.azimute, m.potencia_w, m.ganho_antena,
  m.altura_antena_m, m.data_licenciamento, m.data_primeiro_licenciamento,
  m.data_validade, m.angulo_elevacao_graus, m.angulo_meia_potencia_graus,
  m.frente_costa_db, m.tecnologia, m.designacao_emissao, m.polarizacao,
  m.geracao, m.largura_banda_khz, m.duplex_mhz, m.vencida, m.anos_licenciada,
  m.potencia_dbm, m.azimute_valido, m.elevacao_valida
FROM atual
JOIN ex ON ex.exemplo = atual.n
JOIN emissao m ON m.id = ex.id
ORDER BY m.id;

-- ===================== EXEMPLO 1 =====================
DELETE FROM atual; INSERT INTO atual VALUES (1);
.print ===================== EXEMPLO 1 =====================
.print --- Chave para localizar a linha no CSV
SELECT * FROM v_chave;
.print --- Linha esperada em estacao
SELECT * FROM v_estacao;
.print --- Linha esperada em emissao
SELECT * FROM v_emissao;

-- ===================== EXEMPLO 2 =====================
DELETE FROM atual; INSERT INTO atual VALUES (2);
.print
.print ===================== EXEMPLO 2 =====================
.print --- Chave para localizar a linha no CSV
SELECT * FROM v_chave;
.print --- Linha esperada em estacao
SELECT * FROM v_estacao;
.print --- Linha esperada em emissao
SELECT * FROM v_emissao;

-- ===================== EXEMPLO 3 =====================
DELETE FROM atual; INSERT INTO atual VALUES (3);
.print
.print ===================== EXEMPLO 3 =====================
.print --- Chaves para localizar as 2 linhas no CSV
SELECT * FROM v_chave;
.print --- Linha esperada em estacao
SELECT * FROM v_estacao;
.print --- Linhas esperadas em emissao
SELECT * FROM v_emissao;

.output stdout
