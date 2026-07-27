# B-111 — Interações governadas para plugins

> Wave 15 · Trilha B/C/D/E · Deps: B-066 · Risco R2
> Regras comuns: [`long-term-common.md`](long-term-common.md)

## Problema

B-066 registra comandos e eventos, mas integrações úteis ainda precisam
interagir com mensagens e anexos sem receber acesso genérico ao workspace.

## Escopo

- interactive messages com botões e ações namespaced;
- leitura de histórico limitada a canais e janelas explicitamente concedidos;
- upload e envio de anexos pela API de integração;
- catálogo built-in ampliado, versionado no repositório;
- revogação imediata das concessões e trilha de audit por ação;
- quotas por plugin, tenant e capability.

## Fora de escopo

- código de terceiros executado dentro da API/web;
- App Directory comercial, billing e instalação sem revisão;
- registry e pacotes remotos, tratados por B-137;
- acesso implícito ao histórico por possuir token.

## Contratos

Versionar actions, grants de canal/janela e media API. Toda chamada carrega
identidade do plugin, tenant, capability, grant e idempotency key. Respostas não
revelam a existência de canais fora do escopo. Eventos de grant/revoke/action
entram em audit.

## UX

Admin vê capabilities, canais concedidos, último uso, quota e botão de revogar.
Usuário vê claramente quando uma ação pertence a um plugin. Estados obrigatórios:
ação expirada, plugin desativado, capability negada, timeout e retry seguro.

## Multi-tenant e authZ

Grants são sempre tenant-scoped e intersectados com o escopo do plugin; nunca
substituem membership/autorização do ator. Histórico e anexos exigem grants
separados. Revogação invalida tokens/caches e bloqueia callbacks posteriores.

## Aceite

- [ ] Ação interativa é autenticada, idempotente e auditada.
- [ ] Plugin só lê a janela/canal concedidos.
- [ ] Upload respeita tipo, tamanho, quota, scan e tenant.
- [ ] Revogação impede nova leitura/ação imediatamente.
- [ ] Nenhum JS/DLL remoto é executado no processo ou no browser.

## Testes

- contract tests de action/grant/media;
- integration para idempotência, quota, revogação e callback;
- security cross-tenant, channel scope, SSRF e anexos maliciosos;
- E2E instalar built-in, conceder canal, interagir e revogar.

## Riscos

Capability inflation, histórico excessivo e interactive action forjada.
Mitigar com grants mínimos, expiry, assinatura, replay protection, quotas,
auditoria e defaults negados.
