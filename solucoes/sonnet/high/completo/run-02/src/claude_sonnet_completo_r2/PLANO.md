# PLANO — ETL Anatel (claude_sonnet_completo_r2)

## 1. Compreensão da fonte (verificada em amostra e em varredura completa dos 27 ZIPs)
- 27 ZIPs `<UF>.zip`, um CSV cada (7.760.505 linhas de dados no total), UTF-8 sem BOM, quebra de linha CRLF.
- Separador `|`, todos os valores entre aspas, `""` dentro do valor representa aspas. Nenhuma linha com quebra de linha embutida
  na amostra, mas o parser trata aspas que atravessam linhas por segurança. Todas as linhas têm 41 colunas, cabeçalho idêntico nos 27 arquivos.
- Colunas (por nome, nunca por posição): Status.state, NomeEntidade, NumFistel, NumServico, NumAto, NumEstacao, EnderecoEstacao,
  EndComplemento, SiglaUf, CodMunicipio, DesignacaoEmissao, Tecnologia, tipoTecnologia, meioAcesso, FreqTxMHz, FreqRxMHz, Azimute,
  CodTipoClasseEstacao, ClassInfraFisica, CompartilhamentoInfraFisica, CodTipoAntena, CodEquipamentoAntena, GanhoAntena,
  FrenteCostaAntena, AnguloMeiaPotenciaAntena, AnguloElevacao, Polarizacao, AlturaAntena, CodEquipamentoTransmissor,
  PotenciaTransmissorWatts, Latitude, Longitude, CodDebitoTFI, DataLicenciamento, DataPrimeiroLicenciamento, NumRede, _id,
  DataValidade, NumFistelAssociado, NomeEntidadeAssociado, Municipio.NomeMunicipio.
- Há valores com espaços nas pontas (ex.: NomeEntidade, DesignacaoEmissao), SiglaUf sempre coincide com o nome do ZIP.

## 2. Mapeamento
Exatamente a tabela da seção 3 da especificação (campo bruto → coluna destino → tratamento). Estação recebe: fistel, num_estacao, uf,
cod_municipio, municipio, endereco, latitude, longitude, coordenada_valida, entidade, operadora. Emissão recebe todo o restante
(ver `EmissaoRow`/`Transformer`). `id` é gerado pelo banco.

## 3. Separadores decimais (formatos observados)
`874.5`, `2680`, `6890,00`, `154,390`, `.0`, `-.02`, `0,23`, `-5.6E-5`; lixo: `NA`, `nan`, `#N/A`, `,`, `-`, `1900-03-04`,
`350°`, `14,5.0`, `--4.0`, `45 W`, `53'312`, `1.5/1.5`, `não existe`.
Regra: trim → trocar `,` por `.` → aceitar somente `^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$` (cultura invariante) → `double` finito;
caso contrário NULL. Assim `55` = `55.0`, `18.000` = 18 e `6890,00` = 6890. `-0` normalizado para `0`.
Datas: só `yyyy-MM-dd` exato (`ParseExact`, invariante); `2019-04-10 07:29:59.060`, `-`, `NA`, `44509` → NULL.

## 4. Designação de emissão → largura de banda (kHz)
Pegar os 4 primeiros caracteres da designação em maiúsculas (menos de 4 → NULL). Exatamente uma letra em {H,K,M,G} nas posições 1..3
(nunca na 0), as outras três posições são dígitos ASCII. `inteiro` = dígitos antes da letra, `fração` = dígitos depois (pode ser vazia).
Valor = decimal(`inteiro.fração`) × {H 0,001; K 1; M 1000; G 1.000.000} (cálculo em `decimal`, para evitar ruído binário) → double.
Ex.: `5M00`→5000, `10M0`→10000, `200K`→200, `2M50`→2500. Qualquer outra forma → NULL.

## 5. Validações de três estados
- `azimute_valido`: NULL se azimute NULL; 1 se 0 ≤ az ≤ 360; senão 0.
- `elevacao_valida`: NULL se ângulo de elevação NULL; 1 se −90 ≤ v ≤ 90; senão 0.

## 6. Casos de borda e suposições
- Rejeição: só se NumFistel, NumEstacao ou SiglaUf nulos após limpeza (nenhuma outra condição).
- Número não finito (`NaN`, `Infinity`) → NULL (o regex já os recusa).
- Normalização de texto: NFD, descartar marcas não espaçadoras (Mn), `ToUpperInvariant`, tudo que não for `char.IsLetterOrDigit` vira espaço,
  colapsar espaços, trim; vazio → NULL. Suposição: letras fora de A-Z (ex.: `Ø`, `ß`) permanecem como estão.
- Operadora: sobre os tokens da entidade normalizada (palavra = token entre espaços, equivalente a `\b`, pois só restam letras/dígitos e espaços).
  Ordem 1..7, primeira regra que casa; entidade NULL → OUTRAS. "Começa com a palavra TIM/OI" = primeiro token. `BRASIL TELECOM` = dois tokens consecutivos.
