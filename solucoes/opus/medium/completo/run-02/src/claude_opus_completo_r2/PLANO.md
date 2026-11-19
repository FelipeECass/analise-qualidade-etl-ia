# PLANO — ETL Anatel (claude_opus_completo_r2)

Placeholders: `{{empresa}}` = claude, `{{modelo}}` = opus, `{{geracao}}` = r2.
Projeto: `src/claude_opus_completo_r2`. Banco: `db/claude_opus_completo_r2.db`.

## 1. Compreensão da fonte

O perfil foi feito com os 27 ZIPs (7.760.505 linhas de dados, ~2,95 GB de CSV descompactado).

- Cada ZIP tem um único `csv_licenciamento_<hash>.csv`. O programa lê a **primeira entrada `.csv`** do ZIP, sem depender do nome.
- Encoding UTF-8, sem BOM (o BOM é removido mesmo assim, se aparecer). Quebra de linha CRLF.
- Separador `|`. Todos os campos vêm entre aspas, vazio é `""`. Não há aspas internas nem quebras de linha dentro de valores, mas o parser trata os dois casos (RFC 4180 com `|`).
- O cabeçalho é idêntico nos 27 arquivos (41 colunas):
  `Status.state, NomeEntidade, NumFistel, NumServico, NumAto, NumEstacao, EnderecoEstacao, EndComplemento, SiglaUf, CodMunicipio, DesignacaoEmissao, Tecnologia, tipoTecnologia, meioAcesso, FreqTxMHz, FreqRxMHz, Azimute, CodTipoClasseEstacao, ClassInfraFisica, CompartilhamentoInfraFisica, CodTipoAntena, CodEquipamentoAntena, GanhoAntena, FrenteCostaAntena, AnguloMeiaPotenciaAntena, AnguloElevacao, Polarizacao, AlturaAntena, CodEquipamentoTransmissor, PotenciaTransmissorWatts, Latitude, Longitude, CodDebitoTFI, DataLicenciamento, DataPrimeiroLicenciamento, NumRede, _id, DataValidade, NumFistelAssociado, NomeEntidadeAssociado, Municipio.NomeMunicipio`
- As colunas são localizadas pelo nome no cabeçalho. Se faltar uma coluna obrigatória, o programa falha com erro.
- Nenhuma linha tem `NumFistel`, `NumEstacao` ou `SiglaUf` vazios, mas a regra de rejeição é implementada mesmo assim.

## 2. Mapeamento

Funções de limpeza: **T** = texto limpo (trim de espaços Unicode, vazio → NULL); **U** = T + maiúsculas (invariante); **N** = número; **D** = data; **X** = normalização de texto (1.6).

estacao:
| coluna | origem | tratamento |
|---|---|---|
| fistel | NumFistel | T |
| num_estacao | NumEstacao | T |
| uf | SiglaUf | U |
| cod_municipio | CodMunicipio | T |
| municipio | Municipio.NomeMunicipio | T |
| endereco | EnderecoEstacao | X |
| latitude / longitude | Latitude / Longitude | N |
| coordenada_valida | derivado (lat, lon) | 1/0 |
| entidade | NomeEntidade | X |
| operadora | derivado (entidade) | regras 1–7, senão OUTRAS |

emissao: `uf, fistel, num_estacao` (como acima); `num_ato` ← NumAto (T); `tecnologia` ← Tecnologia (U); `geracao` (derivado); `designacao_emissao` ← DesignacaoEmissao (U); `largura_banda_khz` (derivado); `freq_tx_mhz` ← FreqTxMHz (N); `freq_rx_mhz` ← FreqRxMHz (N); `duplex_mhz` (derivado); `azimute` ← Azimute (N); `potencia_w` ← PotenciaTransmissorWatts (N); `ganho_antena` ← GanhoAntena (N); `altura_antena_m` ← AlturaAntena (N); `data_licenciamento` ← DataLicenciamento (D); `data_primeiro_licenciamento` ← DataPrimeiroLicenciamento (D); `data_validade` ← DataValidade (D); `vencida` e `anos_licenciada` (derivados); `status` ← Status.state (T); `classe_estacao` ← CodTipoClasseEstacao (T); `polarizacao` ← Polarizacao (U); `angulo_elevacao_graus` ← AnguloElevacao (N); `angulo_meia_potencia_graus` ← AnguloMeiaPotenciaAntena (N); `frente_costa_db` ← FrenteCostaAntena (N); `cod_tipo_antena` ← CodTipoAntena (T); `cod_equipamento_antena` ← CodEquipamentoAntena (T); `cod_equipamento_transmissor` ← CodEquipamentoTransmissor (T); `potencia_dbm`, `azimute_valido`, `elevacao_valida` (derivados). `id` é gerado pelo banco (AUTOINCREMENT).

