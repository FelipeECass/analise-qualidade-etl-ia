## Tarefa

Desenvolva, em C#, utilizando o .NET 10.0.404, um programa de console que implemente um pipeline ETL (extração, transformação e carga) sobre os dados públicos de licenciamento de estações da Anatel, conforme a especificação abaixo.

## Regras

- Trabalhe somente dentro de `D:\Projects\TCC`. Não acesse nenhuma outra pasta, incluindo `D:\Projects\TCC_GitHub`.
- Não use, copie, consulte nem reaproveite o código em `D:\Projects\TCC\src\etl-base`. Ele não faz parte da solução. Implemente tudo a partir do projeto .NET vazio do espaço de trabalho.
- Se houver qualquer dúvida necessária para a implementação, pergunte antes de codificar.
- Valores dos placeholders:
  - `{{empresa}}`: `Claude` ou `ChatGPT`.
  - `{{modelo}}`: o modelo de IA usado, por exemplo `Haiku`, `Sonnet` ou `Opus`.
  - `{{geracao}}`: `r1`.
- Nos nomes de arquivo e de pasta, use minúsculas, sem acentos e com `_` no lugar de espaços. Por exemplo: `claude_sonnet_zeroshot_r1`, `chatgpt_luna_zeroshot_r2`.

## Entrada

- 27 arquivos ZIP, um por unidade da federação, nomeados `<UF>.zip` (AC a TO), na pasta D:\Projects\TCC\files.
- Cada ZIP contém um único arquivo CSV (nome do tipo `csv_licenciamento_<hash>.csv`; não dependa do nome).
- Codificação UTF-8. Separador de campos: barra vertical `|`. Todos os valores vêm entre aspas duplas; valor vazio é `""`; aspas dentro de valor são duplicadas (`""`).
- A primeira linha é o cabeçalho. Localize as colunas pelo nome do cabeçalho, não pela posição.
- Cada linha é uma emissão de uma estação (setor × frequência). A mesma estação aparece em várias linhas, e há linhas repetidas.

## Saída

- Um banco SQLite em D:\Projects\TCC\db\{{empresa}}_{{modelo}}_zeroshot_{{geracao}}.db, contendo exatamente as tabelas estacao e emissao, criadas com o esquema do arquivo D:\Projects\TCC\src\DDL.sql, sem alterações.
- Um projeto novo em .net que fica em D:\Projects\TCC\src\{{empresa}}_{{modelo}}_zeroshot_{{geracao}}.

## Data de referência

"Hoje", nas regras abaixo, é a data fixa 2026-10-15.

## 1. Leitura e limpeza geral

1. Remova espaços nas extremidades de todo valor.
2. Valor vazio (`""`) ou só com espaços vira `NULL`.
3. Números: aceite `.` ou `,` como separador decimal. Valor que não for número finito (texto como `NA`, `NaN`, infinito) vira `NULL`. O ponto e a vírgula são sempre separador decimal, nunca de milhar: `55` e `55.0` são o mesmo número, e `18.000` vale 18.
4. Datas: formato estrito `yyyy-MM-dd`. Qualquer outra coisa vira `NULL`.
5. Códigos e identificadores (`NumFistel`, `NumEstacao`, `NumAto`, `CodMunicipio`, `Cod*`, `Status.state`, `CodTipoClasseEstacao`) permanecem texto, sem converter para número (preserva zeros à esquerda).
6. Normalização de texto (usada em entidade e endereço): remover acentos (decomposição Unicode e descarte das marcas), converter para maiúsculas, trocar qualquer caractere que não seja letra ou dígito por um espaço, colapsar espaços repetidos e remover os das pontas. Resultado vazio vira `NULL`. Ex.: `Telefônica  Brasil S.A.` → `TELEFONICA BRASIL S A`.

## 2. Rejeição de linhas

Uma linha não é carregada se, após a limpeza, `NumFistel`, `NumEstacao` ou `SiglaUf` for nulo. Nenhuma outra condição de qualidade rejeita linha: ela é sinalizada (seção 5), nunca descartada.

## 3. Campos importados e seu tratamento

