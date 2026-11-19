# PLANO — ETL Anatel (claude_sonnet_completo_r3)

Parâmetros: empresa = Claude, modelo = Sonnet, geração = r3.
Projeto: `src/claude_sonnet_completo_r3` · Banco: `db/claude_sonnet_completo_r3.db`.

## 1. Compreensão da fonte (verificado nos 27 ZIPs)

- 27 ZIPs `<UF>.zip`, cada um com exatamente um CSV (`csv_licenciamento_<hash>.csv`); o nome não é usado.
- UTF-8 (sem BOM nos arquivos examinados; o leitor tolera BOM), separador `|`, todo valor entre aspas `"`, aspas internas duplicadas, quebra de linha CRLF.
- Total: 7.760.505 linhas de dados (7.760.532 quebras de linha = linhas + 27 cabeçalhos), logo nenhum campo contém quebra de linha. Todas as linhas têm 41 campos.
- `SiglaUf` de toda linha coincide com a UF do ZIP; nenhum `NumFistel`, `NumEstacao` ou `SiglaUf` vazio na amostra completa. Existem 904.265 estações distintas (uf, fistel, num_estacao).
- Colunas (41): Status.state, NomeEntidade, NumFistel, NumServico, NumAto, NumEstacao, EnderecoEstacao, EndComplemento, SiglaUf, CodMunicipio, DesignacaoEmissao, Tecnologia, tipoTecnologia, meioAcesso, FreqTxMHz, FreqRxMHz, Azimute, CodTipoClasseEstacao, ClassInfraFisica, CompartilhamentoInfraFisica, CodTipoAntena, CodEquipamentoAntena, GanhoAntena, FrenteCostaAntena, AnguloMeiaPotenciaAntena, AnguloElevacao, Polarizacao, AlturaAntena, CodEquipamentoTransmissor, PotenciaTransmissorWatts, Latitude, Longitude, CodDebitoTFI, DataLicenciamento, DataPrimeiroLicenciamento, NumRede, _id, DataValidade, NumFistelAssociado, NomeEntidadeAssociado, Municipio.NomeMunicipio.
- As colunas são localizadas pelo nome do cabeçalho (dicionário nome → índice).

## 2. Mapeamento

Segue exatamente a tabela da seção 3 da especificação. Estação: fistel, num_estacao, uf, cod_municipio, municipio, endereco, latitude, longitude, coordenada_valida (derivada), entidade, operadora (derivada). Emissão: todo o restante; `id` é gerado pelo banco. Os tratamentos são: texto limpo (Trim), maiúsculas (ToUpperInvariant), normalização (regra 1.6), número, data.

## 3. Separadores decimais (formatos vistos na amostra)

Inteiros (`55`), ponto (`55.0`, `2.79944`), ponto sem parte inteira (`.5`, `-.12`), vírgula (`205,79`), três casas com ponto (`18.000`, `10.000` — valem 18 e 10), notação científica (`-5.6E-5`), e lixo: `NA`, `nan`, `NAN`, `#N/A`, `n/a`, `-`, `não existe`, `14,5.0`, `15;4`, `350°`, `1900-03-04` (data do Excel), `11,96\u00a0.0`, `&gt;17`.

Conversão: trim; vazio → NULL; troca `,` por `.`; `double.TryParse` com `NumberStyles.Float` e `CultureInfo.InvariantCulture` (sem separador de milhar, de modo que `.` e `,` são sempre decimais); resultado não finito (NaN, ±∞) → NULL. Qualquer texto que não seja número (inclusive `14.5.0` após a troca) → NULL. `-0` é normalizado para `0`.

Datas: somente `yyyy-MM-dd` estrito (`DateTime.TryParseExact`, cultura invariante, sem tolerância a espaços); datas impossíveis (2023-02-30) e formatos como `2031-09-08 00:00:00.000` ou `44509` → NULL. Gravadas como texto `yyyy-MM-dd`.

