# Matriz de Ciclo de Vida, Retenção e Compliance

Fonte transversal para criação, retenção, exclusão, legal hold, export e
reconstrução de dados. ADR-018 continua canônico para mensagens; este documento
estende a política aos derivados e aos serviços futuros.

## Princípios

1. Todo dado possui owner, finalidade, classificação e lifecycle.
2. Cópia derivada não sobrevive silenciosamente à origem.
3. Legal hold preserva; não amplia acesso.
4. Exclusão lógica e purge são estados diferentes.
5. Backup possui janela própria e não é acesso online.
6. Projeção reconstruível não é export canônico por default.
7. Evidência de purge inclui todos os stores aplicáveis.

## Precedência

```text
ordem judicial/legal hold válido
  > preservação de evidência de segurança autorizada
  > política de retenção do tenant
  > solicitação de exclusão aplicável
  > limpeza automática/lifecycle
```

Hold tem escopo, owner, fundamento, início, expiração/revisão e audit. Ao ser
removido, o dado volta à política que teria sido aplicada; itens já vencidos
entram em fila de purge com grace period e evidência, não permanecem para sempre.

## Estados comuns

| Estado | Significado |
|--------|-------------|
| Active | Disponível segundo authZ |
| SoftDeleted | Oculto no uso comum; recuperável conforme política |
| Held | Preservado por hold válido; acesso continua restrito |
| PurgePending | Elegível e aguardando job/grace period |
| Purged | Conteúdo removido dos stores online e derivados |
| Archived | Fora do hot path, ainda retido e governado |
| Rebuildable | Pode ser descartado e refeito da fonte |

## Matriz canônica

| Dado | Retenção | Delete/purge | Hold | Export |
|------|----------|--------------|------|--------|
| Mensagem atual | Tenant + ADR-018 | soft-delete → purge | Sim | Sim |
| Versões editadas | Herda conteúdo/policy própria explícita | Purge com origem | Sim | Sim, se autorizado |
| Reação/read cursor | Necessidade funcional | Delete com usuário/conversa | Normalmente não | Opcional |
| Anexo original | Herda mensagem/policy de Files | Remove objeto + metadata governada | Sim | Sim |
| Thumbnail/preview | Herda origem; curta se possível | Delete/rebuild | Sim enquanto necessário à evidência | Não canônico |
| Link preview | Herda mensagem | Delete/rebuild | Conforme conteúdo persistido | Opcional |
| Áudio/gravação | Policy explícita e consentimento | Remove bytes/derivados | Sim | Sim |
| Transcrição | Herda mídia ou policy mais restrita | Purge independente coordenado | Sim | Sim |
| Página/versão | Policy knowledge | Soft-delete/purge | Sim | Sim |
| Decision/ActionItem | Policy do recurso + provenance | Redige origem; purge conforme regra | Sim | Sim |
| Chunk/embedding | Herda todas as fontes | Delete obrigatório/reindex | Preserva só quando fonte held | Não como fonte |
| Índice de busca | Rebuildable | Remove/reindex | Não é cópia de custódia | Não |
| Inbox/digest/cache | Curta/rebuildable | Invalida/delete | Não | Não |
| Push payload | Mínima, TTL curto | Expira automaticamente | Não | Não |
| Outbox | Janela operacional | Compacta após processamento/evidência | Não substitui hold | Não |
| Audit | Política própria protegida | Restrito; nunca pelo ator comum | Sim quando aplicável | Sim, autorizado |
| Logs/traces/métricas | Curta e sem body | Expiração automática | Excepcional | Não como conteúdo |
| Export gerado | TTL curto + acesso auditado | Delete automático/manual | Derivado do escopo | É o artefato |
| Backup | Janela operacional/legal separada | Expiração segura | Política precisa considerar hold | Restauração controlada |
| Dados offline | Cache mínimo e opt-in | Logout/revoke/expiry | Não é cópia de custódia | Não |
| Dados de plugin/bridge | Declaração por destino | Revogação/delete best-effort | Conforme contrato externo | Conforme integração |

## Criação e classificação

Antes de persistir novo tipo:

- classificar conteúdo, metadata, secret ou identificador;
- declarar tenant/escopo;
- definir fonte canônica ou natureza derivada;
- definir retention default e limites;
- declarar export, hold e purge;
- definir stores, caches e destinos externos;
- registrar métricas sem PII.

Tabela nova sem essa declaração falha no gate R2/R3.

## Exclusão

Uma solicitação gera plano de propagação:

1. validar autoridade e hold;
2. marcar estado canônico;
3. emitir evento versionado;
4. invalidar cache/read models;
5. remover objetos/derivados;
6. tratar providers externos;
7. registrar resultados e falhas;
8. emitir evidência conclusiva.

Falha parcial fica visível e retryable. “Sucesso” não é retornado como purge
concluído se store obrigatório permanece pendente.

## Backups

- Backup não deve reintroduzir dado no ambiente online sem replay de purge.
- Restore aplica ledger/tombstones posteriores ao snapshot.
- Artefato expirado é removido do storage e do catálogo.
- Keys/secrets possuem backup separado e acesso mais restrito.
- Restore drill usa dados sintéticos ou sanitizados.
- Hold em backup depende de decisão legal/operacional; não se presume.

## Legal hold

O hold:

- não aparece para quem perdeu ACL;
- não impede revogação de acesso;
- suspende purge apenas no escopo;
- preserva cadeia de custódia e checksum;
- exige capability segregada;
- registra consulta/export;
- possui revisão e encerramento.

Remoção do hold calcula novamente a elegibilidade usando datas originais.

## Dados derivados e IA

- provider externo recebe apenas escopo autorizado e opt-in;
- contrato declara retenção/delete do provider;
- embeddings/chunks propagam edit/delete;
- resposta derivada guarda provenance e citações;
- desabilitar feature permite apagar projeções sem afetar mensagens;
- snapshot de authZ não substitui authZ atual.

## Federação, bridges e plugins

Antes de enviar:

- destino e trust domain;
- categorias de dados;
- finalidade;
- base/consentimento;
- retention;
- mecanismo de delete/revoke;
- limitações de soberania;
- owner de incidente.

Revogação remota é best-effort quando tecnicamente inevitável e isso deve estar
visível antes de habilitar.

## Evidência e SLO

Medir:

- purge queue age;
- delete propagation lag;
- held bytes/items;
- objetos órfãos;
- backup age/restore success;
- index stale count;
- provider delete failures;
- offline cleanup lag.

## Testes

- retention versus hold concorrente;
- remoção do hold com dado vencido;
- purge em mensagem/anexo/preview/índice;
- restore não ressuscita purge;
- revogação remove cache/offline;
- export exclui ou inclui conforme policy;
- cross-tenant em cada job;
- falha parcial e retry idempotente.
