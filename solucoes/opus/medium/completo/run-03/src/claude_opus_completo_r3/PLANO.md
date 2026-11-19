# PLANO — ETL Anatel (claude_opus_completo_r3)

Projeto: `src/claude_opus_completo_r3` · Saída: `db/claude_opus_completo_r3.db` · Hoje = 2026-10-15.

## 1. Compreensão da fonte

Examinados os 27 ZIPs (`files/AC.zip` … `files/TO.zip`), descompactados para perfilamento:

- Cada ZIP contém exatamente 1 entrada `csv_licenciamento_<hash>.csv` (o programa pega a primeira entrada `.csv`, sem depender do nome).
- Encoding UTF-8 **sem BOM** (o leitor tolera BOM mesmo assim). Quebra de linha **CRLF**.
- Separador `|`; todos os valores entre aspas duplas; vazio = `""`; aspas internas duplicadas.
  Na amostra completa (7.760.505 linhas de dados) não há quebras de linha nem aspas dentro de valores e todas as linhas têm 41 campos; mesmo assim o parser é uma máquina de estados que trata `""`, `|` dentro de aspas e registros multi-linha.
- Cabeçalho idêntico em todos os arquivos (41 colunas):
  `Status.state, NomeEntidade, NumFistel, NumServico, NumAto, NumEstacao, EnderecoEstacao, EndComplemento, SiglaUf, CodMunicipio, DesignacaoEmissao, Tecnologia, tipoTecnologia, meioAcesso, FreqTxMHz, FreqRxMHz, Azimute, CodTipoClasseEstacao, ClassInfraFisica, CompartilhamentoInfraFisica, CodTipoAntena, CodEquipamentoAntena, GanhoAntena, FrenteCostaAntena, AnguloMeiaPotenciaAntena, AnguloElevacao, Polarizacao, AlturaAntena, CodEquipamentoTransmissor, PotenciaTransmissorWatts, Latitude, Longitude, CodDebitoTFI, DataLicenciamento, DataPrimeiroLicenciamento, NumRede, _id, DataValidade, NumFistelAssociado, NomeEntidadeAssociado, Municipio.NomeMunicipio`.
- Colunas localizadas pelo nome no cabeçalho; falta de coluna obrigatória = erro fatal (exit ≠ 0).
- `SiglaUf` sempre coincide com o nome do ZIP; nenhuma linha tem chave nula na amostra (a regra de rejeição é implementada mesmo assim).
- Há espaços nas pontas em vários campos (inclusive NBSP `\u00A0`) → `string.Trim()` (remove qualquer espaço Unicode).

## 2. Mapeamento

### estacao
| coluna | origem | tratamento |
|---|---|---|
| fistel | NumFistel | texto limpo (trim, vazio→NULL) |
| num_estacao | NumEstacao | texto limpo |
| uf | SiglaUf | texto limpo + maiúsculas |
| cod_municipio | CodMunicipio | texto limpo |
| municipio | Municipio.NomeMunicipio | texto limpo, sem normalizar |
| endereco | EnderecoEstacao | normalização 1.6 |
| latitude / longitude | Latitude / Longitude | número |
| coordenada_valida | derivada de lat/lon | 1/0 |
| entidade | NomeEntidade | normalização 1.6 |
| operadora | derivada de entidade | regras ordenadas → VIVO/CLARO/TIM/OI/ALGAR/SERCOMTEL/BRISANET/OUTRAS |

Atributos vêm da **primeira linha carregada** da estação (uf, fistel, num_estacao).