## 4. Designação de emissão → largura de banda (kHz)

Pegar os 4 primeiros caracteres da designação já em maiúsculas (menos de 4 caracteres → NULL). Exigir exatamente uma letra de unidade (`H`,`K`,`M`,`G`) nas posições 1 a 3 (nunca na 0) e os demais três caracteres dígitos ASCII `0-9`. Parte inteira = dígitos antes da letra; fração = dígitos depois. Valor = `inteiro.fração` (decimal exato) × fator (H 0,001; K 1; M 1000; G 1.000.000). Exemplos: `5M00` → 5000; `10M0` → 10000; `200K` → 200; `2M50` → 2500. Sem letra, duas letras, letra na posição 0, ou caractere não dígito → NULL.

## 5. Validações de três estados

- `azimute_valido`: azimute NULL → NULL; 0 ≤ az ≤ 360 → 1; senão 0.
- `elevacao_valida`: ângulo NULL → NULL; −90 ≤ v ≤ 90 → 1; senão 0.
- Os valores já saem do número convertido (um texto inválido virou NULL antes, portanto vira "sem dado", não falso).

## 6. Casos de borda e suposições

| Transformação | Caso | Decisão |
|---|---|---|
| Trim | espaço não separável (`\u00a0`), tabs | `string.Trim()` remove todo espaço Unicode (suposição: "espaços" inclui NBSP) |
| Normalização | acentos | FormD, descarta `NonSpacingMark`, `ToUpperInvariant`, não letra/dígito (`char.IsLetterOrDigit`) → espaço, colapsa, trim; vazio → NULL |
| Município | não normaliza | só trim (`Feijó` fica `Feijó`) |
| Tecnologia | `0.0`, `5G`, `DMR`, `1G60D7W`, ` ` | trim + maiúsculas; geração só mapeia GSM/CDMA/WCDMA/WDCMA/LTE/NR (exato, após maiúsculas); vazio → `N/D`; demais → `OUTRA` (`5G` → `OUTRA` por regra literal) |
| Operadora | palavra | entidade normalizada é dividida em tokens por espaço; "palavra" = token inteiro; `BRASIL TELECOM` = dois tokens consecutivos; "começa com" = primeiro token; ordem 1→7; sem entidade → `OUTRAS` |
| Duplex | tx ou rx NULL | NULL; senão `Math.Round(tx − rx, 3, AwayFromZero)` |
| Potência dBm | w NULL ou ≤ 0 | NULL; senão `Math.Round(10·log10(w·1000), 1, AwayFromZero)` |
| Anos | data primeiro NULL | NULL; senão `(2026-10-15 − data).Days / 365,25` arredondado a 1 casa; datas futuras dão valor negativo (não coberto; mantido) |
| Vencida | validade NULL | 0 (falso) |
| Coordenada | lat/lon NULL, ambas 0, fora do retângulo | 0; dentro (inclusive) → 1. Gravada na estação |
| Estação | várias linhas | atributos da primeira linha aceita da chave (uf, fistel, num_estacao), mesmo que a emissão dessa linha seja duplicada |
| Duplicidade de emissão | chave | uf, fistel, num_estacao, num_ato, azimute, freq_tx, freq_rx, designação, tecnologia (já tratados; NULL = NULL; números comparados pelo valor, então `55` = `55.0`); mantém a primeira |
| Rejeição | fistel/num_estacao/uf nulo | linha descartada; nada mais descarta |
| CSV | BOM, linhas em branco, LF/CRLF, aspas escapadas | leitor próprio tolera todos; coluna ausente no cabeçalho → NULL |
| Ordem | primeira ocorrência | arquivos em ordem alfabética de UF (AC→TO), linhas na ordem do arquivo |
| Chaves de memória | 7,7 mi emissões | conjunto de hashes MD5 de 128 bits da chave (colisão desprezível) em vez de strings |

