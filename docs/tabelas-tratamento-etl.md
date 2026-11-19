# Tabelas de tratamento do ETL — dados de licenciamento da Anatel

Documento de apoio para o TCC. Descreve, para cada coluna das tabelas finais do banco, o campo bruto
de origem e o tratamento aplicado.

## Contexto

- **Fonte dos dados:** sistema Spectrum-E da Anatel (https://sistemas.anatel.gov.br/se/public/view/b/licenciamento.php),
  aba *Licenciadas*, baixado por unidade da federação.
- **Arquivos de entrada:** 27 arquivos ZIP (um por UF, de AC a TO), cada um com um CSV de 41 colunas,
  separador `|` e valores entre aspas. Cada linha é uma emissão de uma estação (setor × frequência).
- **Tecnologia do ETL:** .NET 10 (C#), acesso a dados com Dapper, banco SQLite.
- **Data de referência da execução:** 15/10/2026 (afeta as colunas `vencida` e `anos_licenciada`).
- **Exemplos:** todos os exemplos "bruto → final" usam a mesma linha real do arquivo do AC
  (Telefônica, Rio Branco).

## Legenda do tratamento

| Sigla | Tratamento | Significado |
|---|---|---|
| **L** | Limpeza | Remove espaços nas extremidades; valor vazio vira `NULL`; o conteúdo não muda. Códigos e identificadores permanecem texto. |
| **T** | Tipagem | Converte texto em número ou data, sem mudar o significado. Números aceitam `.` ou `,` e valores não numéricos viram `NULL`; datas seguem o formato `yyyy-MM-dd`. |
| **N** | Normalização | Padroniza texto: maiúsculas; nos nomes e endereços, também remove acentos e troca pontuação por um espaço único. |
| **D** | Derivada | Coluna nova, calculada a partir de um ou mais campos brutos. |
| **V** | Validação | Coluna lógica com três estados: 1 (válido), 0 (inválido) e `NULL` (sem dado suficiente ou regra não aplicável). A linha nunca é descartada por falhar uma validação. |
| **G** | Gerada | Valor criado pelo banco, sem campo bruto correspondente. |

## 1. Visão geral

| Item | Quantidade |
|---|---|
| Colunas do arquivo bruto | 41 |
| Colunas importadas | 30 |
| Colunas descartadas | 11 |
| Tabelas finais | 2 (`estacao` com 11 colunas e `emissao` com 35) |
| Colunas derivadas ou validadas (novas) | 12 |

## 2. Tabela final `estacao` (uma linha por torre)

Os atributos vêm da primeira linha do arquivo em que a estação aparece. A chave primária é
(`uf`, `fistel`, `num_estacao`). A UF faz parte da chave porque a mesma combinação
(`fistel`, `num_estacao`) existe em UFs diferentes (210 casos nos 27 arquivos).

| Coluna final | Campo bruto de origem | Trat. | O que foi feito | Bruto → final |
|---|---|---|---|---|
| `uf` | `SiglaUf` | N | Converte para maiúsculas; linha rejeitada se vazio | `AC` → `AC` |
| `fistel` | `NumFistel` | L | Mantém como texto; linha rejeitada se vazio | `50409146366` → `50409146366` |
| `num_estacao` | `NumEstacao` | L | Mantém como texto; linha rejeitada se vazio | `64300` → `64300` |
| `cod_municipio` | `CodMunicipio` | L | Mantém como texto (preserva zeros à esquerda) | `1200401` → `1200401` |
| `municipio` | `Municipio.NomeMunicipio` | L | Remove espaços; vazio → `NULL` | `Rio Branco` → `Rio Branco` |
| `endereco` | `EnderecoEstacao` | N | Maiúsculas, sem acento, pontuação → espaço único | `RUA FLORIANO PEIXOTO  358` → `RUA FLORIANO PEIXOTO 358` |
| `latitude` | `Latitude` | T | Texto → número decimal | `"-9.972025"` → `-9.972025` |
| `longitude` | `Longitude` | T | Texto → número decimal | `"-67.813556"` → `-67.813556` |
| `coordenada_valida` | `Latitude`, `Longitude` | V | 1 se existem, não são (0,0) e estão dentro do retângulo do Brasil (latitude −33,75 a 5,28; longitude −73,99 a −28,83) | `-9.97, -67.81` → `1` |
| `entidade` | `NomeEntidade` | N | Maiúsculas, sem acento, pontuação → espaço único | `Telefonica Brasil S.a.` → `TELEFONICA BRASIL S A` |
| `operadora` | `NomeEntidade` | D | Classifica a entidade normalizada por regras (VIVO, CLARO, TIM, OI, ALGAR, SERCOMTEL, BRISANET; senão OUTRAS) | `TELEFONICA BRASIL S A` → `VIVO` |

## 3. Tabela final `emissao` (uma linha por setor × frequência)

| Coluna final | Campo bruto de origem | Trat. | O que foi feito | Bruto → final |
|---|---|---|---|---|
| `id` | (não existe no bruto) | G | Número sequencial gerado pelo banco | — → `1`, `2`, … |
| `uf` | `SiglaUf` | N | Maiúsculas; liga à estação | `AC` → `AC` |
| `fistel` | `NumFistel` | L | Texto limpo; liga à estação | `50409146366` |
| `num_estacao` | `NumEstacao` | L | Texto limpo; liga à estação | `64300` |
| `num_ato` | `NumAto` | L | Texto limpo | `155832022` |
| `tecnologia` | `Tecnologia` | N | Maiúsculas | `WCDMA` → `WCDMA` |
| `geracao` | `Tecnologia` | D | GSM, CDMA → 2G · WCDMA, WDCMA → 3G · LTE → 4G · NR → 5G · vazio → N/D · outros → OUTRA | `WCDMA` → `3G` |
| `designacao_emissao` | `DesignacaoEmissao` | N | Maiúsculas | `5M00G9W` → `5M00G9W` |
| `largura_banda_khz` | `DesignacaoEmissao` | D | Decodifica os 4 primeiros caracteres (a letra da unidade ocupa o lugar da vírgula) e converte para kHz | `5M00G9W` → `5000` |
| `freq_tx_mhz` | `FreqTxMHz` | T | Texto → número | `"874.5"` → `874.5` |
| `freq_rx_mhz` | `FreqRxMHz` | T | Texto → número | `"829.5"` → `829.5` |
| `duplex_mhz` | `FreqTxMHz`, `FreqRxMHz` | D | `Tx − Rx`, com sinal | `874.5 − 829.5` → `45` |
| `azimute` | `Azimute` | T | Texto → número | `"40"` → `40` |
| `potencia_w` | `PotenciaTransmissorWatts` | T | Texto → número | `"40"` → `40` |
| `ganho_antena` | `GanhoAntena` | T | Texto → número | `"11.2"` → `11.2` |
| `altura_antena_m` | `AlturaAntena` | T | Texto → número | `"35"` → `35` |
| `data_licenciamento` | `DataLicenciamento` | T | Texto → data (`yyyy-MM-dd`) | `2025-03-25` |
| `data_primeiro_licenciamento` | `DataPrimeiroLicenciamento` | T | Texto → data | `2000-11-27` |
| `data_validade` | `DataValidade` | T | Texto → data | `2028-11-29` |
| `vencida` | `DataValidade` | D | 1 se a validade é anterior à data de execução | `2028-11-29` → `0` |
| `anos_licenciada` | `DataPrimeiroLicenciamento` | D | Anos entre o primeiro licenciamento e a data de execução | `2000-11-27` → `25.9` |
| `status` | `Status.state` | L | Texto limpo | `LIC-LIC-01` |
| `classe_estacao` | `CodTipoClasseEstacao` | L | Texto limpo | `FB` |
| `polarizacao` | `Polarizacao` | N | Maiúsculas | `X` → `X` |
| `angulo_elevacao_graus` | `AnguloElevacao` | T | Texto → número | `"0"` → `0` |
| `angulo_meia_potencia_graus` | `AnguloMeiaPotenciaAntena` | T | Texto → número | `"65"` → `65` |
| `frente_costa_db` | `FrenteCostaAntena` | T | Texto → número | `"23"` → `23` |
| `cod_tipo_antena` | `CodTipoAntena` | L | Texto limpo | `760` |
| `cod_equipamento_antena` | `CodEquipamentoAntena` | L | Texto limpo | `005200301293` |
| `cod_equipamento_transmissor` | `CodEquipamentoTransmissor` | L | Texto limpo | `019980701882` |
| `potencia_dbm` | `PotenciaTransmissorWatts` | D | `10·log10(W·1000)`, 1 casa decimal | `40` W → `46.0` |
| `azimute_valido` | `Azimute` | V | 1 se 0 ≤ azimute ≤ 360; `NULL` se não houver azimute | `40` → `1` |
| `elevacao_valida` | `AnguloElevacao` | V | 1 se −90 ≤ ângulo ≤ 90; `NULL` se não houver | `0` → `1` |

## 4. Resumo por tipo de tratamento

| Tratamento | `estacao` | `emissao` | Total |
|---|--:|--:|--:|
| L, limpeza | 4 | 8 | 12 |
| N, normalização (maiúsculas ou texto) | 3 | 4 | 7 |
| T, tipagem | 2 | 12 | 14 |
| D, derivada | 1 | 7 | 8 |
| V, validação | 1 | 3 | 4 |
| G, gerada | 0 | 1 | 1 |
| **Total de colunas** | **11** | **35** | **46** |

## 5. Campos brutos descartados (11)

| Campo bruto | Motivo |
|---|---|
| `NumServico` | Fora do escopo da análise |
| `EndComplemento` | Fora do escopo; endereço principal já importado |
| `tipoTecnologia` | Significado não confirmado em dicionário oficial |
| `meioAcesso` | Significado não confirmado em dicionário oficial |
| `ClassInfraFisica` | Fora do escopo (infraestrutura compartilhada) |
| `CompartilhamentoInfraFisica` | Fora do escopo (infraestrutura compartilhada) |
| `CodDebitoTFI` | Informação de cobrança de taxa, fora do escopo |
| `NumRede` | Chave composta (`_freq_ato_tecnologia_azimute_município_estação`), não é identificador de rede |
| `_id` | Identificador técnico da fonte |
| `NumFistelAssociado` | Fora do escopo |
| `NomeEntidadeAssociado` | Fora do escopo |

## 6. Efeito do tratamento nos 27 estados

| Etapa | Resultado |
|---|--:|
| Linhas lidas dos 27 arquivos | 7.760.505 |
| Linhas rejeitadas (sem chave) | 0 |
| Emissões duplicadas removidas | 291.514 |
| **Emissões carregadas** | **7.468.991** |
| **Estações carregadas** | **904.265** |
| Estações com coordenada inválida | 3.127 |
| Emissões com licença vencida (em 15/10/2026) | 37.481 |
| Emissões com azimute inválido | 419 |
| Emissões com elevação inválida | 59 |
| Emissões com duplex incoerente | 16.189 |

Identidade de conferência: 7.760.505 = 0 + 291.514 + 7.468.991.

### Distribuição de `geracao` (emissões)

| `geracao` | Emissões |
|---|--:|
| N/D | 5.165.615 |
| 4G | 1.258.644 |
| 2G | 378.791 |
| 3G | 378.665 |
| 5G | 211.195 |
| OUTRA | 76.081 |

### Distribuição de `operadora` (estações)

| `operadora` | Estações |
|---|--:|
| OUTRAS | 718.580 |
| VIVO | 67.533 |
| TIM | 59.950 |
| CLARO | 52.026 |
| BRISANET | 3.860 |
| ALGAR | 2.258 |
| SERCOMTEL | 53 |
| OI | 5 |

## 7. Limitações a declarar no texto

- **A maior parte da base não é telefonia celular.** 69% das emissões não têm tecnologia
  (`geracao = N/D`) e 66% estão abaixo de 700 MHz: rádio privado de órgãos públicos, energia e
  indústria. Por isso 79,5% das estações ficam como `OUTRAS`.
- **A validação de duplex cobre só 29,6% das emissões.** Ela se aplica apenas a 2G a 5G em faixas
  conhecidas; as demais ficam `NULL`.
- **`anos_licenciada` tem valores negativos** (mínimo de −15 anos): há datas de primeiro
  licenciamento no futuro. Não foram tratadas.
- **`polarizacao` tem a variante `VERTICAL`** (956 linhas) ao lado de `V`; a normalização por
  maiúsculas não as unifica.
- **`vencida` e `anos_licenciada` dependem da data de execução** (aqui, 15/10/2026).
- **Unidades não confirmadas em dicionário oficial:** graus (ângulos) e dB (frente-costa); o ganho
  da antena pode ser dBi ou dBd.
- **Valores sentinela** (`999.9`, `666.6`, `99.9`) aparecem em azimute e elevação e são apenas
  sinalizados como inválidos.
- **Coordenada válida** é checada contra o retângulo do Brasil; não verifica se o ponto está dentro
  do município ou do estado informado.