### emissao
| coluna | origem | tratamento |
|---|---|---|
| id | — | AUTOINCREMENT (não informado) |
| uf, fistel, num_estacao | SiglaUf, NumFistel, NumEstacao | como acima |
| num_ato | NumAto | texto limpo |
| tecnologia | Tecnologia | texto limpo + maiúsculas |
| geracao | derivada de tecnologia | 2G/3G/4G/5G/N/D/OUTRA |
| designacao_emissao | DesignacaoEmissao | texto limpo + maiúsculas |
| largura_banda_khz | derivada da designação | ver §4 |
| freq_tx_mhz, freq_rx_mhz | FreqTxMHz, FreqRxMHz | número |
| duplex_mhz | tx − rx | arred. 3 casas |
| azimute | Azimute | número |
| potencia_w | PotenciaTransmissorWatts | número |
| ganho_antena | GanhoAntena | número |
| altura_antena_m | AlturaAntena | número |
| data_licenciamento, data_primeiro_licenciamento, data_validade | DataLicenciamento, DataPrimeiroLicenciamento, DataValidade | data estrita `yyyy-MM-dd` |
| vencida | data_validade < Hoje | 1/0 (nunca NULL) |
| anos_licenciada | (Hoje − data_primeiro_licenciamento)/365,25 | arred. 1 casa |
| status | Status.state | texto limpo |
| classe_estacao | CodTipoClasseEstacao | texto limpo |
| polarizacao | Polarizacao | texto limpo + maiúsculas |
| angulo_elevacao_graus | AnguloElevacao | número |
| angulo_meia_potencia_graus | AnguloMeiaPotenciaAntena | número |
| frente_costa_db | FrenteCostaAntena | número |
| cod_tipo_antena | CodTipoAntena | texto limpo |
| cod_equipamento_antena | CodEquipamentoAntena | texto limpo |
| cod_equipamento_transmissor | CodEquipamentoTransmissor | texto limpo |
| potencia_dbm | 10·log10(potencia_w·1000) | arred. 1 casa; NULL se potência nula ou ≤ 0 |
| azimute_valido, elevacao_valida | validações | três estados |

Demais campos brutos (NumServico, EndComplemento, tipoTecnologia, meioAcesso, ClassInfraFisica, CompartilhamentoInfraFisica, CodDebitoTFI, NumRede, _id, NumFistelAssociado, NomeEntidadeAssociado) são ignorados.

## 3. Separadores decimais / números

Formatos encontrados na amostra: `123`, `123.45`, `123,45`, `.5`, `-.5`, `-12.3`, `18.000`, `-5.6e-05`, `NAN`, `nan`, `#N/A`, `-`, `,`, `14,70.0`, `1900-03-04`, `45 W`, `133'312`, `--9.9`, `1.7/2.2`, `&gt;17`, `não existe`, `36 .0`, `45.5.` etc.

Conversão: trim → vazio = NULL → trocar `,` por `.` → aceitar apenas o padrão
`^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$` → `double.Parse` (InvariantCulture) → se não for finito, NULL.
Assim `.`/`,` são sempre decimais (`18.000` = 18, `55,0` = 55), `.5` = 0,5, notação científica é aceita,
e tudo que tiver dois separadores, texto, unidades, espaços internos, `NaN`/`Infinity` vira NULL.

## 4. Designação de emissão → largura_banda_khz

Designação já em maiúsculas; pega os 4 primeiros caracteres (se tiver menos de 4 → NULL).
Válido somente se exatamente uma posição (2ª, 3ª ou 4ª) for letra `H|K|M|G` e as outras três forem dígitos ASCII.
Valor = decimal(`<dígitos antes>.<dígitos depois>`) × fator (H 0,001; K 1; M 1000; G 1.000.000), calculado em `decimal` (sem erro binário).
Exemplos: `5M00`→5000; `10M0`→10000; `200K`→200; `2M50`→2500; `25K0`→25; `1G60`→1.600.000;
inválidos (→NULL): `11KF`, `7M0D`, `40MO` (letra O), `7M0G` (G na 4ª posição com letra unidade já presente), `40M` (curta), `TORR`, nula.

## 5. Validações de três estados

- `azimute_valido`: azimute NULL → NULL; 0 ≤ az ≤ 360 → 1; caso contrário → 0.
- `elevacao_valida`: ângulo NULL → NULL; −90 ≤ v ≤ 90 → 1; caso contrário → 0.
- (Lógicas de dois estados: `vencida`, `coordenada_valida` → sempre 0/1.)

## 6. Casos de borda e suposições

- **Trim**: `string.Trim()` (inclui NBSP). Vazio após trim = NULL em todos os campos.
- **Maiúsculas**: `ToUpperInvariant()`.
- **Normalização 1.6** (entidade, endereço): NFD → descarta `NonSpacingMark` → `ToUpperInvariant` → todo char que não for `char.IsLetterOrDigit` vira espaço → colapsa espaços → trim → vazio = NULL.
  *Suposição*: "letra ou dígito" no sentido Unicode, logo `º`/`ª` (categoria Lo) são preservados (`Nº 10` → `Nº 10`); `°`, `–`, `´`, controles etc. viram espaço.
