# Plano — chatgpt_gpt_6_completo_r3

Plano concluído antes da implementação. Data de referência: 2026-10-15.

## 1. Compreensão da fonte

Ambiente Linux: /workspace representa D:\Projects\TCC. Não existe projeto vazio em src; será criado um novo. Não consultar etl-base. Usar ChatGPT/GPT-6/r3 nos nomes, em minúsculas e com sublinhados.
Inspecionados os 27 ZIPs: cada um contém um CSV, de nome variável. Amostras reais de AC, RR e SP confirmam UTF-8 sem BOM, CRLF, separador |, cabeçalho sem aspas e valores entre aspas; parser aceitará BOM, aspas duplicadas, separadores e quebras de linha dentro de campos citados. DDL.sql usa CRLF real e será executado sem alteração.

Colunas brutas (ordem observada, não usada para mapear):
Status.state, NomeEntidade, NumFistel, NumServico, NumAto, NumEstacao,
EnderecoEstacao, EndComplemento, SiglaUf, CodMunicipio, DesignacaoEmissao,
Tecnologia, tipoTecnologia, meioAcesso, FreqTxMHz, FreqRxMHz, Azimute,
CodTipoClasseEstacao, ClassInfraFisica, CompartilhamentoInfraFisica,
CodTipoAntena, CodEquipamentoAntena, GanhoAntena, FrenteCostaAntena,
AnguloMeiaPotenciaAntena, AnguloElevacao, Polarizacao, AlturaAntena,
CodEquipamentoTransmissor, PotenciaTransmissorWatts, Latitude, Longitude,
CodDebitoTFI, DataLicenciamento, DataPrimeiroLicenciamento, NumRede, _id,
DataValidade, NumFistelAssociado, NomeEntidadeAssociado, Municipio.NomeMunicipio.

## 2. Mapeamento completo

Todo texto recebe Trim; vazio vira NULL. Identificadores nunca são convertidos.

| Tabela | Destino | Origem/tratamento |
|---|---|---|
| ambas | uf | SiglaUf, maiúsculas invariantes |
| ambas | fistel | NumFistel, texto |
| ambas | num_estacao | NumEstacao, texto |
| estacao | cod_municipio | CodMunicipio, texto |
| estacao | municipio | Municipio.NomeMunicipio, texto sem normalização |
| estacao | endereco | EnderecoEstacao, normalização Unicode |
| estacao | latitude | Latitude, número finito |
| estacao | longitude | Longitude, número finito |
| estacao | coordenada_valida | par existente, não (0,0), latitude [-33.75,5.28], longitude [-73.99,-28.83]; 1 ou 0 |
| estacao | entidade | NomeEntidade, normalização Unicode |
| estacao | operadora | entidade, primeira regra: TELEFONICA/VIVO; CLARO/EMBRATEL; início TIM; início OI/TELEMAR/BRASIL TELECOM; ALGAR; SERCOMTEL; BRISANET; OUTRAS |
| emissao | id | gerado pelo SQLite |
| emissao | num_ato | NumAto, texto |
| emissao | tecnologia | Tecnologia, maiúsculas |
| emissao | geracao | GSM/CDMA=2G; WCDMA/WDCMA=3G; LTE=4G; NR=5G; NULL=N/D; demais=OUTRA |
| emissao | designacao_emissao | DesignacaoEmissao, maiúsculas |
| emissao | largura_banda_khz | quatro caracteres iniciais, conforme seção 4 |
| emissao | freq_tx_mhz | FreqTxMHz, número |
| emissao | freq_rx_mhz | FreqRxMHz, número |
| emissao | duplex_mhz | TX-RX, 3 casas; NULL se faltar operando |
| emissao | azimute | Azimute, número |
| emissao | potencia_w | PotenciaTransmissorWatts, número |
| emissao | ganho_antena | GanhoAntena, número |
| emissao | altura_antena_m | AlturaAntena, número |
| emissao | data_licenciamento | DataLicenciamento, data estrita |
| emissao | data_primeiro_licenciamento | DataPrimeiroLicenciamento, data estrita |
| emissao | data_validade | DataValidade, data estrita |
| emissao | vencida | validade anterior a 2026-10-15; NULL ou igualdade=0 |
| emissao | anos_licenciada | dias desde primeiro licenciamento / 365.25, 1 casa; NULL sem data |
| emissao | status | Status.state, texto |
| emissao | classe_estacao | CodTipoClasseEstacao, texto |
| emissao | polarizacao | Polarizacao, maiúsculas |
| emissao | angulo_elevacao_graus | AnguloElevacao, número |
| emissao | angulo_meia_potencia_graus | AnguloMeiaPotenciaAntena, número |
| emissao | frente_costa_db | FrenteCostaAntena, número |
| emissao | cod_tipo_antena | CodTipoAntena, texto |
| emissao | cod_equipamento_antena | CodEquipamentoAntena, texto |
| emissao | cod_equipamento_transmissor | CodEquipamentoTransmissor, texto |
| emissao | potencia_dbm | 10*log10(watts*1000), 1 casa; NULL para ausente ou <=0 |
| emissao | azimute_valido | seção 5 |
| emissao | elevacao_valida | seção 5 |

