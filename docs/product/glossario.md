# Glossário — VibeChat

Termos canônicos do domínio. Use estes nomes em código, ADRs e UI (labels de UI podem ser localizados).

## Organização e isolamento

| Termo | Definição |
|-------|-----------|
| **Tenant** | Unidade de isolamento lógico de dados e configuração. Corresponde tipicamente a uma organização/cliente. Todas as queries de negócio filtram por `tenant_id`; no PostgreSQL, reforçado por **RLS**. |
| **Workspace** | Espaço de trabalho colaborativo dentro de um tenant. Agrupa people, spaces e configurações. Um tenant pode ter um ou mais workspaces. |
| **Membership** | Vínculo usuário ↔ workspace (ou space/channel), com papéis e permissões. |
| **Role / Permissão** | Papel de **workspace** em `workspace_members.role` (ex.: Owner, Admin, Member, Guest, Auditor) + capabilities do `RolePermissionCatalog` (`workspace.admin`, `admin.dashboard`, etc.). Fonte de verdade da authZ de produto (B-176). Claims JWT / realm roles do Keycloak **não** concedem papel de workspace. Distinto de **roles de banco** (`vibechat_app` / migrator — RLS) e de **realm roles** do IdP (identidade/SSO opcional). |
| **Contact group / Grupo de contatos** | Agrupamento da **lista de pessoas** do workspace para navegação (B-166). Kind `department` (departamento compartilhado, ex.: Vendas, Estoque, TI; admin) ou `personal` (só o dono). **Não** é Space, **não** concede acesso a canal/DM e **não** substitui membership de autorização nem grupos SCIM (B-128). |

## Estrutura de conversa

| Termo | Definição |
|-------|-----------|
| **Space** | Agrupamento organizacional de **canais** dentro de um workspace (ex.: área, projeto). Não organiza a lista de contatos — isso é Contact group (B-166). |
| **Channel** | Canal de mensagens (público, privado ou DM). Unidade principal de conversa. Em privado, quem entra é `channel_members` (roster/gestão em B-186); guest externo é B-040. |
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
| **Inbox / Delivery** | Estado técnico de entrega por destinatário/conexão (delivered, read — conforme fase). |
| **Inbox unificada** | Visão de produto que reúne DMs, menções, threads seguidas, anúncios e itens prioritários sem criar uma segunda fonte de read state (candidata B-117). |
| **Read cursor** | Posição de leitura persistente por (`tenant`, `user`, `channel`) em `messaging.read_cursors` (`lastReadSeq`). Fonte da verdade das **não lidas** (badge, divisor, push); sobrevive a F5 e multi-dispositivo (B-094). Não confundir com estado só em memória no client. |
| **Unread / Não lidas** | Contagem derivada de `maxSeq - lastReadSeq` (e menções). Badge na sidebar hidrata do servidor; só zera de forma definitiva ao avançar o cursor. |
| **Reaction** | Reação emoji (ou similar) a uma mensagem. |
| **Attachment** | Arquivo associado a mensagem; bytes no object storage (MinIO); metadados no PostgreSQL. |
| **Announcement / Anúncio** | Mensagem ou conversa de broadcast com regras de publicação e, opcionalmente, confirmação de leitura; não é sinônimo de notificação push. |
| **Action item** | Ação estruturada derivada de uma conversa e ligada à mensagem de origem; pode evoluir para tarefa, mas preserva evidência. |
| **Digest / Catch-up** | Resumo periódico de atividade autorizada, com links para conversas de origem. |

## Tempo real e presença

| Termo | Definição |
|-------|-----------|
| **Presence** | Estado online/away/offline do usuário, tipicamente no Redis com TTL. |
| **Typing** | Indicador de “digitando…” com TTL curto no Redis. |
| **Hub / Connection** | Conexão SignalR autenticada; grupos por tenant/workspace/channel. |
| **Web Push** | Notificação do navegador via protocolo Web Push + VAPID da instância (D-13 / B-095). Opt-in por dispositivo; payload mínimo; off por default (`Push:Enabled`). Distinto de e-mail (B-043) e de preferências/DND (B-097). |
| **Backplane** | Redis como backplane SignalR para fan-out entre instâncias da API. |

