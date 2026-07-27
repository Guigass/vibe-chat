# Pacotes de Decisão para Capacidades R3

Defaults arquiteturais para reduzir ambiguidade antes das Waves 11–17. Não
selecionam antecipadamente bibliotecas que dependem de benchmark futuro; definem
limites, critérios e rollback que o ADR de implementação deve respeitar.

## Regra comum

Para toda capacidade R3:

1. feature flag off por default;
2. ADR aceito no mesmo PR que introduz a primeira decisão irreversível;
3. threat model atualizado;
4. tenant/authZ/RLS e caso negativo;
5. contratos e eventos versionados;
6. timeout, quota, circuit breaker e observabilidade;
7. failure mode que não derruba o chat;
8. rollback testado;
9. dependência OSS e provenance verificada;
10. `VibeChat Security Review` conclusivo.

Nenhum pacote autoriza ação R4, publicação externa, credencial real ou alegação
de compliance.

## RAG e conhecimento autorizado

Aplica-se a B-119…B-124, especialmente B-120/B-121.

### Defaults

- PostgreSQL permanece source of truth.
- Chunk/embedding é projeção descartável e reconstruível.
- Índice é por tenant/workspace e nunca concede acesso.
- Retrieval revalida ACL atual de cada fonte.
- Resposta inclui citações para conteúdo original.
- Edit/delete/purge/retention propagam para chunks e embeddings.
- Provider externo é opt-in e recebe apenas o contexto mínimo.
- Conteúdo E2EE não entra.

### ADR deve decidir

- pgvector/PostgreSQL versus serviço OSS externo, usando métrica de B-146/ADR-016;
- modelo de chunking e versionamento;
- inferência local e providers;
- reindex e delete propagation;
- orçamento e cache.

### Rollback

Desligar busca semântica, apagar projeções e reconstruir sem afetar mensagens,
páginas ou busca FTS.

## Automação e playbooks

Aplica-se a B-124…B-127.

### Modelo de execução

```text
trigger → filtros → condições → ações → resultado auditado
```

- Definição versionada e imutável por execução.
- Execução carrega `tenant_id`, owner e identidade técnica.
- Toda ação declara capability.
- Idempotency key deriva de automação + versão + evento + ação.
- Depth máximo impede recursão.
- Rate limit e budget por tenant/automação.
- Retry apenas para erro classificado como transitório.
- Dead-letter é consultável e reprocessamento é explícito.
- Dry-run não causa efeito externo.

### ADR deve decidir

- representação interna da DSL;
- scheduler e persistência de estado;
- modelo de compensação;
- concorrência por chave;
- isolamento de conectores.

### Proibido

- script arbitrário dentro da API/Worker;
- credencial embutida na definição;
- ação transitiva com privilégios maiores que owner/capability;
- retry infinito.

## Plugins, SDK e registry

Aplica-se a B-109/B-110/B-066/B-111/B-135…B-137.

### Manifesto canônico

Campos mínimos:

- `id`, `version`, `contractVersion`;
- publisher e provenance;
- capabilities solicitadas;
- endpoints/callbacks;
- configuração não secreta;
- referências de secrets;
- checksums/assinatura quando remoto;
- compatibilidade mínima/máxima;
- política de dados e egress.

### Modelo

- Plugin local é configuração + identidade + serviço externo/callback.
- Nenhum DLL/JS remoto roda dentro de API, Worker ou browser.
- Token fica apenas como hash; secret é mostrado uma vez.
- Grant de canal e capability são independentes.
- Revogação invalida token, callbacks, cache e jobs.
- Registry remoto exige assinatura, provenance, revisão e kill switch.
- Catálogo comunitário é allowlisted.

### ADR deve decidir

- formato de manifesto;
- algoritmo/embalagem de assinatura;
- discovery e distribuição;
- sandbox de ferramentas auxiliares, se existir;
- janela de compatibilidade.

### Rollback

