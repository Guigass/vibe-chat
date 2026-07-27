# Documentação do VibeChat

Este é o ponto de entrada canônico para entender o produto, a arquitetura, o
estado de entrega e a operação do VibeChat.

> Snapshot documental: **2026-07-27**. O estado executável continua sendo
> determinado pelo código, pelos testes e pelo `compose.yaml`; o roadmap registra
> intenção e entrega, não substitui evidência.

## Comece por aqui

| Objetivo | Leitura recomendada |
|----------|---------------------|
| Entender o produto | [Visão](product/visao.md) → [glossário](product/glossario.md) |
| Saber o que existe hoje | [Estado atual](roadmap/estado-atual.md) → [roadmap](roadmap/roadmap.md) |
| Planejar uma feature | [Backlog](roadmap/backlog.md) → [specs](product/specs/README.md) → [decisões](roadmap/decisoes-pendentes.md) |
| Entender a arquitetura | [Visão geral](architecture/visao-geral.md) → [módulos](architecture/diagrama-modulos.md) → [contratos](architecture/contratos.md) |
| Revisar segurança | [Multi-tenancy](security/multi-tenant.md) → [modelo de ameaças](security/modelo-ameacas.md) |
| Desenvolver localmente | [Desenvolvimento](operations/desenvolvimento.md) → [configuração](operations/configuracao-env.md) |
| Operar uma instância | [Operação](operations/operacao.md) → [runbooks](operations/runbooks/README.md) |
| Trabalhar como agente | [`AGENTS.md`](../AGENTS.md) → [orientações](agents/orientacoes.md) |

## Fontes canônicas

Quando dois documentos parecerem divergir, use esta precedência e corrija a
fonte derivada no mesmo trabalho:

1. Decisão humana registrada em `roadmap/decisoes-pendentes.md`;
2. ADR aceito em `adrs/`;
3. contrato público em `architecture/contratos.md`;
4. regra de segurança em `security/`;
5. spec da feature em `product/specs/`;
6. roadmap e backlog;
7. textos de visão, guias e READMEs.

Código, migrations, testes e configuração de runtime são a evidência factual do
que está implementado. Uma linha `Done` sem essa evidência deve ser reaberta como
gap, não defendida apenas pelo texto.

## Mapa da documentação

### Produto

- [Visão do produto](product/visao.md)
- [Glossário](product/glossario.md)
- [Benchmark de mensageria](product/benchmark-mensageria.md)
- [Critérios da fatia vertical](product/criterios-aceite-fatia-vertical.md)
- [Findings e checklist de UX](product/ux-findings.md)
- [Specs de features](product/specs/README.md)

### Arquitetura

- [Visão geral](architecture/visao-geral.md)
- [Modelo de domínio](architecture/modelo-dominio.md)
- [Diagrama de módulos](architecture/diagrama-modulos.md)
- [Contratos compartilhados](architecture/contratos.md)
- [Fluxo de envio de mensagem](architecture/fluxo-envio-mensagem.md)
- [Design system](architecture/design-system.md)
- [Matriz de aderência aos ADRs](architecture/aderencia-adrs.md)
- [ADRs 001–018](adrs/README.md)

### Planejamento e governança

- [Estado atual](roadmap/estado-atual.md)
- [Roadmap executável](roadmap/roadmap.md)
- [Backlog priorizado](roadmap/backlog.md)
- [Riscos](roadmap/riscos.md)
- [Decisões humanas](roadmap/decisoes-pendentes.md)

### Operação e segurança

- [Desenvolvimento](operations/desenvolvimento.md)
- [Configuração por ambiente](operations/configuracao-env.md)
- [Operação](operations/operacao.md)
- [Troubleshooting](operations/troubleshooting.md)
- [Backup e restore](operations/backup-restore.md)
- [Runbooks](operations/runbooks/README.md)
- [Segurança multi-tenant](security/multi-tenant.md)
- [Modelo de ameaças](security/modelo-ameacas.md)

## Ciclo de vida de uma mudança

```text
necessidade → decisão (se humana/arquitetural) → backlog → spec
           → implementação + testes → evidência → Done → atualização do snapshot
```

Uma feature só está pronta para implementação quando:

- tem ID estável;
- dependências e decisões estão resolvidas;
- a spec contém escopo, fora de escopo, contratos, UX, authZ, aceite, testes e riscos;
- mudanças de contrato, arquitetura ou superfície de ameaça estão identificadas;
- não há conflito com ADRs ou decisões humanas.

## Convenções de status

| Status | Significado |
|--------|-------------|
| `Planned` | Escopo autorizado, ainda não entregue |
| `In progress` | Trabalho ativo e rastreável |
| `Blocked` | Depende de decisão ou estado externo explícito |
| `Moved` | Preservado como histórico; o novo ID/local deve ser citado |
| `Done` | Implementação, testes e documentação têm evidência |
| `Superseded` | Decisão ou entrega substituída, com sucessor identificado |

## Qualidade documental

Em cada revisão de wave:

- conferir links relativos;
- reconciliar `Done`/`Planned` entre roadmap, backlog e specs;
- atualizar o [estado atual](roadmap/estado-atual.md);
- revisar riscos e decisões;
- validar mapa de módulos e contratos contra o repositório;
- registrar gaps em vez de esconder divergências em prosa.
