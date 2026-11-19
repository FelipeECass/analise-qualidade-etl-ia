# PLANO — ETL Anatel (Claude · Haiku · completo · r1)

Data de referência (Hoje): 2026-10-15.
Caminhos: Windows `D:\Projects\TCC` = `/workspace` neste ambiente. Entrada `files/<UF>.zip`; DDL `src/DDL.sql`; saída `db/claude_haiku_completo_r1.db`.

## 1. Compreensão da fonte

- Um CSV por ZIP (27 ZIPs, AC a TO). Nome interno não é usado; o único `.csv` do ZIP é lido.
- Separador `|`; todos os valores entre aspas duplas; `""` = vazio; aspas internas duplicadas.
- Quebra de linha CRLF (`\r\n`). Cabeçalho na primeira linha, 41 colunas, idêntico em todos os ZIPs.
- Verificado: em todas as linhas o número de campos (split simples por `|`) é 41; não há aspas escapadas nem `|` dentro de valores. Mesmo assim o leitor é um parser de estados que trata aspas e quebras de linha dentro de campo.
- Volume: ~7,76 milhões de linhas de dados (SP sozinho: 2,32 milhões; arquivo de 895 MB descomprimido).
- Colunas brutas (ordem): Status.state, NomeEntidade, NumFistel, NumServico, NumAto, NumEstacao, EnderecoEstacao, EndComplemento, SiglaUf, CodMunicipio, DesignacaoEmissao, Tecnologia, tipoTecnologia, meioAcesso, FreqTxMHz, FreqRxMHz, Azimute, CodTipoClasseEstacao, ClassInfraFisica, CompartilhamentoInfraFisica, CodTipoAntena, CodEquipamentoAntena, GanhoAntena, FrenteCostaAntena, AnguloMeiaPotenciaAntena, AnguloElevacao, Polarizacao, AlturaAntena, CodEquipamentoTransmissor, PotenciaTransmissorWatts, Latitude, Longitude, CodDebitoTFI, DataLicenciamento, DataPrimeiroLicenciamento, NumRede, _id, DataValidade, NumFistelAssociado, NomeEntidadeAssociado, Municipio.NomeMunicipio.
- Colunas localizadas pelo nome do cabeçalho (ausência de coluna obrigatória → erro, exit ≠ 0).

## 2. Mapeamento

Conforme a tabela da seção 3 da especificação. Estação: fistel, num_estacao, uf, cod_municipio, municipio, endereco, latitude, longitude, coordenada_valida, entidade, operadora. Emissão: todas as demais colunas do DDL. `status`, `classe_estacao`, `polarizacao`, `angulo_*`, `frente_costa_db`, `cod_*` vêm da linha de origem (sem transformação além da limpeza do tipo).

## 3. Formatos numéricos (observados na amostra)

Padrões encontrados nas colunas numéricas: `9`, `9.9`, `9,9` (decimal com vírgula), `.9`, `-.9`, `-9.9`, notação científica (`-9.9e-9`, 3 linhas na latitude), `NAN`/`nan`, `#N/A`, `n/a`, `não existe`, `[ ]`, texto com unidade (`40 W`), datas no lugar de números (`1900-03-04` em AZIMUTE), graus (`9°`), `9/9`, `9-9-9`, `14,5.0`, `9,9.9`.

Regra adotada: trim → vazio = NULL → troca `,` por `.` → exige exatamente uma ocorrência de separador decimal → aceita regex `^[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?$` → `double.Parse` invariant. Qualquer outro texto = NULL.
- `18.000` → 18; `55` = `55.0`; `1,5` → 1.5.
- `NAN`, `#N/A`, `não existe`, `40 W`, `9°`, `14,5.0` (dois separadores) → NULL.

## 4. Designação de emissão → largura de banda

