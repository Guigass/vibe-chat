# B-066 — Plataforma de plugins / integrações (capabilities avançadas)

> Wave 15 · Trilha B/C/D/E · Deps: B-109, B-110, B-108 · Risco R2
> Regras comuns: [`long-term-common.md`](long-term-common.md)

## Problema

Depois do núcleo de envio (B-109) e do shell de instalação local (B-110), ainda
faltam **capabilities extras** que tornam plugins “legais”: slash do plugin,
assinatura rica de eventos inbound, hooks de UI, catálogo built-in curado no
repo. O antigo nome “Marketplace de bots” sugeria loja pública — isso **não**
entra (D-11). Esta fatia é a **última** da trilha de integração.

## Escopo

- Estender o manifesto do plugin (`capabilities[]`) além de `messages.send`:
  - `commands.slash` — comandos registrados pelo plugin (reusa modelo B-087 no
    servidor; handler HTTP do plugin ou fila outbox → callback URL)
  - `events.subscribe` — assinatura inbound além do send (complementa B-108 no
    sentido inverso: VibeChat → plugin URL com eventos escolhidos)
  - `ui.hooks` (opcional, mínimo) — pontos documentados (ex. badge “bot”, menu
    de mensagem) sem carregar JS arbitrário de terceiros na primeira onda de W15
- **Catálogo built-in** versionado no repositório (manifestos oficiais),
  instalável só na instância via B-110 — **sem** registry remoto / loja
- Documentar contrato de manifesto estável em `contratos.md`
- Admin continua `workspace.admin`; visibilidade B-106

## Fora de escopo

- Marketplace / App Directory público, OAuth de apps de terceiros, billing
- Sandbox executando DLL/JS não confiável no processo da API
- Registry remoto e distribuição pública (entram em B-137)
- Substituir B-109/B-110 — esta fatia **só** adiciona capabilities em cima deles

## Contratos

- Versionar schema do manifesto (`vibechat.plugin.manifest.v1`)
- Novas capabilities opt-in; default de plugin novo continua só `messages.send`
- Callbacks do plugin: HTTPS + HMAC (paridade B-048); timeout curto; RLS/tenant

## UX

- Admin → Plugins: “Capabilities” por plugin instalado; built-ins do repo
  aparecem como instaláveis
- Slash do plugin aparece na paleta/lista de B-087 com prefixo/namespace

## Multi-tenant e authZ

Igual B-109/B-110. Callbacks nunca cruzam tenant. Token/secret só hash.

## Aceite

- [ ] Plugin built-in do repo instala via B-110 e declara capability extra
- [ ] Slash do plugin executa só com capability e escopo
- [ ] Sem endpoint de “loja” ou download de pacote remoto arbitrário
- [ ] D-11 respeitado: zero App Directory público

## Testes

- Integration: capability gate; callback HMAC; cross-tenant negativo
- Security: manifesto malicioso / URL SSRF (mesma régua B-091/B-048)
- E2E smoke: instalar built-in → slash → mensagem

## Riscos

- Escopo creep para registry → manter distribuição remota em B-137
- Runtime de código de plugin → manter modelo **config + HTTP callbacks**, não
  process-in-process na primeira entrega de W15
