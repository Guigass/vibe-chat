# Operação Autônoma 24/7 — VibeChat

Runbook do pipeline que executa o roadmap continuamente. O contrato de
autoridade e risco está em [`autonomia.md`](autonomia.md); este documento define
coordenação, cadência, observabilidade e recuperação.

## Invariantes

1. `main` verde vale mais que throughput.
2. Um PR representa um único ID.
3. Nenhum item é implementado sem spec.
4. Nenhuma dependência é assumida como pronta sem status/evidência.
5. R2/R3 recebe QA independente; R3 também recebe ADR, threat model e rollback.
6. Nenhuma automação altera produção, compra recurso ou usa secret real.
7. Falhar repetidamente não gera retry infinito nem reabre decisão fechada.

## Agentes do ciclo

| Agente | Trigger | Entrada | Saída |
|--------|---------|---------|-------|
| Build | Schedule, a cada 12 h | Primeiro item elegível | PR ready com código, testes, docs e status `Done` |
| QA + Merge | Somente PR opened | PR ready + CI | Revisão focada, squash/auto-merge e lease limpo |
| Watchdog / Recovery | Diário, 09:00 BRT | Main, PRs, checks e leases | Relatório ou recovery PR |
| Security Review | Manual / rodada final | Diff + threat model | Revisão profunda quando solicitada |
| Docs / Close | Inativa | — | Responsabilidade incorporada ao Build |
| UX Review | Inativa durante roadmap | — | Rodada humana/exploratória ao final |

Build implementa e mantém roadmap/docs no mesmo PR; QA continua independente e
fornece o veredito final sem repetir as suítes já executadas pelo CI.

## Estado do item

```text
Planned
  → leased
  → branch/designated run
  → PR ready
  → checks conclusivos
  → merged + Done
```

`leased` e `PR ready` são estados operacionais, não novos status de roadmap. O
Build altera o status para `Done` na branch do PR; a mudança só se torna
autoritativa quando o próprio PR chega a `main`.

## Reserva de trabalho

Antes de editar, Build:

1. verifica PRs abertos pelo ID;
2. verifica lease compartilhado `ACTIVE:<ID>`;
3. grava `ACTIVE:<ID>:<run-id>:<UTC>` nas Memories da automação;
4. repete a busca de PR para fechar a janela de corrida;
5. renova o lease enquanto o run estiver ativo.

Lease expira em 6 horas sem heartbeat. Só pode ser recuperado depois de confirmar
que não existe PR nem run/branch ativo para o ID. QA limpa o lease depois do
merge; Build limpa ao sair sem alterações. `BLOCKED-TECH-*` preserva a evidência
e libera o slot.

## Concorrência

- no máximo três PRs Build ativos;
- no máximo um R3 ativo;
- nunca dois itens com dependência direta;
- nunca dois PRs que alterem a mesma migration, contrato ou módulo central;
- Docs e UX podem rodar em paralelo com Build;
- hotfix de `main` suspende novos merges na trilha afetada.

Se não houver mecanismo confiável de lease compartilhado, usar concorrência
Build = 1. Segurança contra trabalho duplicado prevalece sobre velocidade.

## Seleção

1. resolver safety lane comprovada antes de produto: OPS/SEC Critical|High,
   `BUG-*` Alta em `docs/product/bug-findings.md`, ou `UX-*` Alta no caminho
   principal;
2. consumir `roadmap.md` até W10;
3. consumir `horizonte-ambicioso.md` de W11 a W17;
4. dentro da primeira wave não terminal, pegar a primeira linha elegível;
5. item posterior só ultrapassa `BLOCKED-TECH` se não depender dele e não tocar
   os mesmos contratos/módulos;
6. `Conditional` B-060/B-061/B-062 não entra até o ADR registrar métrica acima
   do gatilho;
7. `External action` não bloqueia itens independentes.

## Cadência e budgets

| Controle | Default |
|----------|---------|
| Build schedule | 1 run a cada 12 h |
| QA + Merge | 1 run por PR, somente em PR opened |
| UX Review | Inativa durante roadmap |
| Watchdog | 1 run por dia, 09:00 BRT |
| Security Review | Manual / rodada humana final |
| Docs / Close | Inativa; fechamento no PR do Build |
| Revisão de dependências/security | Semanal e por PR |
| PR ativo máximo | 3 |
| PR R3 ativo máximo | 1 |
| Item por PR | 1 |
| Retry da mesma falha | 2; terceira ocorrência vira `BLOCKED-TECH` |
| Lease | 6 h com heartbeat |
| Feature opcional R3 | Off por default |

O agente pode reduzir escopo interno da implementação, mas não omitir o caminho
principal nem transformar requisitos da spec em follow-up.

## Preflight único do repositório

Antes de deixar o ciclo sem supervisão, confirmar:

