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
| **SCIM** | Adapter de provisionamento 2.0 por tenant (B-128). Token hasheado, usuários e grupos com `externalId` único, mapeamento allowlisted para membership. Não escreve no Keycloak e não apaga conteúdo ao desativar. |

## Estrutura de conversa

| Termo | Definição |
|-------|-----------|
| **Space** | Agrupamento organizacional de **canais** dentro de um workspace (ex.: área, projeto). Não organiza a lista de contatos — isso é Contact group (B-166). |
| **Template de workspace** | Manifesto declarativo `vibechat.workspace-template.v1` que cria spaces, canais, tópicos e defaults de política de mensagem (B-115). Não contém tenant, membros, mensagens nem secrets. Built-ins: time, projeto, comunidade e incidentes. Aplicação idempotente, com pré-visualização; o onboarding de owner/admin pode ser pulado e retomado. |
| **Channel** | Canal de mensagens (público, privado ou DM). Unidade principal de conversa. Em privado, quem entra é `channel_members` (roster e add/remove em B-186); guest externo é B-040. |
| **DM (Direct Message)** | Canal especial 1:1 (`Direct`) ou grupo pequeno (`GroupDm`, 3–9 participantes), sem Space obrigatório. Quem entra depois não lê o histórico anterior (`JoinedSeq`). |
| **Thread** | Subconversa ancorada em uma mensagem pai dentro de um channel. |
| **Tópico** | Conversa nomeada de um canal em modo `Topics` (B-118). Metadado com título, slug local, estado e tags; as mensagens reutilizam a conversa do tópico. Não substitui a thread. |
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
| **Inbox unificada** | Fila de atenção com menções, DMs não lidas, threads seguidas, anúncios pendentes e itens salvos. A leitura continua no cursor canônico; o snooze é estado pessoal (B-117). |
| **Read cursor** | Posição de leitura persistente por (`tenant`, `user`, `channel`) em `messaging.read_cursors` (`lastReadSeq`). Fonte da verdade das **não lidas** (badge, divisor, push); sobrevive a F5 e multi-dispositivo (B-094). Não confundir com estado só em memória no client. |
| **Unread / Não lidas** | Contagem derivada de `maxSeq - lastReadSeq` (e menções). Badge na sidebar hidrata do servidor; só zera de forma definitiva ao avançar o cursor. |
| **Reaction** | Reação emoji (ou similar) a uma mensagem. |
| **Enquete / Poll** | Mensagem com pergunta e 2–10 opções (`messaging.polls`). Voto único ou múltiplo; anônima ou visível; prazo opcional. Auditor lê e não vota. |
| **Mensagem agendada** | Envio futuro do autor (`messaging.scheduled_messages`): horário de parede + fuso IANA, instante UTC canônico. No disparo revalida membership e usa o comando de mensagem com idempotency key estável (B-113). |
| **Lembrete** | Aviso pessoal sobre um horário, uma mensagem ou uma thread (`messaging.reminders`). Não é visível ao canal. Push segue as preferências do dono (B-113). |
| **Attachment** | Arquivo associado a mensagem; bytes no object storage (MinIO); metadados no PostgreSQL. |
| **Busca com filtros** | FTS PostgreSQL em mensagens visíveis ao membership (`GET /search/messages`). Filtros `de:`/`em:`/`antes:`/`tem:` só restringem; nunca ampliam ACL (B-098). Matching versátil (prefixo, acento, websearch, canal/pessoa/anexo) entregue em **B-188**. Distinta da busca semântica (B-121 / ADR-016). Atalho: `Ctrl/Cmd+Shift+F`. |
| **Paleta de comandos** | Overlay de navegação (`Ctrl/Cmd+K`) sobre canais com membership, pessoas visíveis, ações locais e comandos de `GET /commands` (B-087 / B-099). Não substitui a busca de mensagens. |
| **Announcement / Anúncio** | Canal `Announcement` (B-112): só quem tem `announcement.publish` escreve; os demais leem. A mensagem continua `Message` + `seq`. Confirmação opcional até um prazo é leitura explícita, não assinatura legal. |
| **Decision / Decisão** | Compromisso curado ligado a uma mensagem (`messaging.commitments`, kind `decision`). Guarda título, hash de excerpt e referência; não copia o body (B-119). |
| **Action item** | Ação estruturada derivada de uma conversa e ligada à mensagem de origem (`kind=action_item`, B-119). Não copia o body. |
| **Task / Tarefa** | Trabalho pessoal ou de time derivado de uma mensagem (`messaging.tasks`, B-123). Guarda responsáveis, prazo, prioridade, checklist ordenado e versão. A origem permanece a mensagem; o body não é copiado. `[[task:uuid]]` resolve esta entidade. |
| **Form / Formulário** | Coleta estruturada (`messaging.forms`, B-124). O schema publicado fica imutável em `form_versions`. A resposta não muda quando o formulário é republicado. A decisão registra o actor autenticado. Campo oculto não entra na resposta. |
| **Automation / Automação** | Regra declarativa trigger → condições → ações (`automation.definitions`, B-125). A versão publicada não muda. A execução usa identidade própria e só as capabilities concedidas. Segredo não entra no log do run. |
| **Incident room / Sala de incidente** | Canal privado criado por template, com severidade, commander, responders, playbook e timeline de referências (`incident.rooms`, B-126). O papel na sala não concede `workspace.admin`. O postmortem aponta para a evidência e não copia o body. |
| **Conector** | Instalação por workspace que recebe um evento externo assinado e produz um efeito único em canal, tarefa ou incidente (`connectors.installations`, B-127). O segredo fica no envelope e a resposta só mostra máscara. A chamada externa não usa a outbox de mensagens. |
| **Página de conhecimento** | Texto curado em Markdown restrito, dentro de uma coleção com escopo de workspace, space ou canal (`knowledge.pages`, B-120). Versionamento otimista e histórico append-only. Não copia o body da mensagem: a referência `[[…]]` é resolvida na leitura. |
| **Digest / Catch-up** | Resumo pessoal diário/semanal ou sob demanda (B-122). Guarda ids, não o body. A leitura revalida a ACL e não move o cursor. IA só com a busca semântica ligada. |