- **Operadora**: entidade normalizada só tem `[letras/dígitos]` separados por um espaço, então "palavra" = token separado por espaço (equivale a `\b` aqui). `BRASIL TELECOM` é tratado como sequência de duas palavras inteiras (`COMPUNET DO BRASIL TELECOM` → OI; `MD BRASIL TELECOMUNICACOES` não). "Começa com a palavra" = primeiro token. Entidade NULL → OUTRAS.
- **Geração**: comparação exata (case-insensitive) do valor trimado; `LTE (TDD)`, `4G`, `5G`, `0.0`, `DMR` → OUTRA; NULL → N/D.
- **Datas**: regex `^\d{4}-\d{2}-\d{2}$` + `DateTime.TryParseExact` (rejeita 2023-02-30). Datas com hora (`2019-09-20 12:18:58.810`), `-`, `NA` → NULL.
- **vencida**: data_validade NULL → 0.
- **anos_licenciada**: dias inteiros (Hoje − data) / 365,25 em `decimal`, arredondado AwayFromZero; data futura gera valor negativo (spec não proíbe).
- **Arredondamento**: `MidpointRounding.AwayFromZero` sobre `decimal`. Para evitar ruído binário (ex.: 380,65 − 390,65 = −10,000000000000057), os doubles são convertidos para `decimal` pela representação mais curta ("R") e a subtração do duplex é feita em `decimal`. `potencia_dbm` calcula log10 em double e arredonda o resultado convertido para decimal.
- **coordenada_valida**: lat e lon não nulas, não (lat == 0 e lon == 0), −33,75 ≤ lat ≤ 5,28 e −73,99 ≤ lon ≤ −28,83 (inclusive). Uma coordenada só 0 cai fora do retângulo naturalmente.
- **Rejeição**: somente NumFistel, NumEstacao ou SiglaUf nulos após limpeza. Linha com nº de campos diferente do cabeçalho: campos faltantes tratados como vazios (não ocorre na amostra).
- **Duplicidade de emissão**: chave (uf, fistel, num_estacao, num_ato, azimute, freq_tx, freq_rx, designacao, tecnologia) com valores tratados; NULL é representado por marcador próprio (NULL == NULL, distinto de string vazia); números comparados pelo valor double (`55` = `55.0`). Mantém a primeira.
- **Estação**: primeira linha carregada define atributos; linhas seguintes da mesma estação só geram emissões.
- **Ordem**: arquivos processados em ordem alfabética (AC…TO), linhas na ordem do arquivo. Os conjuntos de deduplicação são globais (corretos mesmo que uma UF aparecesse em mais de um arquivo); a chave de emissão é guardada como hash XxHash128 (probabilidade de colisão desprezível para ~7,7 M chaves) para limitar a memória.
- **Caminhos**: a raiz `TCC` é localizada subindo a partir do diretório atual e do diretório do executável até achar `src/DDL.sql` e `files/`; fallback `D:\Projects\TCC`. Assim funciona em Windows (`D:\Projects\TCC`) e em qualquer cópia da pasta.
- **DDL**: o arquivo `src/DDL.sql` é lido e executado tal como está. Se o .db existir, é apagado antes.

## 7. Organização da solução

Arquivos (um projeto console, um `.csproj`, pacote `Microsoft.Data.Sqlite`):

- `Program.cs` — orquestração: resolve caminhos, recria o banco, percorre ZIPs, trata erros (stderr + exit 1).
- `Paths.cs` — localização da raiz do TCC.
- `CsvReader.cs` — leitura de registros `|`/aspas em streaming a partir do `ZipArchiveEntry` (sem extrair para disco).
- `Cleaning.cs` — funções puras de limpeza: texto, maiúsculas, número, data, normalização.
- `Derivations.cs` — funções puras: operadora, geração, largura de banda, duplex, dBm, vencida, anos, coordenada, validações, arredondamento.
- `Records.cs` — `EstacaoRow`, `EmissaoRow`, chave de duplicidade.
- `Transformer.cs` — linha bruta (índices por nome) → registros tratados.
- `SqliteLoader.cs` — criação do esquema via DDL, inserts preparados, transação única, PRAGMAs de carga (`journal_mode=OFF`, `synchronous=OFF`).
- `SelfTest.cs` — verificações dos exemplos do enunciado, executadas com `--selftest` (não roda na execução normal).