Normalização: decomposição FormD, remoção de marcas Unicode, maiúsculas, letras/dígitos preservados, demais caracteres substituídos por um espaço; espaços colapsados; vazio=NULL. Operadoras por limites de palavra, respeitando a ordem especificada.

## 3. Separadores decimais

Amostras contêm inteiros (40), ponto decimal (32.6, 874.5, -9.972025) e zeros finais (20.0). O exemplo fornecido contém vírgula (205,79). Substituir vírgula por ponto e usar cultura invariante, NumberStyles.Float sem separador de milhar. Assim 18.000=18, 55=55.0. Rejeitar NULL, NA, NaN, infinitos, overflow e formatos com dois separadores como números (retornar NULL). Notação exponencial finita será aceita como número; não há regra proibindo-a.

## 4. Designação de emissão

Exigir ao menos 4 caracteres. Nos quatro primeiros, exatamente uma unidade H/K/M/G na posição 1, 2 ou 3 (índice base zero); restantes devem ser dígitos ASCII. Formar inteiro e fração, permitindo fração vazia quando unidade é última. Multiplicar por 0.001, 1, 1000 ou 1000000 kHz. Ignorar sufixo. Exemplos: 5M00=5000, 10M0=10000, 200K=200, 2M50=2500. Padrão inválido=NULL.

## 5. Validações com três estados

azimute_valido: NULL se número ausente/inválido; 1 no intervalo fechado [0,360]; 0 fora.
elevacao_valida: NULL se número ausente/inválido; 1 no intervalo fechado [-90,90]; 0 fora.
coordenada_valida e vencida têm apenas 1/0, conforme mapeamento; ausência resulta 0.

## 6. Casos de borda e decisões

- Rejeitar somente linha sem NumFistel, NumEstacao ou SiglaUf após limpeza; demais problemas viram NULL ou indicadores.
- Datas somente yyyy-MM-dd válidas no calendário, sem horários; datas futuras permitidas e anos podem ser negativos.
- Arredondamento sempre MidpointRounding.AwayFromZero. Usar decimal na subtração quando representável para evitar artefatos binários nas metades; logaritmo usa double. Derivados não finitos viram NULL.
- Preservar zeros em códigos; município preserva acentos; estado/tecnologia/designação/polarização em maiúsculas.
- Comparação ordinal; palavra TIM não casa em ESTIMATIVA. Prioridade de operadoras é obrigatória.
- Estação usa chave (uf,fistel,num_estacao) e atributos da primeira linha aceita, sem atualização posterior.
- Emissão usa chave tratada (uf,fistel,num_estacao,num_ato,azimute,TX,RX,designação,tecnologia), com NULL=NULL, igualdade numérica (55=55.0), mantendo primeira. Polarização e outros dados não participam.
- Processar ZIPs na ordem alfabética da UF e linhas na ordem do CSV. A especificação não define ordem entre ZIPs; esta escolha assegura determinismo inclusive para UF inesperada dentro de um arquivo.
- Cabeçalhos obrigatórios ausentes/duplicados, CSV estruturalmente inválido, ZIP com quantidade diferente de um CSV e problemas de I/O causam falha explícita, sem descartar silenciosamente registros. Falha elimina o banco parcial.
- Exatamente as duas tabelas de domínio do DDL; sqlite_sequence é tabela interna necessária ao AUTOINCREMENT do próprio DDL.
- Não existe código preexistente de projeto no espaço de trabalho; criar projeto mínimo net10.0 com único csproj, SDK 10.0.404 via global.json. Raiz encontrada a partir do diretório do executável, identificando src/DDL.sql e files; fallback Windows D:\Projects\TCC.

## 7. Organização e fluxo

Program resolve caminhos, trata falhas e código de saída. CsvSource usa TextFieldParser (biblioteca padrão) para CSV citado e streaming UTF-8 estrito. Transform concentra funções puras. Pipeline gerencia chaves em HashSet globais, estações/emissões e parâmetros preparados SQLite via Microsoft.Data.Sqlite, transações em lotes e foreign_keys ativado. Chaves globais preservam deduplicação mesmo com UF diferente do ZIP. Leitura streaming evita descompactar os ~3 GB de dados em disco. Um processo sequencial preserva primeira ocorrência. Logs por UF e contadores finais para auditoria.

## 8. Verificação prevista

Compilar Release com SDK disponível. Exercitar funções com casos direcionados: acentos, limites de palavras/prioridade, nulos, vírgulas, finitude, datas estritas, bandas válidas/inválidas, metades positivas/negativas, intervalos inclusivos e data fixa. Exercitar CSV com cabeçalho reordenado, aspas escapadas, separador/linha dentro de valor; deduplicação com nulos, números equivalentes, atributos divergentes e UF diferente. Executar sem argumentos sobre todos os 27 ZIPs. Verificar SQLite integrity_check, foreign_key_check, tabelas/colunas/índices contra DDL original, totais e invariantes SQL. Não comparar linha a linha com resultado esperado. Registrar resultados reais e revisar implementação contra este plano.

## 9. Implementação e revisão

Somente iniciar código depois de gravar este plano. Ao concluir, registrar comandos, resultados e limitações em VERIFICACAO.md.
