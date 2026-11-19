# PLANO — ETL Anatel (claude_opus_completo_r1)

## 1. Compreensão da fonte

- 27 ZIPs `<UF>.zip` (AC…TO) em `files/`, cada um com um único CSV (`csv_licenciamento_<hash>.csv`; o programa pega a única entrada `.csv` do ZIP, sem depender do nome).
- Encoding UTF-8 (sem BOM; o leitor aceita BOM se houver). Quebra de linha CRLF.
- Separador `|`; todo valor entre aspas duplas; vazio = `""`; aspas internas duplicadas (`""`).
  Na amostra não há `|`, aspas nem quebras de linha dentro de valores, mas o parser é um
  autômato completo de CSV (trata aspas escapadas e quebras dentro de aspas).
- Total: 7.760.505 linhas de dados, todas com 41 campos; ~904 mil estações distintas.
- Colunas do bruto (41): `Status.state, NomeEntidade, NumFistel, NumServico, NumAto, NumEstacao,
  EnderecoEstacao, EndComplemento, SiglaUf, CodMunicipio, DesignacaoEmissao, Tecnologia,
  tipoTecnologia, meioAcesso, FreqTxMHz, FreqRxMHz, Azimute, CodTipoClasseEstacao, ClassInfraFisica,
  CompartilhamentoInfraFisica, CodTipoAntena, CodEquipamentoAntena, GanhoAntena, FrenteCostaAntena,
  AnguloMeiaPotenciaAntena, AnguloElevacao, Polarizacao, AlturaAntena, CodEquipamentoTransmissor,
  PotenciaTransmissorWatts, Latitude, Longitude, CodDebitoTFI, DataLicenciamento,
  DataPrimeiroLicenciamento, NumRede, _id, DataValidade, NumFistelAssociado, NomeEntidadeAssociado,
  Municipio.NomeMunicipio`.
- Colunas localizadas pelo nome no cabeçalho (falha com erro se faltar alguma usada).

## 2. Mapeamento

Limpeza base de todo valor: `Trim()` (espaços Unicode, inclusive NBSP); vazio → NULL.

estacao:
| coluna | origem | tratamento |
|---|---|---|
| fistel | NumFistel | texto limpo |
| num_estacao | NumEstacao | texto limpo |
| uf | SiglaUf | maiúsculas |
| cod_municipio | CodMunicipio | texto limpo |
| municipio | Municipio.NomeMunicipio | texto limpo |
| endereco | EnderecoEstacao | normalização 1.6 |
| latitude / longitude | Latitude / Longitude | número |
| coordenada_valida | derivada | ver §4 |
| entidade | NomeEntidade | normalização 1.6 |
| operadora | derivada de entidade | regras regex |

emissao: uf, fistel, num_estacao (como acima); num_ato←NumAto (texto); tecnologia←Tecnologia (maiúsc.);
geracao (derivada); designacao_emissao←DesignacaoEmissao (maiúsc.); largura_banda_khz (derivada);
freq_tx_mhz←FreqTxMHz, freq_rx_mhz←FreqRxMHz (número); duplex_mhz (derivada); azimute←Azimute;
potencia_w←PotenciaTransmissorWatts; ganho_antena←GanhoAntena; altura_antena_m←AlturaAntena (números);
data_licenciamento, data_primeiro_licenciamento, data_validade (datas); vencida, anos_licenciada
(derivadas); status←Status.state; classe_estacao←CodTipoClasseEstacao (texto); polarizacao (maiúsc.);
angulo_elevacao_graus←AnguloElevacao; angulo_meia_potencia_graus←AnguloMeiaPotenciaAntena;
frente_costa_db←FrenteCostaAntena (números); cod_tipo_antena, cod_equipamento_antena,
cod_equipamento_transmissor (texto); potencia_dbm, azimute_valido, elevacao_valida (derivadas).

"Maiúsculas" = `ToUpperInvariant()` após o trim (acentos mantidos, ex. `ANALÓGICO`).

## 3. Separadores decimais

Formatos vistos: `123`, `12.5`, `12,5`, `.5`, `-.5`, `-0.12`, `1.2E-5`, `18.000`, e lixo:
`1,2.3`, `10 W`, `3'5`, `3´5`, `--9.9`, `- 9,9`, `nan`, `NAN`, `-nan`, `#N/A`, `n/a`, `não existe`,
`1-2-3`, `5°`, `9.O`, `&gt;3`, `,`.

