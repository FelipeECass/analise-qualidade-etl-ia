# Análise dos resultados (14/11/2026)

Rodar nesta ordem, a partir desta pasta (Python 3, pandas, scipy):

1. `01_gabarito_duplex.py <base.db> duplex_corrigido.db`: linhas de *emissao* em que o `duplex_mhz` do gabarito difere da regra da especificação (decimal exato, 3 casas, metade para longe do zero). São 7.438.
2. `02_reavaliar_duplex.py duplex_corrigido.db <bancos das soluções...>` (rodar de `runs/`): grava `reavaliacao_duplex.tsv`.
3. `03_corretude.py`: grava `corretude_final.csv` e `divergencias_coluna.csv`, a partir dos JSON do Comparador e da reavaliação.
4. `04_custo.py`: grava `custo_estimado.csv`, com preços de lista da API consultados em 14/11/2026.
5. `05_estatistica.py`: testes de Mann-Whitney (condição) e Kruskal-Wallis (modelo); grava `consolidado_36.csv`.

Dados do SonarQube (API, versão 26.11, perfil Sonar way core): `sonar_extra.csv`, `sonar_regras.txt`, `sonar_consolidado.csv`.
