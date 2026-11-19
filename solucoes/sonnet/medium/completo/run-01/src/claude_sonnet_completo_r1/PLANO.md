# PLANO — ETL Anatel (Claude / Sonnet / completo / r1)

Placeholders: empresa=Claude, modelo=Sonnet, geracao=r1 → banco `db/claude_sonnet_completo_r1.db`, projeto `src/claude_sonnet_completo_r1`.
Nota: o "projeto .NET vazio" não existia no espaço de trabalho (só `src/DDL.sql`); o projeto foi criado com `dotnet new console` (net10.0). Raiz: `D:\Projects\TCC` se existir; senão sobe a partir do binário até achar a pasta com `files` e `src/DDL.sql` (ambiente atual: `/workspace`).

## 1. Compreensão da fonte
- 27 ZIPs `<UF>.zip`, um CSV cada (nome ignorado: usa-se a única entrada `.csv`).
- UTF-8, separador `|`, todos os valores entre aspas `"`, aspas internas duplicadas, quebra de linha `\r\n` (pode haver quebra dentro de campo entre aspas → parser próprio de CSV com estado).
- 41 colunas no cabeçalho (listadas na especificação); localizadas por nome (case-sensitive, com fallback case-insensitive). SP tem ~2,3 M linhas → leitura em streaming, nada de carregar o arquivo em memória.
- Amostra (SP): números com `.` e `,`, até 12 casas; `Tecnologia` vazia na maioria; valores como `Digital`, `DMR`, `WDCMA`; `DataValidade` ocasionalmente `yyyy-MM-dd HH:mm:ss.fff` (→ NULL pela regra estrita); designações com sufixo `--`.

## 2. Mapeamento
Conforme tabela da seção 3 da especificação (campo bruto → coluna). Estação: fistel, num_estacao, uf, cod_municipio, municipio, endereco, latitude, longitude, coordenada_valida, entidade, operadora. Emissão: demais colunas, mais derivadas `geracao`, `largura_banda_khz`, `duplex_mhz`, `potencia_dbm`, `vencida`, `anos_licenciada`, `azimute_valido`, `elevacao_valida`.

## 3. Separadores decimais
Troca `,`→`.`, `double.Parse` com `NumberStyles.Float` e cultura invariante. `.`/`,` nunca é milhar (`18.000`=18). Mais de um separador (ex. `1.234,5`) → `Parse` falha → NULL. Resultado não finito (`NaN`, `Infinity`, overflow) → NULL. Textos (`NA`) → NULL. Armazenado como REAL.

## 4. Designação de emissão
Maiúsculas; precisa de ≥4 caracteres; nos 4 primeiros, exatamente uma letra `H/K/M/G` em posição 1..3 e todo o resto dígitos. Inteiro = antes da letra, fração = depois (pode ser vazia → 0). Valor em `decimal` × fator (H=0,001, K=1, M=1000, G=1.000.000) → double. Qualquer outro padrão (inclusive unidade na posição 0, duas letras, não-dígito) → NULL.

## 5. Validações de três estados
- `azimute_valido`: NULL se azimute nulo; 1 se 0≤x≤360; senão 0.
- `elevacao_valida`: NULL se ângulo nulo; 1 se −90≤x≤90; senão 0.

## 6. Casos de borda e suposições
- Linhas com menos campos que o cabeçalho: campos faltantes = nulo. Linha totalmente vazia ignorada.
- Rejeição só se NumFistel, NumEstacao ou SiglaUf nulos após limpeza.
- Texto sem normalização (`municipio`, códigos, status): apenas Trim. Trim = espaços Unicode (`string.Trim`).
- Normalização: FormD, descarta marcas (Mn/Mc/Me), `ToUpperInvariant`, não letra/dígito → espaço, colapsa.
- `operadora` por tokens do texto normalizado (equivale a `\b`); `BRASIL TELECOM` = tokens consecutivos; ordem de regras 1..7.
- `geracao`: maiúsculas; nulo/vazio → `N/D`.
- Arredondamento: `Math.Round(..., MidpointRounding.AwayFromZero)`; duplex em `decimal` para evitar ruído binário (ex.: 380.65−390.65 → −10.0).
- `potencia_dbm`: NULL se W nulo ou ≤0.
- `anos_licenciada` pode ser negativa se a data for futura (sem tratamento especial). `vencida` = validade < 2026-10-15.
- `coordenada_valida` considera lat/lon não nulas, não ambas 0, dentro do retângulo.
- Datas: `DateTime.TryParseExact("yyyy-MM-dd")` invariante, sem espaços extras (já trimmed); inválidas (ex. 2023-02-30) → NULL.
- Estação: primeira ocorrência por (uf,fistel,num_estacao) na ordem dos arquivos AC→TO e das linhas; atributos dessa linha. Estação é criada mesmo que a emissão seja duplicada.
- Duplicidade de emissão: chave = uf, fistel, num_estacao, num_ato, azimute, freq_tx, freq_rx, designacao, tecnologia (tratados; NULL=NULL), guardada como hash de 128 bits (XxHash128) para economizar memória.
- Arquivos processados em ordem alfabética de UF; `id` autoincrementa na ordem de inserção.

## 7. Organização
- `Program.cs`: orquestra, resolve caminhos, trata erros (stderr, exit 1).
- `CsvReader.cs`: leitor streaming `|`/`"`.
- `Cleaner.cs`: limpeza e parsing (texto, número, data, normalização).
- `Derivations.cs`: operadora, geracao, largura, duplex, dBm, vencida, anos, coordenadas, validações.
- `Loader.cs`: abre ZIP, mapeia colunas, dedupe, grava em SQLite (uma transação, comandos preparados, PRAGMAs de velocidade). Esquema criado executando `DDL.sql` sem alterações.
Justificativa: funções puras separadas facilitam teste; streaming mantém memória baixa.

## 8. Verificação
Build Release sem warnings; execução completa com exit 0; conferir contagens, as tabelas existentes (apenas estacao, emissao), checagem de `PRAGMA foreign_key_check`, e reproduzir os 4 exemplos da especificação via consultas SQL (inclui casos de coordenada, NULLs, `anos_licenciada`, `potencia_dbm`); sondar nulos/valores distintos de geracao/operadora. Não comparar com resultado esperado.

## 9. Revisão final
Após implementar, reler o código contra este plano.
