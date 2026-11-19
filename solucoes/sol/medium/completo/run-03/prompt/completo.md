Você é um engenheiro de software sênior, especialista em engenharia de dados e no desenvolvimento de pipelines ETL em C#/.NET.

## Tarefa

Desenvolva, em C#, .NET 10.0.404, um programa de console que implemente um pipeline ETL (extração, transformação e carga) sobre os dados públicos de licenciamento de estações da Anatel, conforme a especificação abaixo.

## Regras

- Trabalhe somente dentro de `D:\Projects\TCC`. Não acesse nenhuma outra pasta, incluindo `D:\Projects\TCC_GitHub`.
- Não use, copie, consulte nem reaproveite o código em `D:\Projects\TCC\src\etl-base`. Ele não faz parte da solução. Implemente tudo a partir do projeto .NET vazio do espaço de trabalho.
- Se houver qualquer dúvida necessária para a implementação, pergunte antes de codificar.
- Valores dos placeholders:
  - `{{empresa}}`: `Claude` ou `ChatGPT`.
  - `{{modelo}}`: o modelo de IA usado, por exemplo `Haiku`, `Sonnet` ou `Opus`.
  - `{{geracao}}`: `r3`.
- Nos nomes de arquivo e de pasta, use minúsculas, sem acentos e com `_` no lugar de espaços. Por exemplo: `claude_sonnet_completo_r1`, `chatgpt_luna_completo_r2`.

## Entrada

- 27 arquivos ZIP, um por unidade da federação, nomeados `<UF>.zip` (AC a TO), na pasta D:\Projects\TCC\files.
- Cada ZIP contém um único arquivo CSV (nome do tipo `csv_licenciamento_<hash>.csv`; não dependa do nome).
- Codificação UTF-8. Separador de campos: barra vertical `|`. Todos os valores vêm entre aspas duplas; valor vazio é `""`; aspas dentro de valor são duplicadas (`""`).
- A primeira linha é o cabeçalho. Localize as colunas pelo nome do cabeçalho, não pela posição.
- Cada linha é uma emissão de uma estação (setor × frequência). A mesma estação aparece em várias linhas, e há linhas repetidas.

## Saída

- Um banco SQLite em D:\Projects\TCC\db\{{empresa}}_{{modelo}}_completo_{{geracao}}.db, contendo exatamente as tabelas estacao e emissao, criadas com o esquema do arquivo D:\Projects\TCC\src\DDL.sql, sem alterações.
- Um projeto novo em .net que fica em D:\Projects\TCC\src\{{empresa}}_{{modelo}}_completo_{{geracao}}.

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

## Exemplos

A seguir, linhas reais do arquivo bruto e as linhas correspondentes esperadas nas tabelas estacao e emissao.

Cabeçalho do arquivo bruto:

```
Status.state|NomeEntidade|NumFistel|NumServico|NumAto|NumEstacao|EnderecoEstacao|EndComplemento|SiglaUf|CodMunicipio|DesignacaoEmissao|Tecnologia|tipoTecnologia|meioAcesso|FreqTxMHz|FreqRxMHz|Azimute|CodTipoClasseEstacao|ClassInfraFisica|CompartilhamentoInfraFisica|CodTipoAntena|CodEquipamentoAntena|GanhoAntena|FrenteCostaAntena|AnguloMeiaPotenciaAntena|AnguloElevacao|Polarizacao|AlturaAntena|CodEquipamentoTransmissor|PotenciaTransmissorWatts|Latitude|Longitude|CodDebitoTFI|DataLicenciamento|DataPrimeiroLicenciamento|NumRede|_id|DataValidade|NumFistelAssociado|NomeEntidadeAssociado|Municipio.NomeMunicipio
```

### Exemplo 1

Linha do arquivo bruto:

```
"LIC-LIC-01"|"TELEFONICA BRASIL S.A."|"50409146285"|"010"|"48412023"|"442820500"|"AVENIDA CENTENÁRIO. LOTE 277"|""|"RR"|"1400100"|"5M00G7W"|"WCDMA"|""|""|"2160"|"1970"|"120"|"FB"|""|"NA"|"760"|"007690501705"|"15"|"30"|"87"|"6"|"X"|"53"|"008041101882"|"40"|"2.79944"|"-60.71028"|"G"|"2023-08-21"|"1999-11-23"|""|"4d5c019fa88de"|"2038-04-30"|""|""|"Boa Vista"
```

