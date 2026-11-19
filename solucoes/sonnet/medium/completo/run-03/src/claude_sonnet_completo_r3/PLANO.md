# PLANO — claude_sonnet_completo_r3

## 1. Compreensão da fonte
- 27 ZIPs (`<UF>.zip`), cada um com 1 CSV (nome ignorado: pega-se a única entrada `.csv`). ~7,76 milhões de linhas no total.
- UTF-8 (tratar BOM), separador `|`, todos os valores entre aspas, `""` interno = aspa literal, quebra de linha CRLF.
- Parser próprio, em fluxo, ciente de aspas (aceita `\r\n`/`\n` dentro de aspas). Cabeçalho lido pelo nome (40 colunas, incluindo `Municipio.NomeMunicipio`).

## 2. Mapeamento
Conforme tabela da seção 3 do enunciado (campo bruto → coluna, com tratamento: texto limpo / normalizado / maiúsculas / número / data). Chaves: `uf, fistel, num_estacao`.

## 3. Separadores decimais (amostra real)
Formatos vistos: `40`, `.250`, `-8.17`, `49,5`, `6950,00`, `1.5E-5`, `NA`, `#N/A`, `-`, `n/a`, `50 W`, `17,50.0`, `133'312`, `36 .0`, NBSP nas pontas.
Regra: trim (inclui NBSP) → vazio = NULL → troca `,` por `.` → `double.Parse` InvariantCulture com `AllowLeadingSign|AllowDecimalPoint|AllowExponent` (sem milhar) → NULL se falhar ou não finito. `18.000` = 18.

## 4. Designação de emissão
Pegar 4 primeiros caracteres (maiúsculos); exigir 4 caracteres; exatamente uma letra em `HKMG` fora da posição 0; demais posições dígitos. Valor = `int.frac` (decimal) × fator (H 0,001; K 1; M 1000; G 1e6). Qualquer outro caso → NULL.

## 5. Validações três estados
- `azimute_valido`: NULL se azimute nulo; 1 se 0..360; senão 0.
- `elevacao_valida`: NULL se ângulo nulo; 1 se −90..90; senão 0.

## 6. Casos de borda / suposições
- Datas: `ParseExact yyyy-MM-dd` estrito (rejeita `2017-01-12 14:45:18`, `44452`, `-`, datas inexistentes) → NULL.
- Códigos permanecem texto (zeros à esquerda).
- Rejeição só se fistel, num_estacao ou uf nulos após limpeza.
- Arredondamento: `Math.Round(x, n, MidpointRounding.AwayFromZero)`; −0 normalizado para 0.
- `largura_banda`: calculada em `decimal` para evitar ruído binário.
- Operadora: tokens da entidade normalizada separados por espaço; `BRASIL TELECOM` = tokens consecutivos.
- Normalização: FormD, descarta marcas Mn, ToUpperInvariant, não letra/dígito → espaço, colapsa.
- Estação: primeira linha aceita da chave; `coordenada_valida` calculada nessa linha.
- Emissão duplicada: chave (uf, fistel, num_estacao, num_ato, azimute, tx, rx, designação, tecnologia) com NULL≠"" distinguido; guardada como hash de 128 bits para poupar memória.
- Ordem de leitura: arquivos em ordem alfabética de UF, linhas na ordem do arquivo → "primeira" bem definida.
- Datas texto `yyyy-MM-dd`; booleanos 1/0.

## 7. Organização
Arquivos: `Program.cs` (orquestração, exit code/stderr), `CsvReader.cs` (parser), `Cleaner.cs` (limpeza/normalização/números/datas), `Derivations.cs` (colunas derivadas), `Database.cs` (DDL + inserts em transação única), `EtlPipeline.cs` (fluxo, dedup). Separação facilita testar cada regra. DDL lido de `src/DDL.sql` sem alterações.

## 8. Verificação
Após executar: conferir contagem de tabelas (exatamente `estacao`, `emissao`), comparar linhas dos 4 exemplos do enunciado contra o banco, checar ausência de duplicatas de chave, NOT NULL, e que `sqlite_master` corresponde ao DDL.

## 9. Revisão final
Implementação revisada contra o plano: todas as regras acima implementadas. Execução: 7.760.505 linhas lidas, 0 rejeitadas, 904.265 estações, 7.468.991 emissões, 291.514 duplicatas descartadas (~2m50s). Exemplos 1–4 conferidos; sem chaves duplicadas em emissao.
