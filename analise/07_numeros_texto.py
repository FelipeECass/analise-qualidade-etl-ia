import pandas as pd, numpy as np, json, glob, re, os, sqlite3, math
from scipy.stats import kruskal, mannwhitneyu
def cliff(a,b):
    a,b=np.asarray(a),np.asarray(b); return (sum(int(x>y)-int(x<y) for x in a for y in b))/(len(a)*len(b))
porte={'haiku':'entrada','luna':'entrada','sonnet':'inter','terra':'inter','opus':'maior','sol':'maior'}
k=pd.read_csv('corretude_final.csv',sep=';'); k['porte']=k.modelo.map(porte)
# 1a solucao: taxa corrigida quando a 1a foi a aceita; senao, a original (falhas)
R=os.path.expanduser('~/mnt/TCC/resultados')
prim=[]
for _,r in k.iterrows():
    js=sorted(glob.glob(f"{R}/{r.modelo}/medium/{r.condicao}/run-0{r.rodada}/*/*_i*.json"), key=lambda p:int(re.search(r"_i(\d+)\.json$",p).group(1)))
    t1=json.load(open(js[0],encoding='utf-8-sig'))['resultado']['taxa']
    prim.append(r.taxa_corrigida if r.iteracao_aceita==1 else t1)
k['taxa_primeira']=prim
print("menor taxa por modelo x cond (%):"); print(np.floor(k.groupby(["modelo","condicao"]).taxa_corrigida.min()*10000)/100); print(k.groupby(["modelo","condicao"]).taxa_corrigida.min().unstack())
print("identicas por modelo:", k[k.divergentes_corrigido==0].groupby('modelo').size().to_dict())
c=k[k.condicao=='completo']
for col in ['taxa_primeira','taxa_corrigida']:
    for g in ['modelo','porte']:
        h=kruskal(*[x[col].values for _,x in c.groupby(g)]); print(f"KW completo {col} por {g}: H={h.statistic:.2f} p={h.pvalue:.3f}")
a=k[k.condicao=='completo'].taxa_primeira; b=k[k.condicao=='zero_shot'].taxa_primeira
print("MW primeira c x zs p=%.3f cliff=%.2f"%(mannwhitneyu(a,b).pvalue, cliff(a,b)))
a=k[k.condicao=='completo'].taxa_corrigida; b=k[k.condicao=='zero_shot'].taxa_corrigida
print("MW final c x zs p=%.3f cliff=%.2f"%(mannwhitneyu(a,b).pvalue, cliff(a,b)))
d=pd.read_csv('divergencias_coluna.csv',sep=';')
print(d.groupby(['tabela','coluna']).divergencias.agg(['count','min','max','sum']))
print(d[d.coluna=='largura_banda_khz'].to_string())
m=pd.read_csv('consolidado_36.csv',sep=';'); m['porte']=m.modelo.map(porte)
mc=m[m.cond=='c']
for col in ['cc_por_metodo','cog_por_metodo','custo_usd']:
    for g in ['modelo','porte']:
        h=kruskal(*[x[col].values for _,x in mc.groupby(g)]); print(f"KW completo {col} por {g}: H={h.statistic:.2f} p={h.pvalue:.3f}")
for col in ['cc_por_metodo','functions','custo_usd']:
    a=m[m.cond=='c'][col]; b=m[m.cond=='zs'][col]; print(f"MW {col}: med {a.median():.2f} x {b.median():.2f} p={mannwhitneyu(a,b).pvalue:.3f} cliff={cliff(a,b):.2f}")
t=m.groupby('modelo')[['ncloc','complexity','cc_por_metodo','cog_por_metodo','custo_usd']].agg(['mean','std']).round(2)
print(t.to_string()); print("total custo", m.custo_usd.sum().round(2), "ncloc min max", m.ncloc.min(), m.ncloc.max())
