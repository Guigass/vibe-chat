# Discovery de Produto

Discovery reduz incerteza de uma ideia nova ou de um detalhe dentro de uma spec.
Não produz código e não é gate humano para W11–W17, que já estão `Planned`.

## Quando usar

Abrir um brief de discovery quando:

- surge uma capacidade além de W17 ou uma hipótese importante dentro de uma spec;
- há uma persona e problema plausíveis;
- existe evidência que o agente possa coletar ou simular;
- a próxima decisão depende de evidência, não apenas preferência;
- o experimento não exige uma ação R4 externa.

Nome sugerido: `B-NNN-titulo-discovery.md`.

## Template

```markdown
# Discovery B-NNN — Título

> Owner: … · Data: … · Status: Exploring
> Gates: D-… · Dependências: …

## Hipótese

Se [persona] puder [capacidade], então [resultado], porque [insight].

## Problema e job-to-be-done

Quem sofre, em qual contexto, com qual frequência e impacto?

## Evidências

- Entrevistas:
- Dados/telemetria:
- Tickets/feedback:
- Benchmark:
- Solução atual/workaround:

## Alternativas

1. Não fazer;
2. Melhorar fluxo existente;
3. Integrar ferramenta externa;
4. Construir capacidade mínima;
5. Construir plataforma completa.

## Menor experimento útil

Protótipo, concierge, teste de navegação, contrato simulado ou análise de dados.
Definir prazo e o que será descartado.

## Métrica e sinal de sucesso

Baseline, target e janela. Evitar “usuários gostaram” sem comportamento medido.

## Dados, tenancy e authZ

Quais dados cria/copia/indexa? Quem pode ver, exportar e apagar? Há impacto em
RLS, retenção, IA ou terceiros?

## Custo permanente

Operação, suporte, storage, observabilidade, compatibilidade e incidentes.

## Riscos e suposições

Ordenar do mais incerto/irreversível para o mais simples.

## Resultado

- [ ] Promote para spec
- [ ] Iterate
- [ ] Park
- [ ] Reject

Justificativa e evidência:
```

## Critérios de promoção para spec

- problema confirmado por mais de uma evidência;
- resultado e métrica definidos;
- menor fatia útil identificada;
- alternativa “não construir” considerada;
- decisões D-* vigentes respeitadas;
- impacto em contratos, segurança, tenancy, dados e operação entendido;
- dependências e fora de escopo claros;
- custo permanente compatível com os defaults do produto.

## Critérios de rejeição

Rejeitar ou estacionar quando:

- duplica feature existente;
- só alcança paridade cosmética;
- exige arquitetura grande sem demanda;
- enfraquece soberania/self-hosted;
- depende de serviço proprietário obrigatório;
- não há forma segura de preservar ACL/retention;
- custo permanente supera o valor esperado;
- conflita com decisão de produto vigente.

## Status

| Status | Uso |
|--------|-----|
| `Exploring` | Pesquisa ativa |
| `Validated` | Problema/resultado confirmados; pode preparar spec |
| `Parked` | Evidência insuficiente ou timing inadequado |
| `Rejected` | Não seguirá, com motivo preservado |
| `Promoted` | Spec criada e item movido para `Planned` |
