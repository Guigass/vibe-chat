# Contrato de Execução Autônoma — VibeChat

Este documento autoriza agentes a evoluir o repositório continuamente, em PRs
pequenos, seguindo roadmap, specs, testes e evidências. A autonomia é sobre
decisões técnicas e execução no repositório; não concede credenciais, orçamento
ou autoridade sobre produção de terceiros.

## Objetivo

Manter o ciclo abaixo funcionando sem depender de escolha humana rotineira:

```text
selecionar item → reservar ID → implementar + atualizar docs/status → testar
→ abrir PR → CI + QA independente → auto-merge → próximo item
```

## Autoridade delegada

O agente pode, sem pedir confirmação:

- escolher implementação entre alternativas OSS compatíveis;
- criar/emendar ADR técnico dentro das decisões de produto vigentes;
- adicionar migration, endpoint, evento e UI exigidos pela spec;
- corrigir testes, CI, DX e documentação necessários ao item;
- criar feature flag e defaults seguros;
- replanejar detalhes internos sem ampliar resultado da spec;
- rejeitar uma biblioteca por licença, segurança ou manutenção e escolher outra;
- fazer rollback de mudança recém-introduzida que falhe nos gates;
- abrir, revisar, corrigir, aprovar e fazer auto-merge de PRs dentro desta política.

O agente não precisa reabrir D-* porque uma tarefa ficou difícil. Deve decidir
tecnicamente, registrar trade-offs e continuar.

## Poucas condições humanas

Somente estes casos ficam fora da autonomia:

1. aceitar licença nova incompatível com Apache-2.0;
2. comprar serviço, domínio, certificado, licença ou assumir gasto real;
3. usar credencial/secret real ou alterar produção externa;
4. declarar certificação legal/compliance sem auditoria;
5. apagar/restaurar dados reais fora do ambiente efêmero de teste;
6. celebrar contrato, definir preço ou assumir SLA comercial;
7. mudar nome/marca legal fora do white-label já autorizado.

Quando um item depender disso, marcar somente a etapa externa como
`External action`, documentar o artefato/placeholder e seguir para outro item
independente. Não bloquear o projeto inteiro.

## Hierarquia para decisões técnicas

Na dúvida, escolher nesta ordem:

1. segurança e isolamento;
2. integridade/recuperação dos dados;
3. contrato e compatibilidade;
4. simplicidade operacional;
5. OSS e portabilidade;
6. observabilidade/testabilidade;
7. desempenho medido;
8. conveniência de implementação.

Preferir a alternativa:

- reversível;
- com menor superfície operacional;
- já alinhada aos padrões do repositório;
- sem serviço novo;
- com licença permissiva e manutenção ativa;
- que mantém o sistema útil quando o componente opcional falha.

Empate técnico: registrar a opção mais simples no ADR e implementar. Não parar.

## Classes de risco

| Classe | Exemplos | Merge autônomo |
|--------|----------|-----------------|
| R0 — Docs | Texto, links, status, runbook | Sim, com validação documental |
| R1 — Local | UI, domínio sem persistência nova, refactor limitado | Sim, CI + testes da trilha |
| R2 — Dados/contratos | Migration, API/evento, worker, plugin | Sim, integração + arch + security |
| R3 — Sensível/arquitetural | AuthZ, RLS, IA, E2EE, federation, live, novo serviço | Sim, ADR + threat model + flag off + rollback + revisão security |
| R4 — Externo | Credencial real, compra, produção, contrato/legal | Não executar; `External action` |

R3 não significa revisão humana. Significa evidência maior e QA independente.

## Perfil temporário de construção econômica

Durante a construção pré-release do roadmap, o owner autorizou um perfil de
baixo custo: Build e QA usam o CI como gate determinístico; Security Review,
Docs Close, UX Review e Bugbot não rodam automaticamente. Build inclui docs e
status no mesmo PR, e QA limpa o lease após o merge.

Nesse perfil, revisão profunda de segurança/UX e testes exploratórios R2/R3 são
deferidos para a rodada humana ao término do roadmap. `Done` significa
implementado, documentado e verde no CI — não significa aprovação para produção.
Nenhum release/produção pode ocorrer antes dessa rodada final. Testes de
segurança existentes no CI, isolamento tenant, authZ/RLS, secret scan e os gates
da classe de risco continuam obrigatórios.

## Seleção e reserva de trabalho

