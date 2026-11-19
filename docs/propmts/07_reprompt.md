A solução não atingiu o critério de aceitação. Corrija-a com base no resultado da verificação abaixo.

{{SE HOUVER ERRO DE COMPILAÇÃO OU EXECUÇÃO}}
Erros de compilação ou execução:

{{mensagens de erro na íntegra}}
{{FIM}}

{{SE O ESQUEMA DIVERGIR DO DDL}}
Diferenças entre o esquema do banco e o DDL.sql:

- {{diferença}}
{{repetir para cada diferença}}
{{FIM}}

{{SE HOUVER LINHAS AUSENTES OU EXCEDENTES}}
Contagem de linhas:

Tabela {{tabela}}: {{linhas_ausentes}} linhas ausentes; {{linhas_excedentes}} linhas excedentes ou duplicadas
Exemplos de linhas ausentes (chave):
- {{chave}}
{{até 5 exemplos}}
Exemplos de linhas excedentes ou duplicadas (chave):
- {{chave}}
{{até 5 exemplos; as repetições vêm marcadas com "(repetição de uma linha já presente)"}}

{{repetir para cada tabela com diferença}}
{{FIM}}

{{SE HOUVER LINHAS DIVERGENTES}}
Colunas que diferem do resultado esperado:

Tabela {{tabela}}, coluna {{coluna}}: {{numero_de_divergencias}} divergências
Exemplos:
- linha {{chave}}: esperado: {{valor_esperado}} | obtido: {{valor_obtido}}
{{até 5 exemplos por coluna}}

{{repetir para cada coluna divergente}}
{{FIM}}

{{SE HOUVER AVISOS SOBRE O ESQUEMA}}
Avisos sobre o esquema (não reprovam a solução, mas diferem do DDL.sql):

- {{aviso}}
{{FIM}}