Linha esperada em estacao:

```
fistel = 50409146285
num_estacao = 442820500
uf = RR
cod_municipio = 1400100
municipio = Boa Vista
endereco = AVENIDA CENTENARIO LOTE 277
latitude = 2.79944
longitude = -60.71028
coordenada_valida = 1
entidade = TELEFONICA BRASIL S A
operadora = VIVO
```

Linha esperada em emissao (sem a coluna id, gerada pelo banco):

```
uf = RR
fistel = 50409146285
num_estacao = 442820500
num_ato = 48412023
tecnologia = WCDMA
geracao = 3G
designacao_emissao = 5M00G7W
largura_banda_khz = 5000.0
freq_tx_mhz = 2160.0
freq_rx_mhz = 1970.0
duplex_mhz = 190.0
azimute = 120.0
potencia_w = 40.0
ganho_antena = 15.0
altura_antena_m = 53.0
data_licenciamento = 2023-08-21
data_primeiro_licenciamento = 1999-11-23
data_validade = 2038-04-30
vencida = 0
anos_licenciada = 26.9
status = LIC-LIC-01
classe_estacao = FB
polarizacao = X
angulo_elevacao_graus = 6.0
angulo_meia_potencia_graus = 87.0
frente_costa_db = 30.0
cod_tipo_antena = 760
cod_equipamento_antena = 007690501705
cod_equipamento_transmissor = 008041101882
potencia_dbm = 46.0
azimute_valido = 1
elevacao_valida = 1
```

### Exemplo 2

Linha do arquivo bruto:

```
"LIC-LIC-01"|"MINISTÉRIO DA JUSTIÇA - DEPARTAMENTO DE POLICIA ROD. FEDERAL"|"50408478667"|"023"|"19742012"|"1003969574"|"BR 174  km 600 SN  AMAJARI"|""|"RR"|"1400027"|"25K0D7WDT"|""|""|""|"380.65"|"390.65"|""|"FB"|""|""|"86"|"016381309110"|"8.1"|""|""|""|"V"|"55.0"|"033761101152"|"10.000"|"3.6021944444443"|"-60.961194444444"|"A"|"2017-05-16"|"2017-05-16"|"3500"|"62db2001630eb"|"2032-04-11"|""|""|"Amajari"
```

Linha esperada em estacao:

```
fistel = 50408478667
num_estacao = 1003969574
uf = RR
cod_municipio = 1400027
municipio = Amajari
endereco = BR 174 KM 600 SN AMAJARI
latitude = 3.6021944444443
longitude = -60.961194444444
coordenada_valida = 1
entidade = MINISTERIO DA JUSTICA DEPARTAMENTO DE POLICIA ROD FEDERAL
operadora = OUTRAS
```

Linha esperada em emissao (sem a coluna id, gerada pelo banco):

```
uf = RR
fistel = 50408478667
num_estacao = 1003969574
num_ato = 19742012
tecnologia = NULL
geracao = N/D
designacao_emissao = 25K0D7WDT
largura_banda_khz = 25.0
freq_tx_mhz = 380.65
freq_rx_mhz = 390.65
duplex_mhz = -10.0
azimute = NULL
potencia_w = 10.0
ganho_antena = 8.1
altura_antena_m = 55.0
data_licenciamento = 2017-05-16
data_primeiro_licenciamento = 2017-05-16
data_validade = 2032-04-11
vencida = 0
anos_licenciada = 9.4
status = LIC-LIC-01
classe_estacao = FB
polarizacao = V
angulo_elevacao_graus = NULL
angulo_meia_potencia_graus = NULL
frente_costa_db = NULL
cod_tipo_antena = 86
cod_equipamento_antena = 016381309110
cod_equipamento_transmissor = 033761101152
potencia_dbm = 40.0
azimute_valido = NULL
elevacao_valida = NULL
```

### Exemplo 3

Linhas do arquivo bruto (mesma estação):