## 3. Separadores decimais

Formatos vistos (dígitos agrupados como `9`): `9`, `9.9`, `9,9`, `.9`, `-9`, `-9.9`, `-.9`, `-9.9e-9`, mais lixo: `nan`, `NAN`, `#N/A`, `-`, `9 W`, `9'9`, `9´9`, `9-9-9`, `9,9.9`, `--9.9`, `- 9,9`, `9/9`, `9.9.`, `9 .9`, `9,,9`, `não existe`, `&gt;9`, `,`.

Conversão: faço trim e troco `,` por `.`. O resultado precisa casar com a gramática
`[+-]? (dígitos [. dígitos?] | . dígitos) ([eE] [+-]? dígitos)?`, tudo ASCII. A conversão usa `double.Parse(InvariantCulture)`, e o resultado precisa ser finito. Qualquer outra coisa vira NULL.
- O `.`/`,` é sempre decimal: `18.000` → 18, `55`≡`55.0`.
- Mais de um separador (`9,9.9`, `9.9.`) → NULL. `nan`/`inf`/`infinity` → NULL (não casam com a gramática).
- Notação científica (`-3.2e-5`) é aceita, porque é um número finito.

## 4. Designação de emissão → largura_banda_khz

1. Pego a designação já em maiúsculas (NULL → NULL) e uso os **4 primeiros caracteres**. Se tiver menos de 4, uso todos os que existem (suposição: `40M` → 40 MHz).
2. Exatamente um caractere precisa ser `H`, `K`, `M` ou `G`, e não pode estar na posição 0. Todos os outros têm de ser dígitos ASCII `0-9`; se não forem, o resultado é NULL.
3. A parte inteira são os dígitos antes da letra (pelo menos 1). A fração são os dígitos depois dela (pode ser vazia). O valor é `decimal("inteiro.fração") × fator`, com H=0,001, K=1, M=1000, G=1.000.000, convertido para double.
   Ex.: `5M00`→5000; `10M0`→10000; `200K`→200; `2M50`→2500; `25K0`→25; `1G60`→1.600.000; `100H`→0,1.
   Casos inválidos: `16KF` (F não é dígito), `5MO0` (letra O), sem letra de unidade, letra na posição 0, duas letras.

## 5. Validações de três estados

- `azimute_valido`: NULL se azimute é NULL; 1 se 0 ≤ az ≤ 360; senão 0.
- `elevacao_valida`: NULL se angulo_elevacao_graus é NULL; 1 se −90 ≤ v ≤ 90; senão 0.
- Lógicos de dois estados: `vencida` (1 se data_validade não nula e < 2026-10-15) e `coordenada_valida` (1 se lat e lon existem, não são ambas 0, −33,75 ≤ lat ≤ 5,28 e −73,99 ≤ lon ≤ −28,83).

## 6. Casos de borda e decisões

- **Trim**: uso `string.Trim()` do .NET (espaço Unicode, inclusive NBSP `\u00A0`, que aparece na base).
- **Datas**: regex `^\d{4}-\d{2}-\d{2}$` (ASCII) e `DateOnly.TryParseExact("yyyy-MM-dd")` válido no calendário. `2023-01-05 00:00:00.0`, `-`, `NA`, `2023-02-30` → NULL.
- **Normalização (1.6)**: NFD, descarte de `NonSpacingMark` (e das outras marcas combinantes Mc/Me), `ToUpperInvariant`, e todo caractere que não for `char.IsLetterOrDigit` vira espaço. Depois colapso os espaços e faço trim. Vazio → NULL. Suposição: letras Unicode sem decomposição continuam iguais (`º`, `ª` são letras na categoria Lo e ficam, como em `Nº`), já que a regra só manda remover marcas.
- **Operadora**: aplicada sobre a entidade normalizada, que só tem letras/dígitos e espaços simples. "Palavra" é verificada com tokens delimitados por limites (regex `\b`). `TIM`/`OI` precisam ser a primeira palavra. Suposição: a expressão `BRASIL TELECOM` também exige limite de palavra nas duas pontas, então `MD BRASIL TELECOMUNICACOES` → OUTRAS (afeta ~23 linhas).
- **Geração**: comparo `tecnologia` em maiúsculas. `GSM`/`CDMA`→2G; `WCDMA`/`WDCMA`→3G; `LTE`→4G; `NR`→5G; NULL→N/D; o resto (`LTE (TDD)`, `5G`, `DMR`, `0.0`…) → OUTRA, porque a comparação é exata.
- **Arredondamento**: meio para longe do zero. Converto o double para `decimal` pela representação de ida e volta mais curta (`"R"`) e aplico `Math.Round(decimal, n, AwayFromZero)`, o que evita erro binário em 2,25 → 2,3.
- **duplex_mhz**: tx − rx com sinal, arredondado a 3 casas. NULL se faltar algum dos dois.
- **potencia_dbm**: NULL se potência NULL ou ≤ 0; senão `round(10·log10(w·1000), 1)`.
- **anos_licenciada**: `(2026-10-15 − data_primeiro)` em dias / 365,25, arredondado a 1 casa. Datas futuras dão valor negativo (a especificação não exclui esse caso).
- **Estação**: a chave é (uf, fistel, num_estacao), e os atributos vêm da primeira linha vista. Os ZIPs são processados em ordem alfabética (AC…TO) e as linhas na ordem do arquivo. Linhas seguintes da mesma estação não alteram nada.
- **Emissão duplicada**: a chave é uf, fistel, num_estacao, num_ato, azimute, freq_tx, freq_rx, designacao, tecnologia, já tratados. Números entram pelo valor double (55 ≡ 55.0), e NULL tem um marcador próprio, igual entre si e diferente de `""`. A chave vira string com separadores de controle e é guardada como hash XxHash128 num `HashSet<UInt128>`, o que dá pouca memória para 7,7 M linhas; a chance de colisão é desprezível. Vale a primeira ocorrência.
- **Rejeição**: só quando NumFistel, NumEstacao ou SiglaUf ficam NULL após a limpeza.
- **Linha com nº de campos diferente do cabeçalho**: não aparece na base. Campos que faltam são tratados como vazios e campos extras são ignorados (suposição).