Justificativa: separa E/T/L; regras puras e testáveis isoladamente; streaming mantém memória baixa (só os conjuntos de chaves de deduplicação ficam em memória: tuplas de estação e hashes de 128 bits das emissões); inserts preparados numa transação dão desempenho de minutos para ~7,7 M linhas.

Fluxo: ZIP → CsvReader (string[]) → Transformer (limpeza + derivações) → rejeição → dedup estação/emissão → SqliteLoader.

## 8. Verificação

1. `dotnet build -c Release` sem erros.
2. `--selftest`: as 4 linhas de exemplo do enunciado produzem exatamente os valores esperados (estação e emissão), além de casos de borda (números, datas, designações, operadora).
3. Execução completa: exit code 0, tempo dentro do limite.
4. Consultas no banco: `sqlite_master` contém só `estacao`, `emissao` (+ índices/sqlite_sequence do DDL); esquema idêntico ao DDL; nenhuma chave nula; nenhuma emissão órfã; contagem de duplicatas = 0 na chave de dedup; conferência dos exemplos 1–4 direto no banco; distribuição de `geracao`, `operadora`, contagem de NULLs e de flags.
5. Comparação cruzada independente com um script Python (fora do projeto) para uma UF pequena.

## 9. Implementação e revisão

Implementar conforme acima e, ao final, revisar o código item a item contra este plano (seção "Revisão final" abaixo).

## Revisão final

Resultado da execução completa (Linux, mesmo comando `dotnet build` + `dotnet run --no-build`): exit 0 em ~2 min;
7.760.505 linhas lidas, 0 rejeitadas, 291.514 emissões duplicadas descartadas, **904.265 estações** e **7.468.991 emissões**.

Verificações realizadas:
- `--selftest`: os 4 exemplos do enunciado (estação e emissão, coluna a coluna) e ~60 casos de borda passam.
- Esquema: o SQL de `estacao`, `emissao` e dos dois índices em `sqlite_master` é idêntico ao texto do `DDL.sql`; só existem as tabelas do DDL (+ `sqlite_sequence`, criada pelo AUTOINCREMENT).
- 0 emissões órfãs; 0 grupos duplicados pela chave da §6; exemplos 1–4 conferidos direto no banco; tipos armazenados: REAL/INTEGER/TEXT/NULL conforme esperado.
- Reimplementação independente em Python (regex/Decimal) comparada linha a linha em AC, RR, AP, SE, RO, DF, SP e MG: **0 diferenças** em estações e emissões (incluindo a ordem das emissões).

Revisão do código contra o plano:
- §1 parser com aspas/`""`/multi-linha, colunas por nome, ZIP lido em streaming sem depender do nome — `CsvReader.cs`, `Transformer.cs`, `Program.cs` ✔
- §2 mapeamento de todas as colunas das duas tabelas — `Transformer.BuildEstacao/BuildEmissao`, `SqliteLoader` ✔
- §3 números (regex + `,`→`.` + finito) — `Cleaning.Number` ✔
- §4 largura de banda (4 primeiros caracteres, 1 unidade fora da 1ª posição, demais dígitos, `decimal`) — `Derivations.LarguraBandaKhz` ✔
- §5 três estados — `Derivations.AzimuteValido/ElevacaoValida`, gravados como 1/0/NULL ✔
- §6 trim/NULL, normalização Unicode, operadora por tokens, geração exata, datas estritas, arredondamento AwayFromZero em `decimal`, rejeição só por chave nula, dedup NULL==NULL, primeira linha define a estação ✔
- §7 estrutura em componentes conforme descrito; DDL executado sem alterações; banco apagado e recriado; erro → stderr + exit 1 ✔
- Distribuições observadas: geração N/D 5.165.615, 4G 1.258.644, 2G 378.791, 3G 378.665, 5G 211.195, OUTRA 76.081; operadora OUTRAS 718.580, VIVO 67.533, TIM 59.950, CLARO 52.026, BRISANET 3.860, ALGAR 2.258, SERCOMTEL 53, OI 5.