```
"LIC-LIC-01"|"CLARO S.A."|"50418766738"|"019"|"123122021"|"1013264735"|"R ALBINO TAVARES"|""|"RR"|"1400027"|"56M0D7W"|""|""|""|"6845"|"6505"|"138"|"FX"|""|""|"655"|"030791200324"|"38.8"|"65"|"1.8"|"-0.12"|"V"|"88"|"008241102620"|"1.2589"|"3.65333"|"-61.41733"|"C"|"2022-07-08"|"2022-07-08"|"508808"|"4310d24800bff24a"|"2039-11-28"|""|""|"Amajari"
"LIC-LIC-01"|"CLARO S.A."|"50418766738"|"019"|"123122021"|"1013264735"|"R ALBINO TAVARES"|""|"RR"|"1400027"|"56M0D7W"|""|""|""|"6905"|"6565"|"138"|"FX"|""|""|"655"|"030791200324"|"38.8"|"65"|"1.8"|"-0.12"|"H"|"88"|"008241102620"|"1.2589"|"3.65333"|"-61.41733"|"C"|"2022-07-08"|"2022-07-08"|"508808"|"4310d24800bff24c"|"2039-11-28"|""|""|"Amajari"
```

Linha esperada em estacao:

```
fistel = 50418766738
num_estacao = 1013264735
uf = RR
cod_municipio = 1400027
municipio = Amajari
endereco = R ALBINO TAVARES
latitude = 3.65333
longitude = -61.41733
coordenada_valida = 1
entidade = CLARO S A
operadora = CLARO
```

Linhas esperadas em emissao (sem a coluna id, gerada pelo banco):

```
uf = RR
fistel = 50418766738
num_estacao = 1013264735
num_ato = 123122021
tecnologia = NULL
geracao = N/D
designacao_emissao = 56M0D7W
largura_banda_khz = 56000.0
freq_tx_mhz = 6845.0
freq_rx_mhz = 6505.0
duplex_mhz = 340.0
azimute = 138.0
potencia_w = 1.2589
ganho_antena = 38.8
altura_antena_m = 88.0
data_licenciamento = 2022-07-08
data_primeiro_licenciamento = 2022-07-08
data_validade = 2039-11-28
vencida = 0
anos_licenciada = 4.3
status = LIC-LIC-01
classe_estacao = FX
polarizacao = V
angulo_elevacao_graus = -0.12
angulo_meia_potencia_graus = 1.8
frente_costa_db = 65.0
cod_tipo_antena = 655
cod_equipamento_antena = 030791200324
cod_equipamento_transmissor = 008241102620
potencia_dbm = 31.0
azimute_valido = 1
elevacao_valida = 1

uf = RR
fistel = 50418766738
num_estacao = 1013264735
num_ato = 123122021
tecnologia = NULL
geracao = N/D
designacao_emissao = 56M0D7W
largura_banda_khz = 56000.0
freq_tx_mhz = 6905.0
freq_rx_mhz = 6565.0
duplex_mhz = 340.0
azimute = 138.0
potencia_w = 1.2589
ganho_antena = 38.8
altura_antena_m = 88.0
data_licenciamento = 2022-07-08
data_primeiro_licenciamento = 2022-07-08
data_validade = 2039-11-28
vencida = 0
anos_licenciada = 4.3
status = LIC-LIC-01
classe_estacao = FX
polarizacao = H
angulo_elevacao_graus = -0.12
angulo_meia_potencia_graus = 1.8
frente_costa_db = 65.0
cod_tipo_antena = 655
cod_equipamento_antena = 030791200324
cod_equipamento_transmissor = 008241102620
potencia_dbm = 31.0
azimute_valido = 1
elevacao_valida = 1
```

### Exemplo 4

Linha do arquivo bruto:

```
"LIC-LIC-01"|"JNET TECNOLOGIA E TELECOMUNICACAO LTDA"|"50442919867"|"019"|"118512022"|"1014179294"|"Acesso pelo Rio Envirá."|"Região de Floresta"|"AC"|"1200302"|"40M0D7W"|""|""|""|"6700"|"7040"|"205,79"|"FX"|""|""|"655"|"006801702593"|"36.7"|"57"|"3.2"|"-0.1"|"x"|"24"|"018871701397"|"0.199"|"-7.975978"|"-70.252103"|"C"|"2022-09-01"|"2022-09-01"|"1"|"c825c5f80a312db7"|"2042-08-10"|""|""|"Feijó"
```

Linha esperada em estacao:

