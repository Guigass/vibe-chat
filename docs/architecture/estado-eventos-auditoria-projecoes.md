# Estado, Eventos, Auditoria e Projeções

Contrato para manter separados quatro conceitos que possuem retenção, finalidade
e garantias diferentes. VibeChat não adota event sourcing por acidente.

## Os quatro planos

| Plano | Pergunta | Exemplo | Pode reconstruir o domínio? |
|-------|----------|---------|-----------------------------|
| Domain state | Qual é o estado autoritativo agora? | Message, Membership, Decision | É a própria verdade |
| Outbox event | Que efeito assíncrono deve ocorrer após o commit? | `message.created` | Não |
| Audit event | Quem fez o quê, quando e sob qual autoridade? | `membership.revoked` | Não |
| Projection/read model | Como consultar/mostrar com eficiência? | inbox, busca, não lidas, embeddings | Deve ser reconstruível |

## Invariantes

- Outbox não é event store.
- Audit log não é outbox.
- Histórico de edição não é audit log.
- Projeção não vira fonte de autorização.
- Mensagem persiste como evidência canônica da conversa.
- Rebuild de projeção não modifica domain state.
- Delete/revogação propaga para derivados.

## Domain state

Mantém invariantes e versão corrente. Mutation:

1. autentica principal;
2. resolve tenant e authZ atual;
3. valida precondições/version;
4. altera estado;
5. grava outbox na mesma transação;
6. grava audit quando a ação é sensível;
7. confirma commit.

Histórico de versões pode existir como dado de domínio próprio quando o produto
precisa restaurar/comparar, sem transformar todo o sistema em event sourcing.

## Outbox

Finalidade:

- fan-out realtime;
- invalidar/reconstruir projeções;
- disparar jobs;
- entregar integrações.

Garantias:

- gravado atomicamente com o estado;
- at-least-once;
- consumer idempotente;
- ordering apenas quando declarado pelo aggregate/Conversation;
- retention operacional limitada;
- payload mínimo, versionado e sem secret.

Processar/apagar outbox nunca apaga a mensagem canônica.

## Audit

Finalidade:

- responsabilização;
- investigação;
- cadeia de custódia;
- evidência administrativa.

Audit contém:

- tenant e escopo;
- ator efetivo e ator delegador;
- ação e alvo;
- timestamp confiável;
- resultado/reason code;
- correlation/causation;
- policy/capabilities relevantes;
- metadata mínima.

Não contém secret, token ou cópia indiscriminada de body. Retenção e acesso são
próprios. Correção de estado não reescreve audit passado; evento compensatório
explica a mudança.

## Histórico de edição

Histórico responde “como este conteúdo mudou”. Audit responde “quem realizou uma
ação sensível”. Uma edição pode gerar ambos, com payloads e acesso diferentes.

Versão de conteúdo:

- pertence ao recurso;
- preserva versão, autor e horário;
- segue retention/hold do conteúdo;
- respeita authZ atual;
- não é consumida como fila.

## Projeções

Exemplos:

- busca FTS/semântica;
- inbox;
- unread summary;
- notificações pendentes;
- previews;
- dashboards;
- digests.

Toda projeção declara:

- source(s);
- checkpoint/cursor;
- versão do projector;
- idempotency/dedupe;
- lag SLO;
- rebuild;
- delete/revocation propagation;
- ACL aplicada na escrita e revalidada na leitura quando necessário;
- lifecycle.

## Provenance de conteúdo derivado

Decision, ActionItem, resumo, digest, chunk ou sugestão derivada de mensagem
registra conforme aplicável:

| Campo | Uso |
|-------|-----|
| `source_type` / `source_id` | Origem canônica |
| `source_version` | Versão observada |
| `source_excerpt_hash` | Detectar drift sem copiar conteúdo |
| `generated_at` | Momento da derivação |
| `generated_by_principal_id` | Humano/bot/automation |
| `generation_method` | manual, rule, model/provider/version |
| `authorization_snapshot_id` | Explicar decisão histórica |
| `projector_version` | Rebuild/compatibilidade |

O snapshot de autorização é evidência histórica. A autorização **atual** sempre
prevalece para leitura, edição, export e recuperação.

## Edit/delete/revoke

| Mudança na origem | Derivados |
|-------------------|-----------|
| Edit | marca stale, atualiza ou exige nova confirmação conforme produto |
| Soft-delete | redige corpo e remove de busca comum |
| Purge | remove conteúdo e projeções, respeitando hold |
| Revogação de ACL | remove acesso e reindexa/invalida |
| Legal hold | preserva no escopo sem ampliar visibilidade |

Falha de propagação gera métrica/dead-letter e bloqueia alegação de exclusão
concluída.

## Rebuild

Rebuild:

- usa source canônica;
- roda por tenant/partição;
- não publica efeitos de negócio duplicados;
- registra versão/checkpoint;
- pode ser pausado e retomado;
- mantém authZ;
- compara contagem/checksum;
- troca projection version de forma compatível.

## Eventos externos

Webhook, plugin, bridge e SIEM recebem contrato de integração, não acesso à
tabela de outbox. A entrega externa possui delivery ID, assinatura, policy de
dados, retry e retenção próprios.

Cópia fora do VibeChat deixa de ser projeção local e exige declaração de
finalidade, destino, retenção e revogação best-effort.

## Observabilidade

- outbox lag/dead-letter;
- projector lag e checkpoint age;
- rebuild progress/failure;
- delete propagation lag;
- ACL invalidation lag;
- audit write failure;
- stale derived item;
- duplicate delivery prevented.

Métrica não contém conteúdo.

## Testes

- commit falha: nem estado nem outbox persistem;
- consumer repete: projeção/efeito não duplica;
- audit indisponível em ação que o exige: falha fechada ou política documentada;
- rebuild gera resultado equivalente;
- edit/delete/revoke propagam;
- autorização atual redige derivado antigo;
- snapshot histórico não concede acesso;
- tenant A nunca entra em projeção de B.