- Janela = primeiros 4 caracteres em maiúsculas (ou menos, se a designação for mais curta).
- Deve haver exatamente uma letra de unidade (`H`, `K`, `M`, `G`) na janela, em posição ≥ 1.
- Antes da unidade: ≥ 1 dígito. Depois da unidade (dentro da janela): só dígitos; zero dígitos = fração 0.
- Valor = `inteiro.fração` (decimal, sem erro de ponto flutuante) × fator (H=0,001; K=1; M=1000; G=1.000.000).
- Exemplos: `5M00`→5000; `10M0`→10000; `200K`→200; `2M50`→2500; `25K0`→25; `56M0`→56000.
- Não casa: `TORR`, `99MO` (letra O), `99`, unidade duplicada, designação nula → NULL.

## 5. Validações de três estados

- `azimute_valido`: NULL se azimute NULL; 1 se 0 ≤ az ≤ 360; senão 0.
- `elevacao_valida`: NULL se ângulo de elevação NULL; 1 se −90 ≤ v ≤ 90; senão 0.
- `coordenada_valida` (estação, NOT NULL no DDL): 1 somente se lat e lon existem, não são ambas 0 e estão no retângulo (inclusive); caso contrário 0 (nunca NULL).
- `vencida` (NOT NULL): 1 se `data_validade` existe e < 2026-10-15; senão 0.

## 6. Casos de borda e decisões

| Caso | Decisão |
|---|---|
| Data com hora (`2019-04-10 07:29:59.060`, ~7 mil linhas) | Não é `yyyy-MM-dd` estrito → NULL (regra 1.4 literal). |
| Data válida fora de calendário (`2019-02-31`) | NULL (não é data válida). |
| `NumFistel`/`NumEstacao`/`SiglaUf` nulo após limpeza | Linha rejeitada. |
| `SiglaUf` | Maiúsculas; a chave da estação usa o valor já em maiúsculas. |
| `operadora` com `BRASIL TELECOM` | Tratado como expressão (substring) sobre entidade normalizada; demais regras de palavra usam limite de palavra (regex `\b`). Ex.: `NETBRASIL TELECOM` → OI. **Suposição.** |
| Entidade só com símbolos / vazia | Normalização → NULL → OUTRAS. |
| `Tecnologia` `0.0`, `DMR`, `TETRA`, `VHF`, `DIGITAL`, `WDCMA` | `tecnologia` guarda o texto em maiúsculas (inclusive `0.0`). geracao: `WDCMA`→3G; `DMR`, `0.0`, etc. → OUTRA. |
| `Tecnologia` vazia | NULL; geracao `N/D`. |
| `potencia_w` ≤ 0 ou nula | potencia_dbm NULL. |
| `duplex_mhz` | tx − rx em decimal, arredondado a 3 casas (away from zero). |
| `anos_licenciada` | (Hoje − primeiro licenciamento) dias ÷ 365,25, 1 casa, away from zero. |
| Estação repetida | Atributos da primeira linha (por ordem de arquivo AC→TO e ordem da linha). |
| Emissão duplicada | Chave: uf, fistel, num_estacao, num_ato, azimute, freq_tx, freq_rx, designacao, tecnologia (NULL = NULL; −0 normalizado para 0). Mantém a primeira. |
| Campos em número diferente do cabeçalho | Campo faltante = NULL. |
| Arredondamento | Feito em `decimal` (ex.: potencia_dbm: `10·log10(W·1000)` em double, convertido para decimal para arredondar 1 casa). |

## 7. Organização

- `Program.cs`: orquestra (localiza raiz, recria banco a partir do DDL, executa pipeline, código de saída).
- `Csv/CsvRecordReader.cs`: parser de estados `|` com aspas (streaming, sem carregar arquivo).
- `Transform/TextCleaner.cs`: trim, nulo, normalização de texto (acentos, maiúsculas, não-alfanumérico → espaço).
- `Transform/NumberParser.cs`, `DateParser.cs`: parse estrito.
- `Transform/Derived.cs`: operadora, geracao, largura_banda, duplex, potencia_dbm, vencida, anos, validações, coordenada.
- `Load/SqliteLoader.cs`: abertura, DDL, transações em lote, prepared statements, conjuntos de chaves (estação e deduplicação).
- `Pipeline/EtlRunner.cs`: itera ZIPs (ordem alfabética dos nomes), registra estatísticas.
Fluxo: ZIP → CsvRecordReader → Row (campos brutos por nome) → limpeza/transform → rejeição → estação (se nova) → dedupe emissão → INSERT em lote.
Dependência: Microsoft.Data.Sqlite (única). Sem EF.