```
fistel = 50442919867
num_estacao = 1014179294
uf = AC
cod_municipio = 1200302
municipio = Feijó
endereco = ACESSO PELO RIO ENVIRA
latitude = -7.975978
longitude = -70.252103
coordenada_valida = 1
entidade = JNET TECNOLOGIA E TELECOMUNICACAO LTDA
operadora = OUTRAS
```

Linha esperada em emissao (sem a coluna id, gerada pelo banco):

```
uf = AC
fistel = 50442919867
num_estacao = 1014179294
num_ato = 118512022
tecnologia = NULL
geracao = N/D
designacao_emissao = 40M0D7W
largura_banda_khz = 40000.0
freq_tx_mhz = 6700.0
freq_rx_mhz = 7040.0
duplex_mhz = -340.0
azimute = 205.79
potencia_w = 0.199
ganho_antena = 36.7
altura_antena_m = 24.0
data_licenciamento = 2022-09-01
data_primeiro_licenciamento = 2022-09-01
data_validade = 2042-08-10
vencida = 0
anos_licenciada = 4.1
status = LIC-LIC-01
classe_estacao = FX
polarizacao = X
angulo_elevacao_graus = -0.1
angulo_meia_potencia_graus = 3.2
frente_costa_db = 57.0
cod_tipo_antena = 655
cod_equipamento_antena = 006801702593
cod_equipamento_transmissor = 018871701397
potencia_dbm = 23.0
azimute_valido = 1
elevacao_valida = 1
```

## Raciocínio

Antes de escrever qualquer código, siga exatamente as etapas abaixo e registre o plano resultante no arquivo PLANO.md. Só inicie a implementação após concluir todas as etapas.

1. Compreensão da fonte: examine os arquivos de entrada e uma amostra real dos dados. Identifique o separador, o uso de aspas, o encoding e a quebra de linha, e liste as colunas do arquivo bruto.
2. Mapeamento: para cada coluna das tabelas estacao e emissao, identifique o campo bruto de origem e o tratamento exigido pela especificação.
3. Separadores decimais: identifique, na amostra, os formatos numéricos presentes e defina como convertê-los conforme a regra de tipagem.
4. Designação de emissão: defina como decodificar os quatro primeiros caracteres da designação em largura de banda em kHz.
5. Validações de três estados: para cada coluna de validação, defina exatamente quando o valor é 1, 0 ou nulo.
6. Casos de borda: para cada transformação, enumere os casos problemáticos que podem ocorrer nos dados e decida o tratamento, em conformidade com a especificação. Quando a especificação não cobrir um caso, registre a suposição adotada.
7. Organização da solução: decida como estruturar o código (componentes, responsabilidades e fluxo de dados) e justifique a decisão.
8. Verificação: defina como você confirmará que a saída respeita o esquema e as regras antes de considerar a tarefa concluída.
9. Implemente a solução e, ao final, revise o código contra o PLANO.md.

## Entrega

- Desenvolva a solução no projeto .NET vazio presente no espaço de trabalho. A biblioteca de acesso a dados fica a seu critério.
- O programa deve compilar e executar sem interação do usuário e gerar o banco de saída a partir de um banco vazio.
- A saída será comparada linha a linha com um resultado esperado. A solução é aceita quando ao menos 98% das linhas forem idênticas. Linhas ausentes, duplicadas ou a mais contam como erradas, você não deve fazer essa comparação.

## Execução

O programa será compilado e executado assim, a partir da pasta do projeto:

```
dotnet build D:\Projects\TCC\src\{{empresa}}_{{modelo}}_completo_{{geracao}} -c Release
dotnet run --project D:\Projects\TCC\src\{{empresa}}_{{modelo}}_completo_{{geracao}} -c Release --no-build
```

- A pasta do projeto deve conter um único arquivo `.csproj`.
- O programa é executado sem argumentos. Os caminhos de entrada e saída são os das seções Entrada e Saída.
- O banco de saída é apagado antes de cada execução; o programa deve criá-lo a partir do `DDL.sql`.
- Em caso de sucesso, o programa termina com código 0. Em caso de falha, termina com código diferente de 0 e escreve a mensagem de erro na saída de erro (`stderr`).
- A execução tem limite de 2 horas; depois disso, é interrompida e considerada falha.