## 7. Organização da solução

Um único projeto de console, com componentes pequenos e sem estado global:
- `Paths`: resolve a raiz `TCC`. Usa `D:\Projects\TCC` se existir; senão sobe a partir do diretório atual e do diretório do executável até achar `files/` + `src/DDL.sql`. Assim roda tanto no Windows quanto neste ambiente.
- `CsvReader`: parser em streaming (lê o ZIP direto, sem extrair) e devolve `string[]` por registro.
- `Cleaning`: limpeza de texto, número, data e normalização.
- `Derivations`: operadora, geração, largura de banda, arredondamento e demais derivados.
- `Records`: registros `Estacao`/`Emissao`.
- `Transformer`: monta os registros a partir de uma linha bruta (mapeamento do cabeçalho).
- `SqliteLoader`: cria o banco com o DDL lido do arquivo e executado sem alterações. Usa PRAGMAs de carga rápida (`journal_mode=OFF`, `synchronous=OFF`), comandos preparados e uma transação a cada 100 mil linhas.
- `Program`: orquestra extração → transformação → deduplicação → carga, com código de saída 0, ou ≠0 com a mensagem no stderr.

Justificativa: o volume (~3 GB) pede streaming e memória limitada. Separar a limpeza e os derivados em funções puras facilita conferir cada regra contra os exemplos da especificação.

## 8. Verificação

1. Um modo de autoteste interno (`--selftest`) roda as 4 linhas de exemplo da especificação pelo transformador e compara campo a campo com os valores esperados, além de checar casos de borda de número, data e designação.
2. Execução completa. Depois, com Python/sqlite3: o esquema (`sqlite_master`) precisa ser igual ao DDL; só as tabelas `estacao`/`emissao` (+ `sqlite_sequence` do AUTOINCREMENT); contagens; nenhuma emissão sem estação; nenhuma duplicata pela chave; distribuição de geracao/operadora; amostras dos exemplos lidas do banco.
3. Revisão final do código contra este plano.

## 9. Resultado da verificação e revisão

- `--selftest`: OK (4 exemplos da especificação campo a campo + casos de borda).
- Execução completa: 7.760.505 linhas lidas, 0 rejeitadas, 291.514 emissões duplicadas descartadas, **7.468.991 emissões**, **904.265 estações**. Tempo de ~2 min, código de saída 0.
- Banco: só `estacao`, `emissao` (+ `sqlite_sequence`, criada pelo próprio SQLite por causa do AUTOINCREMENT) e os índices do DDL. O SQL de cada objeto em `sqlite_master` aparece literalmente no `DDL.sql`.
- Integridade: 0 emissões sem estação, 0 estações sem emissão, 0 grupos duplicados pela chave da seção 6.
- Tipos: REAL nos números, INTEGER 0/1 nos lógicos, TEXT `yyyy-MM-dd` nas datas, NULL nos ausentes.
- As linhas dos exemplos 1, 3 e 4 lidas do banco conferem com os valores esperados.
- Revisão do código contra o plano: todas as decisões das seções 3–7 estão implementadas em `Cleaning`, `Derivations`, `Transformer`, `Deduplicator` e `SqliteLoader`.