## Identidade e acesso

| Termo | Definição |
|-------|-----------|
| **User** | Perfil humano local estável usado como ator. Autenticação vem de External Identity no IdP; memberships e papéis continuam no VibeChat. |
| **Perfil público** | Ficha do membro no workspace (displayName, cargo/função, sobre, mensagem de destaque, avatar), editável pelo dono e legível por membros autorizados do mesmo workspace — não é exposição na internet aberta (B-167). Distinto de status temporário (B-116) e de preferências pessoais (locale/DND/read receipts/wallpaper/accent). |
| **Personalização visual** | Preferências pessoais de aparência da conversa: wallpaper curado da timeline e accent de destaque (B-185). Só do próprio usuário; distinto de light/dark global (B-049), densidade e de branding por tenant (B-140). |
| **Person** | Pessoa natural, quando conhecida. Não é credencial, sessão, membership nem identificador de autorização. |
| **External Identity** | Vínculo autenticado `(issuer, subject)` vindo de IdP. E-mail é atributo mutável, não chave canônica. |
| **Principal** | Ator autorizável: User, Guest, Bot, Service Account ou Automation. Papel pertence ao vínculo/escopo, não globalmente ao principal. |
| **Device** | Instalação registrada de cliente, usada por push, offline, remote logout e E2EE. Não concede acesso sem sessão/membership atual. |
| **Session** | Sessão autenticada e revogável associada a principal e, quando disponível, device. Token não substitui revalidação de membership. |
| **Cadastro / Provisionamento** | Fluxo em duas camadas: (1) **autenticação** no IdP (Keycloak/OIDC cria a identidade); (2) **autorização** no VibeChat via Membership + Diretivas. Login sozinho **não** concede acesso a workspace — falta membership. Admin convida por e-mail (`POST .../members`) e atribui papel; perfil stub `pending:{email}` é vinculado no primeiro SSO. Seed/demo permanece para DX. Sem self-signup aberto na fase 1 (B-068). |
| **Diretiva** | Regra de autorização derivada do papel (`Role` + `RolePermissionCatalog` / policies de workspace). Ex.: quem pode `channel.create`, `workspace.admin`, `admin.dashboard`, `ai.summarize`, `ai.suggest_reply`. Gerenciada na UI admin junto ao cadastro. Não confundir com “prompt” de IA. |
| **OIDC / SSO** | OpenID Connect; fluxo padrão via Keycloak. |
| **Service Account** | Identidade de máquina para workers/integrações. |
| **Guest** | Membership de escopo reduzido a **um canal**, criada por convite de admin com link de uso único e validade (default 7 dias). Guest envia mensagem, anexo e reação no canal do convite; não lista outros canais, não usa busca global, não vê o diretório do workspace e não lê configuração. Revogação pelo admin encerra sessão e membership. Convite e aceite geram audit. Fora do MVP P1; entra na Wave 10 (D-07 revisado em 2026-07-25 / B-040). |

## Administração e compliance