## 8. Verificação

- Build Release sem erros/warnings relevantes.
- Esquema: `sqlite_master` e `PRAGMA table_info` igual ao DDL (tabelas `estacao` e `emissao` apenas, índices criados).
- Testes de unidade manuais de: normalização (`Telefônica  Brasil S.a.` → `TELEFONICA BRASIL S A`), designação (5M00G7W→5000, 10M0G7W→10000, 200KG7W→200, 2M50F9W→2500), números (`18.000`→18, `NAN`→NULL), datas estritas.
- Conferência dos 4 exemplos da especificação (estacao/emissao) contra o banco gerado.
- Consultas: sem `(uf,fistel,num_estacao)` órfão em emissao; sem duplicata na chave de emissão; `coordenada_valida` ∈ {0,1}; contagem de estações = distintos.
- Execução completa com todos os 27 ZIPs; exit 0; tempo medido.

## 9. Revisão final

Código revisado contra as seções 1 a 7 deste plano. Nenhum desvio encontrado.

Resultado da execução (`dotnet run ... --no-build`, exit 0, ~2 min 45 s):
- Linhas lidas: 7.760.505; rejeitadas: 0 (verificado no bruto: nenhuma linha sem NumFistel, NumEstacao ou SiglaUf).
- Estações: 904.265 (sem duplicata de chave); emissões: 7.468.991; duplicadas descartadas: 291.514.
- Reexecução final com os comandos oficiais (build Release: 0 avisos, 0 erros; run: exit 0, 2 min 46 s), mesmos números.
- Integridade (`pragma integrity_check` = ok; `foreign_key_check` vazio). Domínios: geracao ∈ {2G,3G,4G,5G,N/D,OUTRA}; operadora ∈ {VIVO,CLARO,TIM,OI,ALGAR,SERCOMTEL,BRISANET,OUTRAS}; flags ∈ {0,1,NULL}; datas todas `yyyy-MM-dd`; nenhum NaN/infinito.
- Esquema: texto de `sqlite_master` idêntico ao de `src/DDL.sql` (com CRLF), mesmas colunas, PK, FK e índices. Apenas `estacao` e `emissao` (+ índices do DDL e `sqlite_sequence`, criada pelo AUTOINCREMENT do próprio DDL).
- Órfãos de emissão: 0. Duplicatas na chave de emissão no banco: 0.
- Exemplos 1 a 4 da especificação conferidos campo a campo: 0 divergências. O Exemplo 4 mostra apenas uma das 4 linhas brutas da estação; as 4 emissões estão corretas e distintas (azimute/frequência diferentes).
- Designação: 10M0→10000, 200K→200, 2M50→2500, 56M0→56000, 25K0→25; `TORRE` e designações com letra fora de dígito/unidade → NULL.

Suposições adicionais adotadas (não cobertas ou ambíguas na especificação):
- Notação científica (`-9.9e-9`) é número finito e é aceita.
- Número com dois separadores (`14,5.0`, `1,234.5`) → NULL.
- Número com unidade (`40 W`) → NULL (texto que não é número).
- `DataValidade` com hora (`2019-04-10 07:29:59.060`) → NULL pela regra de data estrita.
- `BRASIL TELECOM` casa como substring (expressão), o que inclui `NETBRASIL TELECOM` → OI.
- Campo ausente numa linha com menos colunas que o cabeçalho → NULL.

Verificações não feitas: testes de unidade automatizados (a verificação foi feita sobre o banco gerado e os exemplos). O gabarito oficial não está disponível aqui, então a taxa de 98% não foi medida.
