# Glossário — VibeChat

Termos canônicos do domínio. Use estes nomes em código, ADRs e UI (labels de UI podem ser localizados).

## Organização e isolamento

| Termo | Definição |
|-------|-----------|
| **Tenant** | Unidade de isolamento lógico de dados e configuração. Corresponde tipicamente a uma organização/cliente. Todas as queries de negócio filtram por `tenant_id`; no PostgreSQL, reforçado por **RLS**. |
| **Workspace** | Espaço de trabalho colaborativo dentro de um tenant. Agrupa people, spaces e configurações. Um tenant pode ter um ou mais workspaces. |
| **Membership** | Vínculo usuário ↔ workspace (ou space/channel), com papéis e permissões. |
| **Role / Permissão** | Papel (ex.: owner, admin, member, guest) e claims que autorizam ações. |

## Estrutura de conversa

| Termo | Definição |
|-------|-----------|
| **Space** | Agrupamento organizacional dentro de um workspace (ex.: departamento, projeto, área). Contém canais. |
| **Channel** | Canal de mensagens (público, privado ou DM). Unidade principal de conversa. |
| **DM (Direct Message)** | Canal especial 1:1 ou grupo pequeno, sem Space obrigatório. |
| **Thread** | Subconversa ancorada em uma mensagem pai dentro de um channel. |
| **Topic** | Assunto/etiqueta opcional associado a um channel ou thread (organização semântica; não substitui channel). |
| **Conversation** | Abstração técnica que unifica channel ou thread para fins de sequência, outbox e entrega. Possui `conversation_id`. |

## Mensagens e entrega

| Termo | Definição |
|-------|-----------|
| **Message** | Unidade de conteúdo enviada a uma conversation (texto, anexos, metadados). Imutável no conteúdo original; edições geram versões/eventos. |
| **Sequence Number** | Número monotônico por conversation (`seq`). Garante ordenação e detecção de lacunas no cliente. |
| **Idempotency Key** | Chave fornecida pelo cliente (ou gerada) para evitar duplicar envios em retries. |
| **Outbox** | Tabela/padrão transacional: evento de domínio gravado na mesma transação da mensagem; um worker publica/entrega depois. |
| **Inbox / Delivery** | Estado de entrega por destinatário/conexão (delivered, read — conforme fase). |
| **Read cursor** | Posição de leitura persistente por (`tenant`, `user`, `channel`) em `messaging.read_cursors` (`lastReadSeq`). Fonte da verdade das **não lidas** (badge, divisor, push); sobrevive a F5 e multi-dispositivo (B-094). Não confundir com estado só em memória no client. |
| **Unread / Não lidas** | Contagem derivada de `maxSeq - lastReadSeq` (e menções). Badge na sidebar hidrata do servidor; só zera de forma definitiva ao avançar o cursor. |
| **Reaction** | Reação emoji (ou similar) a uma mensagem. |
| **Attachment** | Arquivo associado a mensagem; bytes no object storage (MinIO); metadados no PostgreSQL. |

## Tempo real e presença

| Termo | Definição |
|-------|-----------|
| **Presence** | Estado online/away/offline do usuário, tipicamente no Redis com TTL. |
| **Typing** | Indicador de “digitando…” com TTL curto no Redis. |
| **Hub / Connection** | Conexão SignalR autenticada; grupos por tenant/workspace/channel. |
| **Backplane** | Redis como backplane SignalR para fan-out entre instâncias da API. |

## Identidade e acesso

| Termo | Definição |
|-------|-----------|
| **User** | Identidade de pessoa; source of truth de autenticação no Keycloak (OIDC). Perfil espelhado/enriquecido no VibeChat. |
| **Cadastro / Provisionamento** | Fluxo em duas camadas: (1) **autenticação** no IdP (Keycloak/OIDC cria a identidade); (2) **autorização** no VibeChat via Membership + Diretivas. Login sozinho **não** concede acesso a workspace — falta membership. Admin convida por e-mail (`POST .../members`) e atribui papel; perfil stub `pending:{email}` é vinculado no primeiro SSO. Seed/demo permanece para DX. Sem self-signup aberto na fase 1 (B-068). |
| **Diretiva** | Regra de autorização derivada do papel (`Role` + `RolePermissionCatalog` / policies de workspace). Ex.: quem pode `channel.create`, `workspace.admin`, `admin.dashboard`, `ai.summarize`, `ai.suggest_reply`. Gerenciada na UI admin junto ao cadastro. Não confundir com “prompt” de IA. |
| **OIDC / SSO** | OpenID Connect; fluxo padrão via Keycloak. |
| **Service Account** | Identidade de máquina para workers/integrações. |
| **Guest** | Membership de escopo reduzido a **um canal**, criada por convite de admin com link de uso único e validade (default 7 dias). Guest envia mensagem, anexo e reação no canal do convite; não lista outros canais, não usa busca global, não vê o diretório do workspace e não lê configuração. Revogação pelo admin encerra sessão e membership. Convite e aceite geram audit. Fora do MVP P1; entra na Wave 10 (D-07 revisado em 2026-07-25 / B-040). |

