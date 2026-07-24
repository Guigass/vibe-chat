# ADR-018: Retenção e exclusão de mensagens

## Status: Accepted

## Contexto

O MVP precisa de exclusão de mensagens sem comprometer auditoria, multi-tenant nem a complexidade legal de purge imediato. Decisão humana D-03 fechou a política default.

## Decisão

1. **Soft-delete** é o comportamento default de exclusão de mensagem (`DeletedAt` / `DeletedBy`); o corpo deixa de ser exposto nas APIs de leitura.
2. **Hard-delete / purge** fica fora do caminho crítico do MVP: job configurável (sugestão operacional: 90 dias) atrás de feature flag, a implementar em P2 (B-047).
3. **Export de workspace** permanece P2 (B-046).
4. Backups seguem política operacional best effort (D-08); retenção legal de backups é responsabilidade do operador.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Hard-delete imediato | Perde trilha de auditoria e complica reconciliação de `seq`/realtime |
| Retenção zero / sem delete | Bloqueia UX mínima de chat corporativo |
| Política jurídica detalhada no código | Fora do escopo do MVP; cabe ao operador + Legal |

## Consequências

- **+** API/UI simples (`message.deleted` via outbox/SignalR)
- **+** Compatível com LGPD/GDPR em fases: primeiro ocultar, depois purge
- **−** Dados soft-deleted permanecem no Postgres até purge configurado
