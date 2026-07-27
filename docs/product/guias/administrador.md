# Guia do Administrador

Responsabilidades do `workspace.admin` e papéis delegados. Operação de
infraestrutura e secrets pertence ao operador, não ao admin de workspace.

## Separação de autoridade

| Papel | Responsabilidade |
|-------|------------------|
| Member | uso diário |
| Moderator | moderação autorizada |
| Auditor | leitura de audit/conversas conforme capability |
| Admin | membership, papéis, políticas e export do workspace |
| Operador | deploy, env, secrets, backup e upgrade |
| Compliance delegado | legal hold/DLP futuros, com capability separada |

Esconder opção na UI não concede nem remove permissão; API é autoritativa.

## Membership e papéis

### Atual

- convidar/provisionar membro;
- atribuir papel permitido;
- impedir autoelevação;
- auditar convite e alteração;
- autenticação continua no IdP.

### Planejado

- guests por canal em W10;
- SCIM/grupos/deprovisioning em W13;
- administração delegada e quotas em W14.

## Settings sensíveis

### Atual

O admin pode consultar estado mascarado e mudar políticas autorizadas.

- AI/SMTP secret: somente env/secret store.
- Webhook secret: gravável com retorno mascarado.
- Retenção por workspace: admin; kill switch do processo: operador.
- Export: `workspace.admin`.
- Auditor sem `workspace.admin` não lê settings sensíveis.

Nunca copiar máscara como se fosse o secret real.

## Auditoria e export

### Atual

- audit de ações;
- viewer de conversas para capability administrativa;
- export ZIP do workspace;
- soft-deleted pode permanecer visível em contexto de compliance;
- cross-tenant é sempre negado.

### Planejado

- SIEM;
- legal hold/eDiscovery;
- classificação/DLP;
- policy packs;
- cadeia de custódia.

Não apresentar controles alinhados a frameworks como certificação.

## Retenção

- Soft-delete é o default.
- Purge depende de kill switch de processo e política do tenant.
- Export/backup têm lifecycle próprio.
- Legal hold futuro suspende purge apenas no escopo válido.
- Conteúdo derivado (RAG, páginas, digests) deve seguir delete/retention.

## Plugins e integrações

### Atual

Webhook mínimo é tenant-scoped, assinado e auditado.

### Planejado

1. token/bot;
2. instalação local;
3. capabilities e grants;
4. SDK/contract tests;
5. registry assinado;
6. bridges.

Admin concede apenas canais/capabilities necessários. Plugin não recebe acesso
ao workspace apenas por estar instalado.

## IA

Checklist:

- confirmar base legal/política interna;
- escolher provider;
- manter externo off até configurar;
- limitar workspace/capability;
- definir orçamento;
- revisar retenção e classificação;
- auditar uso;
- explicar ao usuário que resposta aponta para fontes.

## Guests

Convite futuro:

- um canal;
- uso único;
- validade padrão de sete dias;
- máximo de trinta dias;
- revogação imediata;
- audit de criação/aceite/revogação.

## Adoção e migração

### Planejado W11

- aplicar template e checklist de onboarding;
- validar export externo em dry-run;
- mapear pessoas, papéis e canais antes de importar;
- acompanhar staging/checkpoints e publicar explicitamente;
- executar preflight de OIDC, e-mail, push, storage e Worker;
- gerar support bundle sanitizado e temporário.

Importação não cria senha nem membership elevada por inferência. Bundle de
suporte não contém mensagens, anexos, tokens ou secrets.

## Canais confidenciais E2EE

Antes de habilitar, o admin vê matriz de capacidades perdidas. E2EE não é
“segurança maior em tudo”; troca busca/compliance/recuperação por confidencialidade
do conteúdo.

## Checklist periódico

- membros e papéis;
- contas inativas;
- integrações e tokens;
- settings/flags;
- retenção;
- exports;
- findings de segurança;
- quotas;
- audit de ações sensíveis;
- canais/guests expirados.
