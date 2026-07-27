# B-114 — Histórico de edição e movimentação

> Wave 11 · Trilha C/D/E · Deps: B-107, B-089 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Edições e mudanças de contexto não preservam uma trilha legível para usuário e
compliance.

## Escopo

- Versionar body e metadados relevantes a cada edição.
- Exibir histórico conforme política do workspace.
- Mover mensagem ou thread entre destinos autorizados.
- Manter redirect/tombstone no destino anterior quando configurado.
- Eventos e audit com actor, origem e destino.
- Export/legal hold incluem versões.

## Fora de escopo

- Reescrever `seq` histórico do destino.
- Mover conteúdo para outro tenant.
- Permitir que histórico restaure anexo purgado.

## Contratos

Tabela `message_versions`; comando de move idempotente; mensagem recebe nova
identidade/seq no destino com vínculo imutável à origem, evitando violar ordem.
ADR deve registrar a semântica final.

## UX

Indicador “editada/movida”, modal de versões e link de origem/destino. Usuário
sem acesso ao destino vê apenas aviso neutro.

## Multi-tenant e authZ

Permissões separadas para ver histórico e mover. AuthZ nos dois canais e RLS em
versões; admin break-glass auditado.

## Aceite

- [ ] Toda edição cria versão.
- [ ] Move preserva evidência sem quebrar `seq`.
- [ ] Usuário sem destino não recebe conteúdo.
- [ ] Delete/hold/export cobrem versões.
- [ ] Move cross-tenant é impossível.

## Testes

Integration de versões/move/idempotência; security origem/destino; export/hold;
E2E de histórico e redirect.

## Riscos

Vazamento pelo tombstone e crescimento de storage. Redigir previews sem ACL e
aplicar retenção às versões.

