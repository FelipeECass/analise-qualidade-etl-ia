# PLANO — ETL Anatel (claude_sonnet_completo_r1)

## 0. Premissas de ambiente
- A especificação usa `D:\Projects\TCC`. Neste ambiente (Linux) a mesma árvore está montada em `/workspace`.
  O programa resolve a raiz assim: variável `TCC_ROOT` → `D:\Projects\TCC` (se existir) → subindo a partir do
  diretório do executável/CWD até achar uma pasta com `files/` e `src/DDL.sql`. Nada fora dessa raiz é lido.
- Pasta `src/etl-base` não existe aqui e não foi consultada. Não existia projeto .NET vazio; criei um novo.
- Nome do projeto/banco: `claude_sonnet_completo_r1` → `db/claude_sonnet_completo_r1.db` (tudo em minúsculas, conforme a regra de nomes de arquivo/pasta).

## 1. Compreensão da fonte (amostra real: 27 ZIPs, 7.760.505 linhas de dados)
- 1 CSV por ZIP; UTF-8 **sem BOM**; quebra de linha **CRLF**; separador `|`; todos os valores entre `"`; `""` = aspas escapadas.
- Nenhum valor contém quebra de linha; todas as linhas têm 41 campos; cabeçalho idêntico nos 27 arquivos.
- Colunas: Status.state, NomeEntidade, NumFistel, NumServico, NumAto, NumEstacao, EnderecoEstacao, EndComplemento, SiglaUf,
  CodMunicipio, DesignacaoEmissao, Tecnologia, tipoTecnologia, meioAcesso, FreqTxMHz, FreqRxMHz, Azimute, CodTipoClasseEstacao,
  ClassInfraFisica, CompartilhamentoInfraFisica, CodTipoAntena, CodEquipamentoAntena, GanhoAntena, FrenteCostaAntena,
  AnguloMeiaPotenciaAntena, AnguloElevacao, Polarizacao, AlturaAntena, CodEquipamentoTransmissor, PotenciaTransmissorWatts,
  Latitude, Longitude, CodDebitoTFI, DataLicenciamento, DataPrimeiroLicenciamento, NumRede, _id, DataValidade,
  NumFistelAssociado, NomeEntidadeAssociado, Municipio.NomeMunicipio.
- Em cada arquivo `SiglaUf` == UF do nome do arquivo (verificado). Logo estações e duplicatas de emissão nunca cruzam arquivos
  → o estado de deduplicação pode ser zerado a cada arquivo (memória limitada).
- Colunas localizadas **pelo nome** no cabeçalho (comparação exata; `Municipio.NomeMunicipio`, `Status.state` com ponto).

## 2. Mapeamento
Conforme tabela da seção 3 da especificação (campo bruto → coluna destino). Resumo dos tratamentos:
texto limpo (trim, vazio→NULL): num_ato, cod_municipio, municipio, classe_estacao, cod_tipo_antena, cod_equipamento_*, status, fistel, num_estacao;
maiúsculas: uf, tecnologia, designacao_emissao, polarizacao; normalização (1.6): entidade, endereco; número: 11 colunas REAL; data: 3 colunas.
Derivadas: operadora/coordenada_valida (estacao); geracao, largura_banda_khz, duplex_mhz, potencia_dbm, vencida, anos_licenciada,
azimute_valido, elevacao_valida (emissao).

## 3. Separadores decimais (formatos observados)
`55`, `55.0`, `-.20`, `.810`, `0,5`, `205,79`, `18.000` (=18), exponencial raro (`-5.6e-05`, `-5.6E-5`).
Lixo observado: `NA`, `nan`, `NAN`, `-nan`, `#N/A`, `n/a`, `-`, `não existe`, `45 W`, `20W`, `133'312`, `14,70.0`, `--2.0`, `4/2`, `1.7/2.2`,
`1900-03-04`, `350°`, `&gt;17`, `0,,002`, `,`, `- 0,2`.
Regra: trim → vazio→NULL → trocar **`,` por `.`** (sem milhar) → aceitar só `[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?`
→ `double.Parse` (InvariantCulture) → se não finito, NULL. Todo o resto → NULL (ex.: `14,70.0` vira `14.70.0`, inválido).
Decisão: notação exponencial é aceita (é número finito); impacto estimado: 4 valores em 7,7 mi linhas.