## Tempo real e presença

| Termo | Definição |
|-------|-----------|
| **Presence** | Estado online/away/offline do usuário, tipicamente no Redis com TTL. |
| **Status personalizado** | Texto curto e emoji do próprio usuário, por tenant, com expiração (B-116). Distinto da presence e do perfil público (B-167). O usuário controla o conteúdo. |
| **Disponibilidade** | Valor derivado de presence, DND e status (`available`, `away`, `busy`, `vacation`, `offline`). Não publica agenda. DND continua mandando na notificação (B-097). |
| **Typing** | Indicador de “digitando…” com TTL curto no Redis. |
| **Hub / Connection** | Conexão SignalR autenticada; grupos por tenant/workspace/channel. |
| **Web Push** | Notificação do navegador via protocolo Web Push + VAPID da instância (D-13 / B-095). Opt-in por dispositivo; payload mínimo; off por default (`Push:Enabled`). Distinto de e-mail (B-043) e de preferências/DND (B-097). |
| **Backplane** | Redis como backplane SignalR para fan-out entre instâncias da API. |

## Identidade e acesso

| Termo | Definição |
|-------|-----------|
| **User** | Perfil humano local estável usado como ator. Autenticação vem de External Identity no IdP; memberships e papéis continuam no VibeChat. |
| **Locale** | Preferência pessoal de idioma da UI (`pt-BR`, `en`, `es`, `fr`, `de`, `it`, `ja`, `zh-CN`, `ko`, `ru`), persistida no perfil (`GET`/`PUT /me`) — não é setting de tenant (B-100 / D-14). |
| **Perfil público** | Ficha do membro no workspace (displayName, cargo/função, sobre, mensagem de destaque, avatar), editável pelo dono e legível por membros autorizados do mesmo workspace — não é exposição na internet aberta (B-167). Distinto de status temporário (B-116) e de preferências pessoais (locale/DND/read receipts/wallpaper/accent). |
| **Personalização visual** | Preferências pessoais de aparência da conversa, por tenant e usuário: wallpaper curado da timeline (`chatWallpaperId`) e accent de destaque (`accentColorId`) em catálogo fechado (B-185). `GET /me` lê; `PUT /users/{userId}/appearance` grava só o dono. `null` é o padrão do produto. Distinto de light/dark global (B-049), densidade e de branding por tenant (B-140). |
| **White-label** | Marca da organização por tenant: nome exibido, logos claro/escuro, favicon e duas cores com contraste mínimo (B-140 / D-24 / ADR-055). Sem CSS/JS/HTML arbitrário. O login público usa o host do operador; About/admin continua identificando VibeChat. |
| **Cliente desktop** | Shell Tauri 2 sobre o mesmo web (B-141 / D-20 / ADR-056). Scheme `vibechat://v1`, update assinado e aparelho sem coluna de token. Flag `Desktop:Enabled` off. A PWA continua sendo o cliente canônico. |
| **Cliente mobile** | Shell Capacitor 7 sobre o mesmo web (B-063 / D-20 / ADR-057). iOS e Android, scheme `vibechat://v1`, share com confirmação, push só com ids e aparelho sem coluna de token. Flag `Mobile:Enabled` off. Sem conta de loja a distribuição é sideload. A PWA continua sendo o cliente canônico. |
| **Person** | Pessoa natural, quando conhecida. Não é credencial, sessão, membership nem identificador de autorização. |
| **External Identity** | Vínculo autenticado `(issuer, subject)` vindo de IdP. E-mail é atributo mutável, não chave canônica. |
| **Principal** | Ator autorizável: User, Guest, Bot, Service Account ou Automation. Papel pertence ao vínculo/escopo, não globalmente ao principal. |
| **Device** | Instalação registrada de cliente, usada por push, offline, remote logout e E2EE. Não concede acesso sem sessão/membership atual. |
| **Fila offline** | Comandos locais de envio, edição, reação e tarefa (B-143 / ADR-059), cifrados por conta e tenant. O sucesso só existe depois do ack. Flag `Features:OfflineWrite:Enabled` off. |
| **Session** | Sessão autenticada e revogável associada a principal e, quando disponível, device. Token não substitui revalidação de membership. |
| **Cadastro / Provisionamento** | Fluxo em duas camadas: (1) **autenticação** no IdP (Keycloak/OIDC cria a identidade); (2) **autorização** no VibeChat via Membership + Diretivas. Login sozinho **não** concede acesso a workspace — falta membership. Admin convida por e-mail (`POST .../members`) e atribui papel; perfil stub `pending:{email}` é vinculado no primeiro SSO. Seed/demo permanece para DX. Sem self-signup aberto na fase 1 (B-068). |
| **Diretiva** | Regra de autorização derivada do papel (`Role` + `RolePermissionCatalog` / policies de workspace). Ex.: quem pode `channel.create`, `workspace.admin`, `admin.dashboard`, `ai.summarize`, `ai.suggest_reply`. Gerenciada na UI admin junto ao cadastro. Não confundir com “prompt” de IA. |
| **OIDC / SSO** | OpenID Connect; fluxo padrão via Keycloak. SSO corporativo (B-164) é brokering OIDC/SAML no Keycloak: o app continua só com o JWT do Keycloak, e o rótulo do botão é o `displayName` do IdP. |
| **Service Account** | Identidade de máquina para workers/integrações. |
| **Guest** | Membership de escopo reduzido a **um canal**, criada por convite de admin com link de uso único e validade (default 7 dias). Guest envia mensagem, anexo e reação no canal do convite; não lista outros canais, não usa busca global, não vê o diretório do workspace e não lê configuração. Revogação pelo admin encerra sessão e membership. Convite e aceite geram audit. Entregue em W10-10 / B-040 (ADR-024). |