- [ ] branch protection de `main` exige CI e Secret scan;
- [ ] auto-merge por squash está habilitado;
- [ ] Build abre PR ready, nunca draft, com roadmap/docs no mesmo PR;
- [ ] QA pode comentar/aprovar ou persiste o veredito em artefato durável;
- [ ] QA dispara somente em PR opened e ignora Dependabot;
- [ ] schedules não criam runs Build sobrepostos sem lease;
- [ ] QA deduplica pelo `head_sha` e não repete suítes do CI;
- [ ] token possui apenas escopos necessários;
- [ ] environments de produção continuam protegidos e fora do token;
- [ ] API/Worker usam role PostgreSQL sem ownership, `SUPERUSER` ou
      `BYPASSRLS`, e os testes de RLS passam com a credencial real de runtime;
- [ ] custo/quotas de agentes possuem alertas;
- [ ] backup e restore do repositório/configuração da automação estão descritos;
- [ ] textos dos prompts versionados foram sincronizados no dashboard.

As limitações atuais e IDs do dashboard ficam em
[`/.cursor/automations/README.md`](../../.cursor/automations/README.md).

## Watchdog

Avaliar a cada ciclo e em um resumo diário:

| Sinal | Ação automática |
|-------|-----------------|
| `main` vermelho | Pausar merges afetados; abrir hotfix/revert por PR |
| PR sem progresso > 6 h | Verificar run; renovar ou liberar lease |
| Check pendente > 60 min | Diagnosticar serviço; não mergear |
| Mesmo erro 3 vezes | `BLOCKED-TECH-*`; escolher item independente |
| Dois PRs com mesmo ID | Comentar a duplicidade, **fechar** o mais novo/extra (nunca draft) e preservar o survivor |
| Drift roadmap/spec | PR R0 de reconciliação antes de selecionar o item |
| Finding cross-tenant/secret | Bloqueio global de merge até correção |
| Custo/uso anômalo | Reduzir cadência; nunca comprar capacidade |

O drift documental é definido por
[`qualidade-documental.md`](../roadmap/qualidade-documental.md). Enquanto
`OPS-DOC-CHECKER` não estiver implementado, Docs executa o contrato de auditoria
descrito ali e anexa o resultado ao PR.

## Evidência obrigatória

Todo PR registra:

```text
Work-Item: <B-*|GAP-*|HOTFIX-*|SEC-*|OPS-*|BUG-*|UX-*|DOCS-*>
Wave: <W*-*|W11…W17|maintenance|docs|recovery>
Trilha: <A|B|C|D|E|F|G>
Deps satisfeitas: <ids ou —>
Automation: build|docs|ux-review|watchdog
Risk: R0|R1|R2|R3
Lease: <run-id>
```

E inclui comandos, resultados, migrations/rollback, screenshots quando há UI,
trace/metric quando há concorrência/realtime e threat-model/ADR quando R3.

## Ações R4

Uma ação externa vira uma entrada explícita, não uma pergunta recorrente:

```text
External action: EXT-<ID>
Necessário para: <etapa>
Motivo R4: <credencial, gasto, produção, legal, contrato ou marca>
Artefato já preparado: <arquivo/placeholder>
Impacto enquanto pendente: <capacidade degradada>
Próximo item independente: <ID>
```

O pipeline segue com fallback local, mock, sideload ou feature off quando a spec
permitir. Um resumo semanal agrupa ações externas; agentes não pedem a mesma
coisa a cada run.

## Recuperação

### Regressão após merge

1. bloquear merges da trilha;
2. reverter o squash ou abrir `HOTFIX-*`;
3. adicionar teste de regressão;
4. executar os gates da classe original;
5. documentar causa e retomar.

### Automação presa

1. capturar run/PR/commit e último heartbeat;
2. interromper o run;
3. preservar logs sem secrets;
4. liberar lease somente após confirmar ausência de writer;
5. retomar com abordagem diferente.

### Roadmap inconsistente

Não implementar por inferência. Abrir PR R0 que reconcilie código, testes,
spec, backlog e status; depois retornar à seleção.

## Métricas do pipeline

- lead time `Planned → Done`;
- tempo de PR até checks conclusivos;
- taxa de merge sem retrabalho;
- regressões/reverts por classe;
- falhas repetidas e itens `BLOCKED-TECH`;
- findings security por PR;
- leases expirados/PRs duplicados;
- percentual de `Done` com evidência;
- ações R4 abertas e idade;
- custo de automação por item entregue.

## Estado terminal

Quando W7–W17 estiverem `Done`, `Rejected` ou `External action`, Build entra em
manutenção: regressões, segurança, dependências, performance medida, UX
observada e drift documental. Não inventa uma Wave 18 sem visão, decisão e specs.