| Termo | Definição |
|-------|-----------|
| **Audit log (ações)** | Trilha de eventos sensíveis (`audit.audit_events`: login admin, role change, message.send/delete, etc.). Feed em `/admin` — B-042. Com `contentAuditEnabled=true` (B-169), `message.delete` inclui snapshot do body no metadata (sobrevive a purge); com `false`, só ids/timestamps. |
| **Auditoria de conversa** | Visão ADMIN do histórico de um canal/DM/thread (conteúdo, edições, soft-delete, autores, anexos) para compliance — B-067; distinta do audit log de ações. Condicionada a `contentAuditEnabled` (B-169): desligada, auditores não leem body e E2EE (B-064) fica permitido; ligada, soft-delete permanece legível na conversa e no snapshot de `message.delete`. |
| **Configuração sensível** | Tokens, webhooks, chaves de AI/SMTP e secrets de integração. Só administradores leem/alteram via `/admin` + `GET/PUT /api/v1/admin/settings` (`workspace.admin`); membros/Auditor → 403; resposta só com máscara/`configured` (B-069). AI/SMTP secrets ficam em env/secret store; secret HMAC de webhook é gravável no admin e nunca retornado em claro (B-048). |
| **Webhook outbound** | Endpoint HTTP do tenant que recebe fan-out de eventos assinado com HMAC-SHA256 (`X-VibeChat-Signature`). Fase 1 (B-048): 1 endpoint, só `MessageCreated`. Extensão (B-108): multi-endpoint, catálogo de eventos, filtro de canal, ping de teste. Só `workspace.admin`. |
| **Plugin / Integração** | Pacote instalável por manifesto + capabilities + identidade Bot. Núcleo de envio: **B-109**; install/gestão local: **B-110**; capabilities avançadas: **B-066/B-111** (W15); registry assinado: **B-137**. Sem billing ou código não confiável in-process (D-18). |
| **Integração / Bot API** | Identidade `Role.Bot` + token para sistemas externos **enviarem** mensagens (capability `messages.send`, B-109). Base de todo plugin na 1ª onda. |
| **Export de workspace** | Pacote ZIP compliance (`vibechat.workspace.export.v1`) com JSON do workspace (membros, spaces, channels, threads, messages com soft-delete, metadados de anexos — sem binários MinIO). `GET /api/v1/admin/workspaces/{id}/export`; exige `workspace.admin`; audit `workspace.export` (B-046 / D-03). |
| **Backup de chat** | Capacidade de produto (B-172) para export/import de pacote `vibechat.chat.backup.v1`, tarefas agendadas e envio a destinos remotos (SFTP/FTP, SMB, S3-compatível, Google Drive, WebDAV). Distinto do **backup operacional** de infra (B-031 / `backup-restore.md`) e do export compliance pontual (B-046). |
| **Retenção / purge** | Política por tenant (`messaging.message_retention_settings`: `enabled`, `retentionDays`) + kill switch de processo `MessageRetention:Enabled` (off default). Com ambos ligados, o worker hard-deleta soft-deletes além do cutoff; remove reactions; detach anexos (sem delete MinIO); audit `message.purge` (B-047 / ADR-018 / D-03). |
| **Política de edição/apagar** | Regras por workspace sobre quem pode editar/apagar mensagem e por quanto tempo após o envio (`messaging.edit.*` / `messaging.delete.*` em admin settings). Papéis do catálogo + janela em minutos + override de moderação (B-107). Distinta de retenção/purge. A **superfície de UI** de editar é o composer (B-173), não a bolha. Preservar o conteúdo apagado no audit (`message.delete` com body) é obrigação de B-169 quando `contentAuditEnabled=true`, não desta política. |
| **Legal hold** | Suspensão controlada de purge para conteúdo sob obrigação legal; sua precedência sobre retenção precisa de decisão D-23. |
| **eDiscovery** | Busca, preservação e export de conteúdo para investigação/compliance, com authZ e audit reforçados. |
| **DLP** | Data Loss Prevention: classificação e políticas que detectam/restringem exposição de dados sensíveis. |
| **Policy pack** | Conjunto versionado de defaults e controles administrativos para um perfil de organização; nunca reduz controles obrigatórios silenciosamente. |

## Plataforma e infra