| Campo bruto | Coluna destino | Tratamento |
|---|---|---|
| `NumFistel` | `fistel` (estação e emissão) | texto limpo |
| `NumEstacao` | `num_estacao` (estação e emissão) | texto limpo |
| `NumAto` | `num_ato` | texto limpo |
| `SiglaUf` | `uf` (estação e emissão) | maiúsculas |
| `CodMunicipio` | `cod_municipio` | texto limpo |
| `Municipio.NomeMunicipio` | `municipio` | texto limpo (sem normalizar) |
| `NomeEntidade` | `entidade` | normalização de texto (regra 1.6) |
| `EnderecoEstacao` | `endereco` | normalização de texto (regra 1.6) |
| `Latitude` | `latitude` | número |
| `Longitude` | `longitude` | número |
| `Tecnologia` | `tecnologia` | maiúsculas |
| `DesignacaoEmissao` | `designacao_emissao` | maiúsculas |
| `FreqTxMHz` | `freq_tx_mhz` | número |
| `FreqRxMHz` | `freq_rx_mhz` | número |
| `Azimute` | `azimute` | número |
| `PotenciaTransmissorWatts` | `potencia_w` | número |
| `GanhoAntena` | `ganho_antena` | número |
| `AlturaAntena` | `altura_antena_m` | número |
| `AnguloElevacao` | `angulo_elevacao_graus` | número |
| `AnguloMeiaPotenciaAntena` | `angulo_meia_potencia_graus` | número |
| `FrenteCostaAntena` | `frente_costa_db` | número |
| `Polarizacao` | `polarizacao` | maiúsculas |
| `CodTipoClasseEstacao` | `classe_estacao` | texto limpo |
| `CodTipoAntena` | `cod_tipo_antena` | texto limpo |
| `CodEquipamentoAntena` | `cod_equipamento_antena` | texto limpo |
| `CodEquipamentoTransmissor` | `cod_equipamento_transmissor` | texto limpo |
| `Status.state` | `status` | texto limpo |
| `DataLicenciamento` | `data_licenciamento` | data |
| `DataPrimeiroLicenciamento` | `data_primeiro_licenciamento` | data |
| `DataValidade` | `data_validade` | data |

Os demais campos do arquivo bruto não são importados.

## 4. Colunas derivadas

Em todo arredondamento, a metade arredonda para longe do zero: 2,25 → 2,3; −2,25 → −2,3.

| Coluna | Regra |
|---|---|
| `operadora` | Aplicar, em ordem, sobre `entidade` normalizada; vale a primeira que casar; nenhuma ou entidade nula → `OUTRAS`: (1) contém a palavra `TELEFONICA` ou `VIVO` → `VIVO`; (2) contém a palavra `CLARO` ou `EMBRATEL` → `CLARO`; (3) começa com a palavra `TIM` → `TIM`; (4) começa com a palavra `OI`, ou contém a palavra `TELEMAR`, ou contém a expressão `BRASIL TELECOM` → `OI`; (5) contém a palavra `ALGAR` → `ALGAR`; (6) contém a palavra `SERCOMTEL` → `SERCOMTEL`; (7) contém a palavra `BRISANET` → `BRISANET`. "Palavra" significa delimitada por limite de palavra (`TIM` não casa dentro de `ESTIMATIVA`). Esta coluna fica na estação. |
| `geracao` | A partir de `tecnologia`, ignorando caixa: `GSM`, `CDMA` → `2G`; `WCDMA`, `WDCMA` (erro de digitação existente na base) → `3G`; `LTE` → `4G`; `NR` → `5G`; vazia/nula → `N/D`; qualquer outro valor → `OUTRA`. |
| `largura_banda_khz` | Dos quatro primeiros caracteres da designação (em maiúsculas): uma letra de unidade `H`, `K`, `M` ou `G` (não na primeira posição) separa a parte inteira da fracionária; as demais posições são dígitos. Valor = `inteiro.fração` × fator em kHz (`H` = 0,001; `K` = 1; `M` = 1000; `G` = 1.000.000). Inválido ou designação nula → `NULL`. `5M00G7W` → 5000; `10M0G7W` → 10000; `200KG7W` → 200; `2M50F9W` → 2500. |
| `duplex_mhz` | `freq_tx_mhz − freq_rx_mhz`, com sinal, arredondado a 3 casas; `NULL` se qualquer uma faltar. |
| `potencia_dbm` | `10 · log10(potencia_w · 1000)`, arredondado a 1 casa; `NULL` se `potencia_w` for nula ou ≤ 0. |
| `vencida` | Verdadeiro se `data_validade` não nula e anterior a Hoje; senão falso. |
| `anos_licenciada` | `(Hoje − data_primeiro_licenciamento)` em dias ÷ 365,25, arredondado a 1 casa; `NULL` se a data faltar. |
| `coordenada_valida` | Verdadeiro se latitude e longitude existem, não são ambas 0, e estão dentro do retângulo do Brasil (inclusive): latitude −33,75 a 5,28; longitude −73,99 a −28,83. Esta coluna fica na estação. |