## Administração e compliance

| Termo | Definição |
|-------|-----------|
| **Audit log (ações)** | Trilha de eventos sensíveis (`audit.audit_events`: login admin, role change, message.send/delete, etc.). Feed em `/admin` — B-042. Com `contentAuditEnabled=true` (B-169), `message.delete` inclui snapshot do body no metadata (sobrevive a purge); com `false`, só ids/timestamps. |
| **Auditoria de conversa** | Visão ADMIN do histórico de um canal/DM/thread (conteúdo, edições, soft-delete, autores, anexos) para compliance — B-067; distinta do audit log de ações. Condicionada a `contentAuditEnabled` (B-169): desligada, auditores não leem body e E2EE (B-064) fica permitido; ligada, soft-delete permanece legível na conversa e no snapshot de `message.delete`. |
| **Configuração sensível** | Tokens, webhooks, chaves de AI/SMTP/VAPID e secrets de integração. Só administradores leem/alteram via `/admin` + `GET/PUT /api/v1/admin/settings` (`workspace.admin`); membros/Auditor → 403; resposta só com máscara/`configured` (B-069). Secrets de **infra** (DB, IdP, MinIO, keyring) ficam só em env/secret store (D-04). **Produto** (OpenRouter, SMTP, HMAC, VAPID, flags) vai ao DB quando `RuntimeSettings:DatabaseOverridesEnabled` (ADR-020 / B-187). `.env` permanece só infra. |
| **Webhook outbound** | Endpoint HTTP do tenant que recebe fan-out assinado com HMAC-SHA256 (`X-VibeChat-Signature`, `X-VibeChat-Event`). Até 5 por tenant (B-108 / ADR-026): eventos `MessageCreated` (default), `MessageEdited`, `MessageDeleted`, `ReactionChanged`; filtro de canal opcional; ping `WebhookTest`. Secret mascarado. Só `workspace.admin`. Com `Integrations:EventSchemas:Enabled` (B-139, off por default) a entrega também valida `schemaVersion`. |
| **Schema de evento público** | Contrato versionado (`eventType` + `schemaVersion`) dos eventos que saem para webhook. Fonte: `PublicEventCatalog`. v1 de `messaging.message.created` é o body atual; v2 é o envelope e coexiste até 2027-04-19 (B-139 / ADR-041). |
| **Plugin / Integração** | Pacote instalável por manifesto + capabilities + identidade Bot. Núcleo de envio: **B-109**; install/gestão local: **B-110 Done**; capabilities avançadas: **B-066 Done** (slash, callback HMAC, ganchos, flag off, ADR-049); interações governadas: **B-111 Done** (ações, grant de janela, anexo, ADR-050); SDK e contract-test kit: **B-136 Done** (flag off, ADR-051); registry assinado: **B-137 Done** (mirror offline, flag off, ADR-053). Sem billing (D-18). |
| **Integração / Bot API** | Identidade `Role.Bot` + token para sistemas externos **enviarem** mensagens (capability `messages.send`, B-109). Base de todo plugin na 1ª onda. |
| **Portal do desenvolvedor** | Superfície admin para credencial, OpenAPI, eventos e chamadas sintéticas (B-135). O segredo aparece uma vez. O explorador não grava mensagem nem chama URL externa. Flag `Integrations:DeveloperPortal:Enabled`. |
| **Export de workspace** | Pacote ZIP compliance (`vibechat.workspace.export.v1`) com JSON do workspace (membros, spaces, channels, threads, messages com soft-delete, metadados de anexos — sem binários MinIO). `GET /api/v1/admin/workspaces/{id}/export`; exige `workspace.admin`; audit `workspace.export` (B-046 / D-03). |
| **Legal hold** | Preservação de um escopo (workspace, pessoa, canal, conversa ou intervalo) contra o purge (B-129 / ADR-042). Exige `compliance.hold`, aprovação de outra pessoa e flag `Features:LegalHold:Enabled`. Remover o hold não apaga na hora. |
| **DLP** | Classificação de dados e bloqueio de ações incompatíveis (B-130 / ADR-044). Rótulos built-in Public, Internal, Confidential e Restricted, com herança workspace → space → canal. Exige grant `dlp` e flag `Features:Dlp:Enabled`. Simulação mede sem bloquear. |
| **SIEM** | Destino HTTPS do tenant que recebe o envelope `audit.event.recorded` v1, assinado, sem body de mensagem (B-132 / ADR-045). Exige grant `audit.export` e flag `Features:Siem:Enabled`. Checkpoint, retry pelo mesmo `eventId` e circuito. |
| **Policy pack** | Baseline técnica versionada (`small-team`, `enterprise-baseline`, `regulated`) que resolve controles efetivos com override explícito (B-133 / ADR-046). Não é certificação. Exige grant `policy` e flag `Features:PolicyPacks:Enabled`. |
| **Escopo delegado** | Permissão administrativa de catálogo fixo (`tenant`, `workspace`, `space`, `compliance`, `security`, `billing-read`) com motivo e expiração (B-134 / ADR-047). Não é papel custom. O grantor não concede acima do próprio escopo nem a si mesmo. Exige flag `Features:DelegatedAdmin:Enabled`. |
| **Quota de workspace** | Teto soft/hard por recurso (`users`, `channels`, `storage`, `messages`, `automations`, `ai-budget`). O hard limit é atômico. A tela mostra uso, tendência e a ação (`none`, `warning`, `blocked`). |
| **Lifecycle de storage** | Ledger de bytes por tipo (`attachment`, `preview`, `export`, `recording`), com reserva, arquivo, GC e tombstone (B-145 / ADR-060). Flag `Features:StorageLifecycle:Enabled` off. A URL de CDN não leva a object key. |
| **Capacity model / SLO** | Envelope de carga (`small`, `standard`, `large`) e objetivos de disponibilidade, lag e recuperação da instalação (B-146 / ADR-048). Standard é 99,9% (D-25) e a recuperação segue D-28. Não é SLA. O burn rate só aparece para `workspace.admin`, com a flag `Features:CapacitySlo:Enabled`. |
| **eDiscovery** | Caso com custodians e query, snapshot `vibechat.ediscovery.export.v1` e checksum (B-129). Exige `compliance.discovery`, separado de `workspace.admin`. |
| **Importação assistida** | Job `vibechat.import.v1` que valida, simula, prepara e publica pessoas, estrutura e histórico no workspace do ator (B-153 / ADR-030). Flag `Features:Import:Enabled` default off. Exige `workspace.import`. Autor externo vira perfil histórico sem membership. Relatório não traz body nem segredo. |
| **Backup de chat** | Pacote `vibechat.chat.backup.v1`, tarefas e destinos remotos (SFTP/FTP, SMB, S3, Drive, WebDAV) de um workspace (B-172 / ADR-061). Segredo mascarado. Flag `Features:ChatBackup:Enabled` off. Distinto do backup operacional (B-031 / `backup-restore.md`) e do export compliance (B-046). |
| **Quarentena de anexo** | Estado em que o arquivo já está no storage, mas não baixa. Com `Files:MalwareScan:Enabled` (B-131 / ADR-040, off por default) o worker marca `Clean`, `Quarantined` ou `ScanFailed`. Admin libera ou apaga com justificativa; outro tenant não vê a fila. |
| **Retenção / purge** | Política por tenant (`messaging.message_retention_settings`: `enabled`, `retentionDays`) + kill switch de processo `MessageRetention:Enabled` (off default; SoT DB de instância quando overrides on — B-187). Com ambos ligados, o worker hard-deleta soft-deletes além do cutoff; remove reactions; detach anexos (sem delete MinIO); audit `message.purge` (B-047 / ADR-018 / D-03). |
| **Versão de mensagem** | Body anterior gravado a cada edição quando `Messaging:History:Enabled` está ligado (`messaging.message_versions`, B-114 / ADR-029). A leitura segue `message.history.read` e a política do workspace. |
| **Mensagem movida** | Cópia com `seq` novo no canal de destino e tombstone neutro na origem (`<system:moved>`), sem revelar o destino a quem não é membro. Comando idempotente `message.move` (B-114). |
| **Política de edição/apagar** | Regras por workspace sobre quem pode editar/apagar mensagem e por quanto tempo após o envio (`messaging.edit.*` / `messaging.delete.*` em admin settings; B-107 / ADR-025). Papéis do catálogo + janela em minutos + override de moderação. Distinta de retenção/purge. A **superfície de UI** de editar é o composer (B-173), não a bolha. O body soft-deleted permanece na linha; o snapshot no audit (`message.delete` com body) continua obrigação de B-169 quando `contentAuditEnabled=true`. |
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
| **Bridge** | Integração que replica eventos/identidades entre VibeChat e outra rede; implica cópia de dados fora do domínio local. **B-138 Done**: contrato `vibechat.bridge.envelope.v1` fora do processo, pin de host, consentimento, identidade remota marcada e delete remoto não garantido (ADR-054, flag off). |
| **Federação** | Comunicação server-to-server entre instâncias independentes, com identidade e dados distribuídos. Não confundir com multi-tenancy local. |
| **Import batch** | Execução versionada e idempotente que transforma export externo em recursos canônicos após dry-run/staging; não é bridge contínua. |
| **Support bundle** | Pacote `vibechat.support-bundle.v1` allowlisted, sanitizado, com checksum, TTL de 15 minutos e no máximo 3 downloads (B-154). Flag `Features:SupportBundle:Enabled` default off. Exige `support.bundle`. Não inclui body, token nem secret. |

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
| **Hot path** | Caminho quente de envio, history, fan-out, outbox, busca, upload ou export. B-170 lista o dono e o SLI de B-146. |
| **Alavanca de escala** | O que cresce primeiro dentro do Compose (réplica de API, worker, nó do Postgres) antes de bus, OpenSearch ou Kubernetes. |

## Siglas úteis

| Sigla | Significado |
|-------|-------------|
| **ADR** | Architecture Decision Record |
| **PWA** | Progressive Web App |
| **CDK** | Angular Component Dev Kit |
| **SSO** | Single Sign-On |
| **S3** | API de object storage compatível (Amazon S3) |
| **LTS** | Long-Term Support |