Desativar/revogar plugin e catálogo sem migration destrutiva. Mensagens criadas
pelo bot permanecem auditáveis.

## Bridges e federação

Aplica-se a B-138/B-065.

### Ordem obrigatória

1. connector/bridge limitado;
2. framework comum;
3. trust domain allowlisted;
4. federação server-to-server;
5. nunca discovery pública por default.

### Envelope remoto

Inclui:

- origem e destino;
- identidade local/remota;
- tenant/trust domain;
- event id e versão;
- timestamp/expiry;
- assinatura;
- política de cópia e retenção;
- correlation id.

### Regras

- Identidade remota é sempre visível.
- Conteúdo copiado deixa explícito onde passa a existir.
- Revogação remota é best-effort e auditada.
- Namespace remoto não pode colidir com ID local.
- Peer não recebe diretório ou canal não concedido.
- Egress policy, SSRF e pinning de destino são obrigatórios.

### ADR deve decidir

- padrão aberto adotado ou protocolo VibeChat;
- discovery, assinatura e rotação;
- negotiation de versão;
- semântica de edit/delete;
- retry, deduplicação e partição.

### Rollback

Revogar trust domain, parar fan-out e preservar fila/audit para reconciliação.

## Clientes, dispositivo e offline

Aplica-se a B-141/B-063/B-143.

### Defaults

- PWA é referência de comportamento.
- Todos os clientes usam API/eventos comuns.
- Nenhum endpoint exclusivo concede privilégio.
- Tokens usam secure storage da plataforma.
- Cache local sensível é minimizado e criptografado quando suportado.
- Remote logout e revogação limpam credenciais.
- Fila offline usa idempotency key estável.
- Conflito de mensagem não cria seq local como verdade.

### Semântica de sync

- Servidor é autoritativo.
- Client mantém cursor por conversation.
- Reconnect faz gap-fill e reenvia operações idempotentes.
- Edit conflitante mostra estado e não sobrescreve silenciosamente.
- Delete/retention revogam cache local na próxima sincronização.
- IDs, ack, cursor, tombstone e rebuild seguem
  [`protocolo-sync-realtime.md`](protocolo-sync-realtime.md).

### ADR deve decidir

- wrapper desktop/mobile;
- storage seguro;
- formato da fila;
- estratégia de background sync;
- support matrix.

### Rollback

Desabilitar escrita offline preservando leitura online. Cliente incompatível
recebe bloqueio de upgrade claro.

## E2EE

Aplica-se a B-064 e D-26.

### Limites de produto

- Opt-in por workspace/canal e off por default.
- Sem busca server-side, RAG, DLP, moderação de conteúdo, preview, transcrição ou
  legal hold do body.
- Metadata mínima continua auditável.
- Perda de chave pode ser irrecuperável.
- Escrow organizacional não entra na primeira versão.

### Requisitos criptográficos

- protocolo e biblioteca públicos, maduros e auditados;
- nenhuma criptografia própria;
- avaliar MLS como candidato padrão para grupos antes de escolher alternativa;
- forward secrecy e rotação documentadas;
- device verification;
- tratamento de membro adicionado/removido;
- backup de chave opt-in com KDF adequada;
- testes com vetores conhecidos;
- threat model de dispositivo comprometido.

### ADR deve decidir

- protocolo;
- identidade/chaves por dispositivo;
- distribuição e rotação;
- backup/recuperação;
- compatibilidade multi-client.

### Gate adicional

Revisão criptográfica independente antes de produção. Se indisponível, feature
permanece experimental/off; isso não bloqueia outras waves.

## Live media e gravação

Aplica-se a B-147…B-149.

### Topologia

- Provider interface.
- SFU/TURN OSS em profile separado.
- Media nunca atravessa `apps/api`.
- API emite token efêmero scoped.
- Chat funciona com live indisponível.
- Gravação e transcrição são serviços separados e off por default.

### Consentimento