## 4. Designação de emissão → largura_banda_khz
Designação em maiúsculas; precisa ter ≥ 4 caracteres, senão NULL. Dos 4 primeiros: exatamente uma letra em {H,K,M,G}, em posição 1..3;
as demais posições são dígitos ASCII `0-9`. parte inteira = dígitos antes da letra (≥ 1), fração = dígitos depois (pode ser vazia → 0).
valor = `int.frac` × fator (H 0,001; K 1; M 1000; G 1.000.000), calculado em `decimal` (sem ruído binário) e convertido a double.
Ex.: `5M00G7W`→5000; `10M0G7W`→10000; `200KG7W`→200; `2M50F9W`→2500; `1G60D7W`→1.600.000; `TORR`/`""`/`ABCD`→NULL.
Caso `1M5K...` (2 letras de unidade nos 4 primeiros) → inválido (NULL).

## 5. Validações de três estados
- azimute_valido: azimute NULL → NULL; 0 ≤ az ≤ 360 → 1; senão 0.
- elevacao_valida: ângulo NULL → NULL; −90 ≤ v ≤ 90 → 1; senão 0.
(`vencida`, `coordenada_valida` e `geracao` são NOT NULL, dois estados.)

## 6. Casos de borda e suposições
| Tema | Decisão |
|---|---|
| Trim | `string.Trim()` (whitespace Unicode, inclui NBSP) em todo valor. Espaço interno é preservado. |
| Rejeição | só se NumFistel, NumEstacao ou SiglaUf for NULL após limpeza. Linha com nº de campos ≠ cabeçalho: não ocorre; se ocorrer, campos faltantes tratados como vazio. |
| Datas | regex estrita `^\d{4}-\d{2}-\d{2}$` **e** data de calendário válida (`DateOnly.TryParseExact`). `2031-09-08 00:00:00.000`, `44509`, `-`, `1900-03-04.0` → NULL. |
| Texto "NA" etc. em colunas de texto | NÃO viram NULL (só números); ex.: `Tecnologia=NA` → tecnologia `NA`, geracao `OUTRA`. |
| Tecnologia | maiúsculas; geracao pela tabela da especificação; `0.0`, `5G`, `DMR`, `LTE (TDD)` → `OUTRA`; NULL → `N/D`. Comparação após maiúsculas (e trim já feito). |
| Normalização de texto | NFD → descarta `NonSpacingMark` → `ToUpperInvariant` → não `char.IsLetterOrDigit` vira espaço → colapsa/trim; vazio→NULL. `º`/`ª` contam como letras (categoria Lo). |
| Operadora | tokens separados por espaço (equivale a `\b` pois só restam letras/dígitos/espaço); regras 1–7 em ordem; `TIM`/`OI` só como 1º token; `BRASIL TELECOM` = dois tokens consecutivos. |
| UF | maiúsculas; a chave de estação usa a UF já em maiúsculas. |
| coordenada_valida | lat e lon não nulos, não (0 e 0), lat ∈ [−33,75; 5,28], lon ∈ [−73,99; −28,83]. Calculada sobre a primeira linha da estação. |
| Estação | primeira linha aceita da chave (uf,fistel,num_estacao) fornece cod_municipio, municipio, endereco, latitude, longitude, coordenada_valida, entidade, operadora — mesmo que esses campos sejam NULL nela. |
| Duplicata de emissão | chave = (uf,fistel,num_estacao,num_ato,azimute,tx,rx,designacao,tecnologia) com valores tratados, NULL==NULL; ordem preservada; mantém a 1ª. Comparação numérica por bits do double (−0.0 normalizado para 0.0). Linhas duplicadas não criam nova estação nem emissão. |
| Arredondamento | meio longe de zero: `(decimal)valor` (15 dígitos significativos, elimina ruído binário) + `Math.Round(..., MidpointRounding.AwayFromZero)`; fallback em double se |v| > 1e15. |
| duplex_mhz | tx − rx (double) arredondado a 3 casas; NULL se faltar um. |
| potencia_dbm | `10*log10(w*1000)`, 1 casa; NULL se w NULL ou ≤ 0. |
| vencida | `data_validade` não nula e `< 2026-10-15` → 1; senão 0. |
| anos_licenciada | `(2026-10-15 − primeiro_lic).Days / 365.25`, 1 casa; pode ser negativa se a data for futura (sem tratamento especial); NULL se data faltar. |
| Gravação | datas `yyyy-MM-dd`; lógicos 1/0/NULL; REAL como double (ponto, ida e volta "R"); texto como veio. |
| Ordem | arquivos em ordem alfabética (AC…TO); linhas na ordem do arquivo; `emissao.id` sequencial nessa ordem. |

