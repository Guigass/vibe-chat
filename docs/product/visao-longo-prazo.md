# Visão de Longo Prazo — VibeChat

## North star

O VibeChat deve evoluir de um chat corporativo self-hosted para uma **plataforma
aberta de comunicação, conhecimento e automação**, na qual uma organização
consegue conversar, decidir, executar trabalho e preservar memória institucional
sem entregar o controle dos dados a um SaaS fechado.

O diferencial não será copiar a soma de Slack, Teams, Discord e WhatsApp. Será
combinar:

1. **comunicação assíncrona organizada**;
2. **governança e soberania de dados**;
3. **automação extensível sem lock-in**;
4. **IA opcional, explicável e controlada**;
5. **operação self-hosted proporcional ao tamanho da organização**.

## Promessa por público

| Público | Promessa de longo prazo |
|---------|-------------------------|
| Colaborador | Encontrar o que importa, responder no contexto e transformar conversa em ação |
| Líder de time | Acompanhar decisões, riscos, tarefas e saúde da comunicação sem microgerenciar |
| TI / Plataforma | Instalar, atualizar, integrar e observar com contratos claros |
| Security / Compliance | Controlar acesso, retenção, classificação, auditoria e export |
| Desenvolvedor de integração | Criar automações e bots por APIs estáveis e capabilities mínimas |
| Comunidade OSS | Estender sem depender de SDK, marketplace ou serviço proprietário |

## Pilares estratégicos

### 1. Conversas que permanecem compreensíveis

- timeline rica, tópicos/threads, inbox, não lidas e busca;
- anúncios, mensagens agendadas, lembretes e resumos;
- histórico de edição e movimentação de conversa;
- redução de ruído por filtros, prioridade e preferências.

### 2. Conversa que vira conhecimento

- decisões e action items vinculados à mensagem de origem;
- resumos e digests verificáveis, com links para evidência;
- páginas leves ou coleções curadas server-authoritative (D-17);
- busca semântica opcional sem enfraquecer ACL ou retenção.

### 3. Conversa que executa trabalho

- tarefas derivadas de mensagens;
- formulários, aprovações e automações;
- playbooks para incidentes, onboarding e processos repetíveis;
- integrações bidirecionais com ferramentas internas.

### 4. Plataforma confiável para organizações sérias

- provisioning, grupos, administração delegada e quotas;
- legal hold, eDiscovery, classificação, DLP e SIEM quando autorizados;
- malware scanning e políticas de arquivo;
- SLOs, capacity planning, lifecycle de storage e recuperação testada.

### 5. Ecossistema aberto e responsável

- APIs e eventos versionados;
- bots e plugins com capabilities explícitas;
- SDKs e exemplos OSS;
- bridges e registry governado em W15, após contratos e controles de segurança;
  marketplace comercial/billing permanece fora.

### 6. IA como camada opcional, não como fundação

- catch-up, resumo, busca, tradução e extração de decisões;
- respostas sempre vinculadas às fontes autorizadas;
- provider externo off por default e alternativa local quando viável;
- controles por tenant/workspace, auditoria, orçamento e política de retenção.

## Princípios de expansão

1. **Profundidade antes de largura:** terminar a qualidade da mensageria antes de
   abrir novas superfícies.
2. **Uma fonte de verdade:** features novas não criam cópias concorrentes de
   mensagem, permissão ou identidade.
3. **ACL acompanha o conteúdo:** busca, IA, export, bridge e notificação revalidam
   autorização.
4. **Assíncrono por padrão:** integrações e IA não entram no hot path de envio.
5. **Capabilities mínimas:** plugins e bots recebem somente o necessário.
6. **Escala por evidência:** bus, OpenSearch e Kubernetes continuam condicionados
   aos ADRs 015–017.
7. **Aposta grande exige evidência:** live media, CRDT, federação, E2EE e clients
   nativos entram na ordem W15–W17, com ADR, feature flag e rollback; multi-região
   write permanece fora.
8. **Delight sem ruído:** personalização, som e motion servem comunicação; não
   transformam o produto em entretenimento visual.

## Horizontes

| Horizonte | Resultado esperado |
|-----------|--------------------|
| H1 — Excelente chat | Waves 7–10 completas; experiência diária confiável |
| H2 — Trabalho conectado | Inbox, ações, automações e integrações maduras |
| H3 — Enterprise soberano | Provisioning, compliance, segurança e operação em escala |
| H4 — Plataforma/ecossistema | SDK, plugins avançados e distribuição governada |
| H5 — Apostas transformadoras | Live, federação, E2EE, canvas e clients nativos com gates R3 |

O detalhamento está em
[`roadmap/horizonte-ambicioso.md`](../roadmap/horizonte-ambicioso.md).

## O que não muda

- Apache-2.0;
- self-hosted first;
- monólito modular até evidência justificar extração;
- PostgreSQL como source of truth;
- multi-tenancy e RLS como invariantes;
- Compose como baseline operacional;
- nenhuma feature envia PII a IA silenciosamente;
- nenhuma dependência proprietária obrigatória.

## Métricas de longo prazo

### Adoção e valor

- tempo até primeira conversa útil;
- usuários ativos semanais e retenção por organização;
- percentual de mensagens lidas/respondidas no contexto correto;
- tempo para localizar uma decisão ou mensagem;
- automações executadas com sucesso.

### Confiança

- incidentes cross-tenant: zero;
- percentual de restores e upgrades testados com sucesso;
- tempo de detecção e recuperação;
- cobertura de audit para ações sensíveis;
- percentual de respostas de IA com fonte e autorização verificadas.

### Plataforma

- tempo para criar uma integração segura;
- estabilidade de contratos e taxa de breaking changes;
- atraso de outbox, fan-out realtime e indexação;
- custo operacional por usuário ativo;
- compatibilidade entre versões suportadas.