Regra: trocar `,` por `.`; aceitar somente o padrão `^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$`;
converter com `double.Parse(InvariantCulture)`; resultado não finito → NULL. Portanto um único
separador decimal; dois separadores (`1,2.3`), espaços internos, unidades, NaN e infinito → NULL.
`18.000` → 18. Notação científica é aceita (é número finito).

## 4. Designação de emissão → largura_banda_khz

`d = designacao (maiúsc.)`; `p = d[0..min(4,len)]`. Válido se `p` tem exatamente uma letra
de `{H,K,M,G}`, que não está na posição 0, e todos os outros caracteres são dígitos.
Valor = `decimal("inteiro.fração")` × fator (H 0,001; K 1; M 1000; G 1.000.000), fração vazia = 0.
Ex.: `5M00`→5000, `10M0`→10000, `200K`→200, `2M50`→2500, `100H`→0,1, `1G60`→1.600.000.
Inválidos: `12KF` (F não é dígito), `10MO`, `TORR`, nulo. Cálculo em `decimal` para evitar
erro binário (ex. 2.5×1000).

## 5. Validações de três estados

- azimute_valido: NULL se azimute NULL; 1 se 0 ≤ az ≤ 360; senão 0.
- elevacao_valida: NULL se angulo_elevacao_graus NULL; 1 se −90 ≤ v ≤ 90; senão 0.
- (dois estados) vencida: 1 se data_validade não nula e < 2026-10-15; senão 0.
- (dois estados) coordenada_valida: 1 se lat e lon não nulas, não (lat=0 e lon=0),
  −33,75 ≤ lat ≤ 5,28 e −73,99 ≤ lon ≤ −28,83; senão 0.

## 6. Casos de borda e decisões

- Texto: valor só com espaços (inclusive NBSP) → NULL. Trim usa `string.Trim()` (Unicode).
- Normalização 1.6: NFD → remove categoria `NonSpacingMark` (e SpacingCombining/Enclosing) →
  `ToUpperInvariant` → todo caractere que não seja letra/dígito Unicode (`Rune.IsLetterOrDigit`)
  vira espaço → colapsa espaços → trim → vazio vira NULL. Suposição: `º`/`ª` são letras Unicode
  (categoria Lo) e portanto são mantidos (interpretação literal de "letra ou dígito"); `³`
  (categoria No, não é dígito decimal) vira espaço. Mojibake (`Ã§`) é mantido como letras
  (Ã→A após remover til), sem tentativa de reparo.
- Datas: `DateOnly.TryParseExact(v,"yyyy-MM-dd",Invariant)` — `2031-09-08 00:00:00.000`, `-`,
  `NA`, `----`, datas inexistentes (`2023-02-30`) → NULL.
- Códigos: mantidos como texto (zeros à esquerda preservados). `NumRede`, `_id` etc. não importados.
- Tecnologia → geracao: comparação após trim e maiúsculas, igualdade exata: `GSM`,`CDMA`→2G;
  `WCDMA`,`WDCMA`→3G; `LTE`→4G; `NR`→5G; NULL→`N/D`; qualquer outro (`LTE (TDD)`, `5G`, `0.0`,
  `DMR`…)→`OUTRA`.
- Operadora: regex com `\b` sobre a entidade normalizada, em ordem: `\b(TELEFONICA|VIVO)\b`→VIVO;
  `\b(CLARO|EMBRATEL)\b`→CLARO; `^TIM\b`→TIM; `^OI\b|\bTELEMAR\b|\bBRASIL TELECOM\b`→OI;
  `\bALGAR\b`; `\bSERCOMTEL\b`; `\bBRISANET\b`; senão/NULL → OUTRAS.
- Arredondamento: converter o double para `decimal` e `Math.Round(..., AwayFromZero)`; a conversão
  double→decimal usa 15 dígitos significativos, eliminando ruído binário (2.25 → 2.3, −2.25 → −2.3).
- duplex: `round(tx − rx, 3)` (calculado em decimal), NULL se faltar um.
- potencia_dbm: NULL se potência NULL ou ≤ 0; senão `round(10·log10(w·1000), 1)`.
- anos_licenciada: `round((2026-10-15 − data).Days / 365.25, 1)`; pode ser negativo para data futura
  (a especificação não proíbe; mantido).
- Rejeição: só se NumFistel, NumEstacao ou SiglaUf nulo após limpeza (na base atual: 0 linhas).
- Linha com número de campos diferente do cabeçalho (não ocorre): campos ausentes tratados como
  vazios, extras ignorados.
- Estação: atributos da primeira ocorrência da chave (uf, fistel, num_estacao), na ordem de
  processamento: arquivos em ordem alfabética de UF (AC→TO), linhas na ordem do arquivo.