## 7. Organização da solução
- `Program.cs` — entrada, resolução de caminhos, tratamento de exceção (stderr + exit code 1), orquestração.
- `PathResolver` — raiz do projeto, entradas, saída.
- `CsvReader` (PipeCsvReader) — leitor de streaming, aspas duplicadas, CRLF/LF, sem alocar linha inteira (7,7 mi linhas).
- `Cleaner` — regras da seção 1 (trim, número, data, normalização de texto).
- `Derivations` — operadora, geracao, largura_banda, arredondamento, potência, validações.
- `EtlProcessor` — lê ZIP a ZIP, rejeita, deduplica, monta registros.
- `SqliteLoader` — cria o schema a partir do DDL.sql (sem alterações), grava em transações em lote com comandos preparados.
Justificativa: separação das regras puras (testáveis) do I/O; dedup por arquivo (UF) limita memória; stream de ZIP evita extrair em disco.
Ordem de carga: estação antes da emissão para respeitar FK (FK só é verificada se `PRAGMA foreign_keys=ON`; deixo ligado e a estação é inserida antes).
Desempenho: `journal_mode=OFF`/`synchronous=OFF` e uma transação grande por arquivo durante a carga (banco novo, recriável).

## 8. Verificação (antes de concluir)
1. Build Release sem erros; run com `--no-build` exit 0 em tempo bem inferior a 2 h.
2. Esquema: `sqlite_master` do banco gerado idêntico ao DDL.sql (apenas as tabelas estacao e emissao + 2 índices + sqlite_sequence do AUTOINCREMENT).
3. Os 4 exemplos da especificação: rodar a transformação sobre as linhas brutas de exemplo (RR/AC) e comparar com os valores esperados
   (consulta no banco por fistel/num_estacao).
4. Invariantes por SQL: `emissao` sem órfãs (FK); nenhuma chave de estação duplicada; nenhuma duplicata pela chave de emissão;
   contagens: linhas lidas = rejeitadas + estações-novas... (linhas lidas = rejeitadas + duplicadas + emissões); `geracao`/`operadora` sem NULL;
   `vencida`, `coordenada_valida` ∈ {0,1}; sem datas fora de `yyyy-MM-dd`.
5. Revisão final do código contra este plano.

## 9. Revisão final (código × plano) e resultados da verificação
- Ajuste em relação ao plano: `InvariantGlobalization` removido, pois no modo invariante `Normalize(FormD)` não decompõe acentos
  (endereços saíam com `Á`). Além disso há fallback `AccentTable` (tabela NFD gerada de unicodedata) caso o runtime/ICU não normalize;
  rodadas completas com e sem `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` geraram bancos byte a byte idênticos.
- Carga completa: 7.760.505 linhas lidas, 0 rejeitadas, 291.514 emissões duplicadas descartadas, 904.265 estações, 7.468.991 emissões; ~2 min.
- Esquema: apenas `estacao`, `emissao` (+ índices e `sqlite_sequence`), criados executando `DDL.sql` sem alterações.
- Invariantes SQL: `PRAGMA foreign_key_check` vazio; zero duplicatas pela chave de emissão; datas só `yyyy-MM-dd`; `geracao`/`operadora` sem NULL.
- Exemplos 1–4 da especificação conferidos no banco (valores idênticos aos esperados, inclusive `anos_licenciada`, `potencia_dbm`, `duplex_mhz`).
- Verificação cruzada: reimplementação independente em Python (Decimal HALF_UP, regex) comparada coluna a coluna para RR, AC, TO, PI, SE → 0 diferenças.
- Suposições registradas que podem divergir de um gabarito: `º`/`ª` mantidos como letras na normalização; notação exponencial aceita como número.
