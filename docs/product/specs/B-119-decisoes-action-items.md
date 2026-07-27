# B-119 — Decisões e action items

> Wave 12 · Trilha B/C/D · Deps: B-092, B-093 · Risco R2
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Decisões e compromissos desaparecem na timeline, sem owner, estado ou ligação
confiável à conversa que os originou.

## Escopo

- Converter mensagem em Decision ou ActionItem.
- Título, descrição, owner, due date e estado.
- Link imutável à mensagem/thread de origem.
- Histórico de mudanças e comentários via conversa vinculada.
- Lista por workspace/channel/pessoa.
- Fechar/reabrir com audit.

## Fora de escopo

- Project management completo.
- Dependências/Gantt.
- IA criando ação sem confirmação nesta fatia.

## Contratos

Entidades tenant-scoped; endpoints CRUD idempotentes; eventos
`decision.*`/`action_item.*`. Delete da origem mantém referência redigida conforme
retention, sem copiar body indefinidamente. Provenance guarda `source_id`,
`source_version`, hash de excerpt, método/ator e snapshot explicativo de authZ;
a autorização atual sempre prevalece, conforme
[`estado-eventos-auditoria-projecoes.md`](../../architecture/estado-eventos-auditoria-projecoes.md).

## UX

Ação no menu da mensagem, drawer de detalhes, filtros e estados claros. Origem
abre no ponto exato.

## Multi-tenant e authZ

Criar exige leitura da origem; atribuir exige membro elegível. Visibilidade
herda o channel, salvo coleção explicitamente mais restrita.

## Aceite

- [ ] Item aponta à origem e abre corretamente.
- [ ] Owner recebe inbox/notificação conforme preferência.
- [ ] Revogação de ACL remove acesso.
- [ ] Edit/delete/retention não vazam body copiado.
- [ ] Histórico é auditável.

## Testes

Integration CRUD/eventos, security origem/assignee, retention e E2E de conversão.

## Riscos

Criar uma segunda cópia da mensagem. Persistir apenas referência e campos
curados, com política explícita de retenção.