Suposições registradas: NBSP é espaço; notação científica é número válido; `5G` em `Tecnologia` é `OUTRA`; arredondamento sobre `double` com `MidpointRounding.AwayFromZero`; a largura de banda não é arredondada.

## 7. Organização da solução

Projeto de console único, `Program.cs` mais classes pequenas e sem estado, para testar isoladamente:

- `Paths`: resolve a raiz (`D:\Projects\TCC` no Windows; em outro ambiente, sobe a partir do diretório do projeto/atual até achar uma pasta com `files` e `src/DDL.sql`).
- `CsvReader`: leitor em streaming, tolerante, para `|` e `"`.
- `Cleaner`: trim, texto, normalização, número, data.
- `Derivations`: operadora, geração, largura de banda, duplex, dBm, vencida, anos, coordenada, validações.
- `Etl`: lê cada ZIP (sem extrair em disco), rejeita linhas, deduplica, monta `Station` e `Emission`.
- `SqliteWriter`: cria o banco a partir do `DDL.sql` lido do arquivo (sem alterações), grava em lote numa transação com comandos preparados.
- `Program`: orquestra, captura exceções, mensagem em `stderr` e código de saída ≠ 0.

Justificativa: separar as regras puras (testáveis) da E/S; streaming para manter a memória baixa; uma única passada na ordem dos arquivos para preservar a semântica de "primeira ocorrência". Biblioteca: `Microsoft.Data.Sqlite`.

## 8. Verificação

1. Compilar com `-c Release` sem avisos; executar com `--no-build`; código de saída 0.
2. Conferir que o banco tem exatamente as tabelas `estacao` e `emissao`, e que `sqlite_master` contém o mesmo DDL de `DDL.sql`.
3. Contagens: `estacao` = 904.265 (valor independente, medido na análise da fonte); `emissao` ≤ linhas lidas.
4. Reproduzir os 4 exemplos da especificação (linhas isoladas), comparando campo a campo com o esperado.
5. Consultas de sanidade: nenhuma chave de estação duplicada; nenhuma emissão sem estação; nenhuma chave de emissão duplicada; valores lógicos só 0/1/NULL; datas só `yyyy-MM-dd`; `geracao`/`operadora` dentro dos valores permitidos.
6. Não comparo com saída esperada externa (proibido pela tarefa).

## 9. Revisão final

Ao terminar, rever o código contra este plano; resultado registrado em "Revisão" abaixo.

## Revisão

Código revisado contra as seções 1–9 deste plano; resultado da execução completa (`dotnet build -c Release` + `dotnet run --no-build`, código 0, ~2 min):

- lidas = 7.760.505; rejeitadas = 0; estacao = 904.265 (igual à contagem independente da fonte); emissao = 7.468.991; emissões duplicadas descartadas = 291.514.
- Schema idêntico ao `DDL.sql` (a única entrada extra é `sqlite_sequence`, criada pelo SQLite por causa de `AUTOINCREMENT`); `integrity_check` = ok.
- Sem chave de estação duplicada, sem emissão órfã, sem chave de emissão duplicada, sem estação sem emissão; lógicos só 0/1/NULL; datas só `yyyy-MM-dd`.
- Os 4 exemplos da especificação (estações e emissões) conferem campo a campo.

Desvio encontrado e corrigido na verificação: com `InvariantGlobalization=true`, `string.Normalize(FormD)` não decompõe acentos, e `endereco`/`entidade`/`operadora` saíam errados (ex.: `CENTENÁRIO`; `TELEFÔNICA` não casava com VIVO, 470 estações). Solução: tabela de decomposição canônica embutida (`AccentTable.cs`, gerada do Unicode), independente de ICU/NLS.

Pontos não cobertos pela especificação e mantidos como suposição (ver seção 6): NBSP é espaço; notação científica é número; `5G` em `Tecnologia` → `OUTRA`; anos de datas futuras ficam negativos; arredondamento em `double` com `AwayFromZero`.