## Administração e compliance

| Termo | Definição |
|-------|-----------|
| **Audit log (ações)** | Trilha de eventos sensíveis (`audit.audit_events`: login admin, role change, message.send/delete, etc.). Feed em `/admin` — B-042. |
| **Auditoria de conversa** | Visão ADMIN do histórico de um canal/DM/thread (conteúdo, edições, soft-delete, autores, anexos) para compliance — B-067; distinta do audit log de ações. |
| **Configuração sensível** | Tokens, webhooks, chaves de AI/SMTP e secrets de integração. Só administradores leem/alteram via `/admin` + `GET/PUT /api/v1/admin/settings` (`workspace.admin`); membros/Auditor → 403; resposta só com máscara/`configured` (B-069). AI/SMTP secrets ficam em env/secret store; secret HMAC de webhook é gravável no admin e nunca retornado em claro (B-048). |
| **Webhook outbound** | Endpoint HTTP do tenant que recebe fan-out de eventos assinado com HMAC-SHA256 (`X-VibeChat-Signature`). Fase 1 (B-048): 1 endpoint, só `MessageCreated`. Extensão (B-108): multi-endpoint, catálogo de eventos, filtro de canal, ping de teste. Só `workspace.admin`. |
| **Plugin / Integração** | Pacote instalável **só na instância** (manifesto + capabilities + identidade Bot). Núcleo de envio: **B-109**; install/gestão: **B-110**; capabilities avançadas: **B-066** (P3). Sem loja pública (D-11). |
| **Integração / Bot API** | Identidade `Role.Bot` + token para sistemas externos **enviarem** mensagens (capability `messages.send`, B-109). Base de todo plugin na 1ª onda. |
| **Export de workspace** | Pacote ZIP compliance (`vibechat.workspace.export.v1`) com JSON do workspace (membros, spaces, channels, threads, messages com soft-delete, metadados de anexos — sem binários MinIO). `GET /api/v1/admin/workspaces/{id}/export`; exige `workspace.admin`; audit `workspace.export` (B-046 / D-03). |
| **Retenção / purge** | Política por tenant (`messaging.message_retention_settings`: `enabled`, `retentionDays`) + kill switch de processo `MessageRetention:Enabled` (off default). Com ambos ligados, o worker hard-deleta soft-deletes além do cutoff; remove reactions; detach anexos (sem delete MinIO); audit `message.purge` (B-047 / ADR-018 / D-03). |
| **Política de edição/apagar** | Regras por workspace sobre quem pode editar/apagar mensagem e por quanto tempo após o envio (`messaging.edit.*` / `messaging.delete.*` em admin settings). Papéis do catálogo + janela em minutos + override de moderação (B-107). Distinta de retenção/purge. |

## Plataforma e infra

| Termo | Definição |
|-------|-----------|
| **Modular Monolith** | Um deploy (API + worker) com módulos de domínio isolados por fronteiras e contratos. |
| **Module** | Pacote/assembly de domínio (Identity, Directory, Messaging, Files, Search, AI, Notifications, Audit…). |
| **Contract** | Interface/DTO/evento compartilhado entre módulos; sem referência direta a implementações internas. |
| **Worker** | Processo que consome outbox, processa jobs (indexação leve, thumbnails, AI, etc.). |
| **Object Storage** | MinIO (S3-compatible) para blobs. |
| **RLS** | Row Level Security no PostgreSQL, política por `tenant_id` (e claims de sessão). |

## IA (opcional)

| Termo | Definição |
|-------|-----------|
| **AI Provider** | Implementação atrás de `IAiCompletionProvider` (Null / Mock / OpenRouter). |
| **AI Feature** | Capacidade opcional (resumo, sugestão, busca semântica futura) desligada por padrão. |
| **Prompt Context** | Contexto permitido (mensagens/canais com ACL) enviado ao provedor — nunca cruzar tenants. |

## Observabilidade

| Termo | Definição |
|-------|-----------|
| **Trace** | Span distribuído (Tempo via OTel). |
| **Metric** | Contadores/histogramas (Prometheus). |
| **Log** | Log estruturado (Loki). |
| **Correlation Id** | ID que amarra request HTTP ↔ hub ↔ outbox ↔ worker. |

## Siglas úteis

| Sigla | Significado |
|-------|-------------|
| **ADR** | Architecture Decision Record |
| **PWA** | Progressive Web App |
| **CDK** | Angular Component Dev Kit |
| **SSO** | Single Sign-On |
| **S3** | API de object storage compatível (Amazon S3) |
| **LTS** | Long-Term Support |