- Emissão duplicada: chave (uf, fistel, num_estacao, num_ato, azimute, freq_tx, freq_rx,
  designacao, tecnologia) com valores já tratados; números comparados pelo valor (55 = 55.0),
  NULL = NULL (representado por marcador distinto de string vazia). A chave é serializada e
  reduzida a hash XxHash128 (128 bits; colisão desprezível) para caber em memória.
- Suposição de caminhos: a especificação usa `D:\Projects\TCC`. O programa localiza a raiz
  subindo a partir do diretório atual e do diretório do executável até achar uma pasta com
  `files/` e `src/DDL.sql`; se não achar, usa `D:\Projects\TCC`. Assim funciona no Windows e
  neste ambiente (onde a raiz é `/workspace`).
- Banco: nome `claude_opus_completo_r1.db` (minúsculas, regra de nomes). Se já existir, é apagado.
  DDL.sql é executado literalmente.

## 7. Organização da solução

Projeto console único (`claude_opus_completo_r1.csproj`, net10.0), dependências
`Microsoft.Data.Sqlite` e `System.IO.Hashing`. Arquivos:

- `Program.cs` — orquestração, localização de caminhos, códigos de saída e erros em stderr.
- `CsvReader.cs` — parser de CSV `|`/aspas em streaming (sem carregar o arquivo em memória).
- `Cleaning.cs` — funções puras: texto, maiúsculas, número, data, normalização.
- `Derivations.cs` — funções puras: operadora, geração, largura de banda, duplex, dBm, etc.
- `Records.cs` — registros `Estacao` e `Emissao`.
- `Transformer.cs` — linha bruta → (Estacao, Emissao) usando o mapa de colunas do cabeçalho.
- `SqliteLoader.cs` — cria o banco com DDL.sql, comandos preparados, transação única.

Fluxo: ZIP → stream do CSV → parser → Transformer → dedup (HashSet de estações / hash de
emissões) → loader. Justificativa: separação entre parsing, regras puras (testáveis com os
exemplos) e carga; streaming mantém memória baixa; comandos preparados + uma transação +
`journal_mode=OFF`/`synchronous=OFF` dão desempenho (banco é recriado do zero a cada execução).

## 8. Verificação

- Autoteste embutido (`--selftest`) com as 4 linhas de exemplo da especificação, comparando
  campo a campo com os valores esperados; e casos unitários de número/data/designação/arredondamento.
- Executar build Release e run sem argumentos; checar código de saída 0.
- Conferir no banco: apenas tabelas `estacao`/`emissao` (+ `sqlite_sequence` criada pelo
  AUTOINCREMENT do próprio DDL), esquema idêntico ao DDL (`sqlite_master`), sem violação de PK,
  `PRAGMA foreign_key_check` vazio, contagens (linhas lidas, rejeitadas, duplicadas, carregadas),
  distribuição de geracao/operadora, ausência de duplicatas pela chave, consultar as linhas dos
  exemplos no banco.
- Conferência independente em Python de algumas regras (contagem de emissões únicas).

## 9. Revisão

Ao final, revisar o código contra este plano (seção "Revisão final" abaixo).

## Revisão final (executada)

- Build Release sem erros/avisos (`Microsoft.Data.Sqlite` 10.0.15 — a 10.0.0 trazia SQLitePCLRaw com
  vulnerabilidade conhecida). Execução sem argumentos: código 0, ~2 min.
- `--selftest`: 4 exemplos da especificação idênticos campo a campo + 29 casos unitários: OK.
- Resultado: 7.760.505 linhas lidas; 0 rejeitadas; 291.514 emissões duplicadas descartadas;
  904.265 estações; 7.468.991 emissões.
- Esquema: `sqlite_master` contém exatamente o texto do DDL.sql para as 2 tabelas e 2 índices
  (além de `sqlite_sequence`, criada automaticamente pelo SQLite por causa do AUTOINCREMENT do DDL).
- `PRAGMA foreign_key_check` vazio; nenhuma estação sem emissão; nenhuma duplicata pela chave §6.
- Conferência independente (script Python separado) reproduziu as contagens de estações e emissões.
- Código revisado contra §2–§7: mapeamento, limpeza numérica (regex + finito), datas estritas,
  normalização (NFD + marcas + IsLetterOrDigit), regras de operadora/geração/largura/dBm/duplex/
  anos/vencida/coordenada, validações de três estados, deduplicação, ordem de processamento,
  gravação de datas como texto e lógicos como 1/0 — conformes.