- aviso contínuo durante gravação;
- consentimento explícito;
- entrada tardia recebe aviso antes de transmitir;
- retenção e acesso da gravação são visíveis;
- transcrição aponta para sessão e timestamps.

### ADR deve decidir

- stack SFU/TURN;
- sizing e regiões;
- codec/browser matrix;
- armazenamento e lifecycle;
- moderação;
- degradação de rede.

### Rollback

Desabilitar criação de sessão; sessões existentes expiram; chat permanece ativo.

## HA, capacidade e storage

Aplica-se a B-145/B-146/B-144 e D-25/D-28.

### Perfis

| Perfil | Objetivo |
|--------|----------|
| Basic/Dev | Compose simples, sem objetivo de disponibilidade, RPO ≤24h/RTO ≤4h best effort |
| Standard | 99,9% como objetivo, PITR/WAL, RPO ≤1h, RTO ≤4h |
| HA | 99,95% como objetivo, RPO ≤5 min, RTO ≤30 min |

São objetivos da referência, não SLA comercial.

### Gates

- workload e dataset reproduzíveis;
- p50/p95/p99, erro e saturação;
- outbox lag e reconnect;
- restore e failover;
- migration com versões adjacentes;
- storage growth e lifecycle;
- custo operacional documentado.
- SignalR multi-instância segue [`signalr-ha.md`](signalr-ha.md).
- Dados e objetos seguem
  [`ciclo-vida-dados.md`](../security/ciclo-vida-dados.md).

### ADR deve decidir

- topologia PostgreSQL/Redis/MinIO;
- orquestração apenas após ADR-017;
- bus apenas após ADR-015;
- OpenSearch apenas após ADR-016;
- CDN/provider adicional.

### Rollback

Cada migration/topologia define retorno ao Standard ou roll-forward seguro.

## Canvas colaborativo

Aplica-se a B-120/B-152.

### Ordem

1. páginas server-authoritative;
2. versionamento otimista e histórico;
3. modelo de permissão/export/retention;
4. benchmark CRDT/OT;
5. colaboração realtime.

### Regras

- ACL é da página/coleção, não por bloco na primeira versão.
- Referência a mensagem preserva identidade, não duplica permissão.
- Snapshot e log são compactáveis.
- Delete/retention/export incluem estado colaborativo.
- Cliente offline não pode ressuscitar conteúdo revogado.

### ADR deve decidir

- CRDT/OT e biblioteca OSS;
- wire format;
- snapshot/compaction;
- presence/cursors;
- compatibilidade entre clientes.

### Rollback

Congelar coedição e manter leitura/edição server-authoritative.

## Compliance e governança

Aplica-se a B-128…B-134.

### Separação de funções

- `workspace.admin`, Auditor, Security/Compliance delegado e operador de infra
  não são equivalentes.
- Legal hold exige capability separada.
- Busca e export de compliance são auditados.
- Policy pack nunca reduz controle obrigatório silenciosamente.
- DLP não exporta conteúdo para provider externo por default.

### Precedência

```text
legal hold válido
  > purge/retention no escopo preservado
  > exclusão normal
```

Hold fora do escopo não suspende purge globalmente.

### ADR deve decidir

- modelo de case/custodian;
- cadeia de custódia;
- formatos de export;
- mecanismos de DLP;
- integração SCIM/Keycloak;
- porta de provisioning independente do adapter/IdP;
- delegação e quotas.

### Gate R4

Pare apenas para parecer legal, certificação, contrato externo ou credencial de
produção. A implementação técnica pode continuar em modo não-certificado e
claramente rotulado.

## Checklist do ADR gerado pelo agente

- contexto e B-ID;
- decisões D-* aplicáveis;
- alternativas OSS;
- licença/provenance;
- dados e contratos;
- authZ/tenancy/RLS;
- ameaça e abuso;
- operação/capacidade;
- compatibilidade;
- migration;
- rollback;
- observabilidade;
- testes;
- feature flag;
- escolha e consequência.