| Termo | Definição |
|-------|-----------|
| **Modular Monolith** | Um deploy (API + worker) com módulos de domínio isolados por fronteiras e contratos. |
| **Module** | Pacote/assembly de domínio (Identity, Directory, Messaging, Files, Search, AI, Notifications, Audit…). |
| **Contract** | Interface/DTO/evento compartilhado entre módulos; sem referência direta a implementações internas. |
| **Worker** | Processo que consome outbox, processa jobs (indexação leve, thumbnails, AI, etc.). |
| **Object Storage** | MinIO (S3-compatible) para blobs. |
| **RLS** | Row Level Security no PostgreSQL, política por `tenant_id` (e claims de sessão). |
| **Workflow / Automação** | Execução auditável de trigger → condições → ações, com idempotência, limites e identidade própria. |
| **Playbook** | Sequência operacional reutilizável de passos, owners e evidências, por exemplo para incidente ou onboarding. |
| **Bridge** | Integração que replica eventos/identidades entre VibeChat e outra rede; implica cópia de dados fora do domínio local. |
| **Federação** | Comunicação server-to-server entre instâncias independentes, com identidade e dados distribuídos. Não confundir com multi-tenancy local. |
| **Import batch** | Execução versionada e idempotente que transforma export externo em recursos canônicos após dry-run/staging; não é bridge contínua. |
| **Support bundle** | Pacote diagnóstico allowlisted, sanitizado, temporário e auditado; nunca dump amplo de logs/dados. |

## IA (opcional)

| Termo | Definição |
|-------|-----------|
| **AI Provider** | Implementação atrás de `IAiCompletionProvider` (Null / Mock / OpenRouter). |
| **AI Feature** | Capacidade opcional (resumo, sugestão, busca semântica futura) desligada por padrão. |
| **Prompt Context** | Contexto permitido (mensagens/canais com ACL) enviado ao provedor — nunca cruzar tenants. |
| **RAG** | Retrieval-Augmented Generation: recuperação de fontes autorizadas para fundamentar uma resposta de IA; embeddings e citações também obedecem ACL e retenção. |
| **Bot de IA** | `Principal.Bot` visível na conversa, ligado a uma definição/version publicada que combina personalidade, model binding, skills, knowledge grants, MCP grants e guardrails. Nome/persona não concede acesso. |
| **BotVersion** | Snapshot imutável de uma publicação do bot. Fixa versões/hashes de prompt, modelo, skills, grants e policy para execução, audit e rollback reproduzíveis. |
| **System prompt** | Instrução de maior precedência configurável na versão do bot, abaixo apenas das invariantes/policies obrigatórias da plataforma. Não contém secret nem concede capability. |
| **Model binding** | Referência governada a provider/modelo, parâmetros allowlisted, classe de dados, timeout, fallback e budget. Chave do provider fica no secret store/env. |
| **Skill de bot** | Pacote declarativo e versionado de instruções, schema e exemplos reutilizáveis. Não executa código, não contém credencial e só referencia grants já concedidos. |
| **Knowledge source** | Arquivo, página ou snapshot de URL com owner, classificação, ACL, versão, hash, retention e estado de ingestão. |
| **Knowledge collection / grant** | Agrupamento de fontes e autorização explícita que permite a uma versão de bot usar esse conteúdo; continua subordinado à ACL atual do solicitante. |
| **Chunk / Embedding** | Trecho versionado derivado de uma fonte e sua representação vetorial. Herda lifecycle/ACL; é projeção reconstruível, não fonte canônica. |
| **Índice Qdrant** | Serviço vetorial opcional da Wave 18. Guarda vetores e metadata mínima; PostgreSQL continua SoT e revalida ACL antes de devolver conteúdo. |
| **MCP server** | Serviço externo compatível com Model Context Protocol que expõe prompts, resources ou tools. Registro no VibeChat é tenant/workspace-scoped, com endpoint/auth/egress governados. |
| **MCP tool / grant** | Função exposta por servidor MCP e a concessão local que limita qual bot pode chamá-la, sob quais scopes, classificação, quota e aprovação. Descrição do servidor não substitui policy local. |
| **BotRun** | Execução imutavelmente ligada a uma BotVersion, ator/delegador, conversa, sources/tools, decisions, custo e stop reason. Não persiste chain-of-thought. |
| **Guardrail** | Policy versionada aplicada ao input, contexto, tools e output para limitar dados, custo, ações e destinos. Falha de guardrail não autoriza tool/egress. |
| **Approval de tool** | Confirmação humana vinculada a run, tool, schema, target, argumentos hash e expiração. Alterar argumentos ou revogar acesso invalida a aprovação. |

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