1. Ler `roadmap.md` e depois `horizonte-ambicioso.md`.
2. Escolher o primeiro `Planned` cujas dependências estejam `Done`.
3. Confirmar que existe `B-NNN-*.md` e que a spec declara R0–R3.
4. Não iniciar se já houver PR aberto com o mesmo ID.
5. Um PR possui exatamente um Wave/Backlog ID.
6. Trabalho paralelo é permitido apenas quando módulos/arquivos não se sobrepõem
   materialmente e não existe dependência entre itens.
7. Limite recomendado: até três PRs ativos, um por trilha independente.

## ADR autônomo

ADR pode ser criado e marcado `Accepted` pelo agente quando:

- a decisão de produto D-* já está fechada;
- alternativas, licença, segurança e operação foram comparadas;
- existe rollback ou migration path;
- a escolha não exige R4;
- o PR inclui testes/evidência compatíveis.

Para R3, começar pelos defaults de
`docs/architecture/pacotes-decisao-r3.md`. Divergência exige justificativa no
ADR, não uma nova decisão humana.

ADRs 015–017 continuam baseados em gatilhos medidos. “O projeto será grande” não
é evidência para bus, OpenSearch ou Kubernetes.

## Gates por classe

### R0

- links relativos;
- `git diff --check`;
- IDs/status consistentes;
- nenhuma alegação `Done` sem evidência.

### R1

- build/lint;
- unitários;
- testes de UI/a11y relevantes;
- screenshots quando layout mudar;
- docs/contratos atualizados se aplicável.

### R2

- gates R1;
- integration/Testcontainers;
- migration e rollback/compatibilidade;
- architecture tests;
- security cross-tenant negativo;
- idempotência/outbox quando houver mutação.

### R3

- gates R2;
- ADR;
- threat model;
- feature opcional atrás de flag off por default; controle fundamental de
  segurança (ex.: RLS runtime) não recebe bypass/kill switch;
- failure mode e rollback testados;
- secrets/PII revistos;
- load/capacity test proporcional;
- `VibeChat Security Review` conclusivo no perfil normal; no perfil econômico,
  registrar a revisão profunda como gate deferido da rodada humana final.

## Política de merge

Auto-merge por squash é permitido quando:

- PR está ready;
- CI obrigatória está conclusiva e verde;
- QA independente deu `PASS` ou `PASS WITH NITS`;
- `VibeChat Security Review` passou para R2/R3, exceto no perfil econômico
  pré-release documentado acima;
- branch está atualizada e sem conflito;
- escopo corresponde a um ID;
- evidência está no PR;
- não há R4.

Nits não bloqueiam. Viram `GAP-*` apenas se objetivos, verificáveis e pequenos.

## Falhas e prevenção de loops

- Primeira falha: diagnosticar e corrigir no mesmo PR.
- Segunda falha equivalente: reduzir a abordagem, revalidar a spec e tentar uma
  alternativa técnica.
- Terceira falha equivalente: registrar `BLOCKED-TECH-<ID>` com evidência,
  preservar a branch/PR sem merge e selecionar outro item independente.
- Não repetir comando/abordagem idênticos esperando resultado diferente.
- Blocker técnico não vira decisão humana automaticamente.
- Ao fechar a causa em outro PR, reativar o item bloqueado.

## Regressão pós-merge

1. interromper novos merges na trilha afetada;
2. abrir `HOTFIX-<id>` ou reverter o squash, escolhendo o caminho mais seguro;
3. rodar gates da classe original;
4. registrar causa e teste de regressão;
5. retomar a fila quando main estiver verde.

Outras trilhas independentes podem continuar se não compartilham a falha.

## Definition of Done de item

- comportamento e estados da spec entregues;
- contratos/migrations/eventos versionados;
- authZ, tenancy e RLS cobertos;
- observabilidade e runbook proporcionais;
- testes da classe verdes;
- docs e glossário sincronizados;
- PR mergeado;
- roadmap/backlog marcados `Done` com evidência;
- nenhum follow-up necessário para o caminho principal funcionar.

## Definition of Done do projeto documentado

O programa chega ao estado de manutenção quando:

- Waves 7–17 estão `Done`, `Rejected` ou `External action`;
- todos os critérios de saída de wave passam;
- não existem findings críticos/altos abertos;
- Standard e HA têm evidência de operação;
- upgrade/restore/security drills estão documentados e reproduzíveis;
- contratos públicos têm política de compatibilidade;
- release/versionamento seguem `docs/operations/release-versionamento-suporte.md`;
- o pipeline muda para gaps, segurança, performance e manutenção.
