A solução não atingiu o critério de aceitação. Corrija-a com base no resultado da verificação abaixo.

Colunas que diferem do resultado esperado:

Tabela emissao, coluna largura_banda_khz: 3628164 divergências
Exemplos:
- linha uf="AL", fistel="50409428698", num_estacao="2119", num_ato="48352023", azimute=35, freq_tx_mhz=2130, freq_rx_mhz=1940, designacao_emissao="5M00G7W", tecnologia="WCDMA": esperado: 5000 | obtido: NULL
- linha uf="AL", fistel="50409428698", num_estacao="2119", num_ato="48352023", azimute=155, freq_tx_mhz=2130, freq_rx_mhz=1940, designacao_emissao="5M00G7W", tecnologia="WCDMA": esperado: 5000 | obtido: NULL
- linha uf="AL", fistel="50409428698", num_estacao="2119", num_ato="48352023", azimute=275, freq_tx_mhz=2130, freq_rx_mhz=1940, designacao_emissao="5M00G7W", tecnologia="WCDMA": esperado: 5000 | obtido: NULL
- linha uf="AL", fistel="50409428698", num_estacao="2119", num_ato="125002022", azimute=35, freq_tx_mhz=874.5, freq_rx_mhz=829.5, designacao_emissao="5M00G7W", tecnologia="WCDMA": esperado: 5000 | obtido: NULL
- linha uf="AL", fistel="50409428698", num_estacao="2119", num_ato="125002022", azimute=275, freq_tx_mhz=874.5, freq_rx_mhz=829.5, designacao_emissao="5M00G7W", tecnologia="WCDMA": esperado: 5000 | obtido: NULL

Tabela emissao, coluna duplex_mhz: 2375 divergências
Exemplos:
- linha uf="DF", fistel="50450314308", num_estacao="623298040", num_ato="388902003", azimute=0, freq_tx_mhz=0.1345, freq_rx_mhz=0, designacao_emissao="500HJ3E", tecnologia=NULL: esperado: 0.134 | obtido: 0.135
- linha uf="ES", fistel="50003817504", num_estacao="698079361", num_ato="902020", azimute=0, freq_tx_mhz=158.13125, freq_rx_mhz=163.19375, designacao_emissao="8K10F1E", tecnologia=NULL: esperado: -5.062 | obtido: -5.063
- linha uf="ES", fistel="50003817504", num_estacao="699002184", num_ato="902020", azimute=0, freq_tx_mhz=158.13125, freq_rx_mhz=163.19375, designacao_emissao="12K5F1E", tecnologia=NULL: esperado: -5.062 | obtido: -5.063
- linha uf="ES", fistel="50003817504", num_estacao="1002978510", num_ato="902020", azimute=0, freq_tx_mhz=158.13125, freq_rx_mhz=163.19375, designacao_emissao="8K10F1E", tecnologia=NULL: esperado: -5.062 | obtido: -5.063
- linha uf="ES", fistel="50003817504", num_estacao="1002605730", num_ato="902020", azimute=0, freq_tx_mhz=158.13125, freq_rx_mhz=163.19375, designacao_emissao="12K5F1E", tecnologia=NULL: esperado: -5.062 | obtido: -5.063
