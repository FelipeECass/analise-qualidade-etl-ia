import pandas as pd
from scipy.stats import mannwhitneyu, wilcoxon, kruskal
m=pd.read_csv('sonar_consolidado.csv',sep=';')
c=pd.read_csv('custo_estimado.csv',sep=';'); c['cond']=c.condicao.map({'completo':'c','zero_shot':'zs'}); c['r']=c.execucao.str[-1].astype(int)
m=m.merge(c[['modelo','cond','r','custo_usd']],on=['modelo','cond','r'])
k=pd.read_csv('corretude_final.csv',sep=';'); k['cond']=k.condicao.map({'completo':'c','zero_shot':'zs'}); k=k.rename(columns={'rodada':'r'})
m=m.merge(k[['modelo','cond','r','taxa_corrigida','divergentes_corrigido']],on=['modelo','cond','r'])
m.to_csv('consolidado_36.csv',sep=';',index=False)
for col in ['complexity','cc_por_metodo','cognitive_complexity','cog_por_metodo','functions','ncloc','custo_usd','taxa_corrigida']:
    a=m[m.cond=='c'].sort_values(['modelo','r'])[col].values; b=m[m.cond=='zs'].sort_values(['modelo','r'])[col].values
    mw=mannwhitneyu(a,b).pvalue; wx=wilcoxon(a,b).pvalue
    kw=kruskal(*[g[col].values for _,g in m.groupby('modelo')]).pvalue
    print(f"{col:22s} med_c={pd.Series(a).median():9.4f} med_zs={pd.Series(b).median():9.4f} MW p={mw:.3f} Wilcoxon p={wx:.3f} | KW modelo p={kw:.4f}")
