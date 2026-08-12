# Guia do Colaborador

Visão de uso diário do VibeChat.

## Entrar e encontrar seu workspace

### Atual

1. A organização fornece a URL da instância.
2. O login oficial usa OIDC/Keycloak.
3. O usuário só acessa workspaces/canais em que possui membership.
4. DevAuth (botões Alice/Bob/Demo e `X-Dev-User`) existe só no lab local
   (`ENABLE_DEV_AUTH=true` + API Development) e não representa login de
   staging/produção.

Se o workspace esperado não aparecer, procure o administrador; criar outra conta
não concede membership.

## Organização

### Atual

- Workspace: limite principal da organização.
- Space: agrupamento visual de canais.
- Channel: conversa de equipe.
- DM: conversa direta.
- Thread: discussão vinculada a uma mensagem.

Canais privados e DMs só aparecem a membros autorizados.

### Planejado

- DM em grupo: W10.
- Canais de anúncio e confirmação: W11.
- Modo de canal por tópicos/fórum: W11.
- Inbox unificada: W11.

## Enviar mensagens

### Atual

- texto;
- anexo;
- thread;
- reação;
- editar e soft-delete segundo permissões atuais;
- presença e indicador de digitação;
- atualização em tempo real.

O cliente usa uma chave de idempotência e o servidor atribui `seq`; em reconnect,
lacunas são preenchidas pelo histórico.

### Planejado W8

- múltiplos anexos, drag-and-drop, colar e progresso;
- áudio;
- formatação;
- menções;
- emojis/reactions livres;
- responder citando;
- encaminhar;
- rascunho persistente;
- comandos slash.

## Ler e recuperar contexto

### Atual

- busca textual;
- histórico por canal/thread;
- anexos e reações.

### Planejado W9–W12

- agrupamento e separador de não lidas;
- histórico paginado e pular para mensagem;
- previews;
- pins e salvos;
- recibos de leitura persistentes;
- busca com filtros;
- decisões/action items ligados à origem;
- páginas de conhecimento;
- digests e catch-up;
- tarefas e aprovações.

## Notificações

### Planejado W10

- Web Push opt-in;
- preferência por canal;
- DND;
- seguir thread.

A permissão do navegador só deve ser solicitada após ação explícita. Conteúdo
sensível pode ser omitido do payload conforme policy.

## IA

### Atual/opcional

Resumo e sugestão podem existir quando a organização habilita IA.

Regras:

- off por default;
- provider externo exige configuração;
- resposta pode falhar sem quebrar o chat;
- usuário não deve assumir que resumo substitui a fonte.

### Planejado

Busca semântica, digests e notas sempre apontam para evidências autorizadas.

## Guests

### Planejado W10

Guest entra por convite de uso único para um canal, com validade:

- não lista workspace;
- não usa busca global;
- não convida;
- não cria canal;
- não acessa configuração;
- revogação encerra acesso.

## Offline e clientes

- PWA é o cliente canônico.
- Desktop/mobile entram depois de W15.
- Escrita offline real entra em W16.
- Cliente nunca inventa ordem final; servidor continua autoritativo.

## Privacidade e canais E2EE

O servidor self-hosted lê mensagens comuns para entregar busca, moderação,
export e compliance.

Canais E2EE serão opcionais em W16 e terão capacidades reduzidas, claramente
indicadas. Perda de chave pode impedir recuperação.

## Acessibilidade

W10 consolida WCAG 2.2 AA. Sempre deve existir:

- alternativa por teclado;
- foco visível;
- contraste;
- labels para leitor de tela;
- reduced motion;
- alternativa por clique para drag-and-drop.

## Reportar problema

Inclua:

- ação esperada;
- resultado observado;
- tela/URL sem tokens;
- browser e viewport;
- horário e correlation id, se visível;
- screenshot sem conteúdo sensível.

Nunca envie senha, token, cookie, secret ou mensagem privada em issue pública.