## 5. Validações técnicas (colunas lógicas com três estados)

Cada validação é verdadeiro, falso ou `NULL`. `NULL` significa "sem dado suficiente" e não equivale a falso.

| Coluna | Regra |
|---|---|
| `azimute_valido` | `NULL` se `azimute` nulo; verdadeiro se 0 ≤ azimute ≤ 360; senão falso. |
| `elevacao_valida` | `NULL` se `angulo_elevacao_graus` nulo; verdadeiro se −90 ≤ valor ≤ 90; senão falso. |

## 6. Deduplicação e estações

- Estação: identificada por (`uf`, `fistel`, `num_estacao`). A UF entra na chave porque a mesma combinação (`fistel`, `num_estacao`) existe em UFs diferentes. Há uma linha por estação; os atributos vêm da primeira linha em que ela aparece (`cod_municipio`, `municipio`, `endereco`, `latitude`, `longitude`, `coordenada_valida`, `entidade`, `operadora`).
- Emissão duplicada: duas linhas com a mesma combinação de `uf`, `fistel`, `num_estacao`, `num_ato`, `azimute`, `freq_tx_mhz`, `freq_rx_mhz`, `designacao_emissao` e `tecnologia` (valores já tratados; `NULL` igual a `NULL`). Mantém-se a primeira; as demais não são carregadas.
- As colunas de emissão não listadas acima (por exemplo `polarizacao`, ângulos, códigos de homologação) não entram na chave de duplicidade.

## 7. Gravação

- Estação e emissão são carregadas em tabelas separadas, ligadas por (`uf`, `fistel`, `num_estacao`).
- Datas gravadas como texto `yyyy-MM-dd`; valores lógicos como `1`/`0`; `NULL` como `NULL`.

## Entrega

- Desenvolva a solução no projeto .NET vazio presente no espaço de trabalho. A biblioteca de acesso a dados fica a seu critério.
- O programa deve compilar e executar sem interação do usuário e gerar o banco de saída a partir de um banco vazio.
- A saída será comparada linha a linha com um resultado esperado. A solução é aceita quando ao menos 98% das linhas forem idênticas. Linhas ausentes, duplicadas ou a mais contam como erradas, você não deve fazer essa comparação.

## Execução

O programa será compilado e executado assim, a partir da pasta do projeto:

```
dotnet build D:\Projects\TCC\src\{{empresa}}_{{modelo}}_zeroshot_{{geracao}} -c Release
dotnet run --project D:\Projects\TCC\src\{{empresa}}_{{modelo}}_zeroshot_{{geracao}} -c Release --no-build
```

- A pasta do projeto deve conter um único arquivo `.csproj`.
- O programa é executado sem argumentos. Os caminhos de entrada e saída são os das seções Entrada e Saída.
- O banco de saída é apagado antes de cada execução; o programa deve criá-lo a partir do `DDL.sql`.
- Em caso de sucesso, o programa termina com código 0. Em caso de falha, termina com código diferente de 0 e escreve a mensagem de erro na saída de erro (`stderr`).
- A execução tem limite de 2 horas; depois disso, é interrompida e considerada falha.