- Geração: `Tecnologia` trim, comparação sem caixa; `5G`, `LTE (TDD)`, `DMR`, `0.0` etc. → OUTRA (só os valores listados mapeiam); nulo → N/D.
- Arredondamento: `Math.Round(x, n, MidpointRounding.AwayFromZero)`.
- `potencia_dbm`: NULL se potência nula ou ≤ 0. `duplex_mhz`: NULL se tx ou rx nulo.
- `anos_licenciada`: `(2026-10-15 − data).Days / 365.25`, 1 casa; pode ser negativo se a data for futura (não há regra de corte).
- `vencida`: data_validade não nula e < 2026-10-15; nula → 0.
- `coordenada_valida`: lat e lon não nulas, não ambas 0, lat ∈ [−33,75; 5,28], lon ∈ [−73,99; −28,83].
- Estação: chave (uf, fistel, num_estacao); atributos da primeira linha aceita da estação (na ordem: ZIPs em ordem alfabética, linhas na ordem do arquivo).
- Emissão duplicada: chave (uf, fistel, num_estacao, num_ato, azimute, freq_tx, freq_rx, designacao, tecnologia) com valores já tratados, NULL = NULL;
  a primeira é mantida. A chave é guardada como hash MD5 de 128 bits (memória baixa; probabilidade de colisão desprezível) — a deduplicação é global
  entre arquivos. Campos de texto da chave são codificados com prefixo de tamanho para evitar ambiguidades de concatenação.
- Linhas de comprimento diferente do cabeçalho: campos ausentes tratados como vazios (não ocorreu nos dados).
- Gravação: datas `yyyy-MM-dd`, booleanos 1/0, NULL como NULL, REAL em double.

## 7. Organização da solução
- `Program.cs`: orquestra (resolve pastas, apaga e recria o DB, processa ZIPs em ordem, relata, código de saída 0/1 + stderr).
- `PathResolver`: localiza a raiz (`D:\Projects\TCC` se existir; senão sobe a partir do diretório atual/binário procurando `files` + `src\DDL.sql`).
- `CsvReader`: parser de CSV streaming, `|`/aspas, sem alocar por campo além da string.
- `Cleaner`: limpeza geral (trim/NULL, número, data, normalização de texto).
- `Derivations`: operadora, geração, largura de banda, duplex, dBm, vencida, anos, coordenada, validações (funções puras, testáveis).
- `Loader`: SQLite (DDL.sql executado tal qual, transação única, comandos preparados, PRAGMAs de carga), dedupe de estação e emissão.
Justificativa: separação entre leitura, regras puras e gravação facilita revisar cada regra contra a especificação e manter o fluxo em streaming
(7,7 milhões de linhas, nunca todas em memória).

## 8. Verificação
1. Rodar com o RR.zip isolado (`ETL_ONLY=RR`, variável de ambiente de depuração) e comparar as linhas dos exemplos 1–4 do enunciado com o banco.
2. Rodar completo; conferir: só as tabelas `estacao`, `emissao` (+ `sqlite_sequence`), esquema idêntico ao DDL (`sqlite_master`), `PRAGMA foreign_key_check` vazio,
   contagens (linhas lidas = rejeitadas + duplicadas + gravadas), sem duplicatas pela chave de emissão, domínio de `geracao`/`operadora`, booleanos ∈ {0,1}, datas no formato.
3. Verificação independente com um script SQL/Python sobre amostras (não compara com resultado esperado, que não temos).
4. Exit code 0 e tempo de execução bem abaixo de 2 h.
5. Ao final, revisão do código contra este PLANO.

## 9. Revisão final contra o plano (executada)
- Implementado conforme os itens 1–8; desvio único em relação ao rascunho: `InvariantGlobalization` foi removido do `.csproj`, porque o modo invariante
  não decompõe acentos em `string.Normalize` (a regra 1.6 falhava: `CENTENÁRIO` não virava `CENTENARIO`). Com ICU/NLS a normalização funciona.
- Os 4 exemplos do enunciado reproduzem exatamente (estacao e emissao).
- Execução completa: 7.760.505 linhas lidas, 0 rejeitadas, 291.514 emissões duplicadas descartadas, 904.265 estações, 7.468.991 emissões; ~2 min; exit 0.
- Esquema idêntico ao DDL.sql (só `estacao`, `emissao`, índices e `sqlite_sequence` do AUTOINCREMENT); `foreign_key_check` vazio; nenhuma estação sem emissão;
  nenhuma chave de emissão duplicada; booleanos ∈ {0,1}; datas em `yyyy-MM-dd`.
- Reimplementação independente em Python (AC, AP, DF, PB, RR, ES, GO, MA, PE ≈ 1,15 milhão de emissões) comparada coluna a coluna, na ordem: 0 divergências.
- Suposições registradas: ver seção 6 (ex.: `anos_licenciada` negativo para datas futuras; `5G`/`LTE (TDD)` → OUTRA).
