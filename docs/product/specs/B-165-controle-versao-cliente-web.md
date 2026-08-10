# B-165 — Controle de versão do cliente web (cache / PWA)

> Wave W7-9 · Trilha D/A · Deps: W4-7 (B-029), W6-8 · Decisões: D-05, D-20 · Risco R1

## Problema

Após deploy do web (Compose/`ng serve` rebuild), o browser frequentemente permanece
em assets antigos: `index.html` e o service worker (`ngsw`) servem shell/JS
stale. O usuário fica em UI/API desencontradas até limpar cache manualmente —
efeito observado no lab e previsto em
[`release-versionamento-suporte.md`](../../operations/release-versionamento-suporte.md)
(“cache antigo não pode corromper dados”).

Hoje há `environment.appVersion` estático e `GET /api/v1/admin/version` (só
admin); não há detecção de update do SW, nem headers anti-cache no `index.html`,
nem sinal público de build para o cliente comparar.

## Escopo

- Expor um identificador de build do web (versão SemVer + hash/build id curto)
  gerado no build e embutido no cliente.
- Endpoint ou artefato estático público de versão do web (sem auth), alinhado ao
  build publicado — ex.: `/version.json` ou meta no `index.html`.
- Detectar update do Angular Service Worker (`SwUpdate`) e oferecer reload
  explícito (banner/toast com CTA “Atualizar”), sem reload silencioso que apague
  rascunho em digitação.
- Em mismatch cliente × build publicado (após checagem leve no boot/focus),
  mostrar o mesmo caminho de atualização.
- Headers de cache no nginx do web: `index.html` / `ngsw.json` / `version.json`
  sem cache agressivo; assets hashed podem continuar imutáveis.
- Documentar no runbook de upgrade o comportamento esperado pós-deploy do web.

## Fora de escopo

- Versionamento SemVer de release do monorepo (já em
  `operations/release-versionamento-suporte.md`).
- Matriz desktop/mobile (B-141 / B-063) e sync offline rico (B-143).
- Forçar logout ou derrubar sessões SignalR só por bump de patch.
- Schema registry de eventos (B-139) ou breaking de API `/api/v1`.
- Dependabot / bumps de pacote (B-076).

## Contratos

- Artefato público de versão do web: campos mínimos `name`, `version`,
  `buildId` (ou equivalente), sem secret e sem dado de tenant.
- `GET /api/v1/admin/version` permanece admin; se reutilizado, não vira
  dependência do caminho de update do PWA anônimo.
- Mudança de contrato HTTP público (se houver endpoint novo na API) atualiza
  `docs/architecture/contratos.md` no mesmo PR.
- Não altera eventos de hub nem schema de mensagens.

## UX

- Banner/toast discreto com tokens `--vc-*`: texto curto em pt-BR (“Nova versão
  disponível”) + botão primário para recarregar.
- Não interromper composer com texto não enviado: preferir avisar e deixar o
  usuário confirmar o reload.
- Admin overview pode continuar mostrando a versão; idealmente a mesma fonte do
  build embutido.
- Sem cards desnecessários; motion sutil se houver animação de entrada do aviso.

## Multi-tenant e authZ

- Versão/build do web é da instância (global), não por tenant.
- Endpoint/artefato público não vaza tenant, membership nem secret.
- Update de SW não altera authZ; após reload, sessão OIDC/DevAuth segue o fluxo
  já existente.

## Aceite

- [x] Build do web embute `version` + `buildId` distintos entre dois deploys.
- [x] Artefato/endpoint público de versão responde sem autenticação e sem PII.
- [x] Com SW ativo, publicar build novo faz o cliente oferecer atualização sem
      limpeza manual de cache.
- [x] `index.html` (e manifesto SW / version) não ficam com cache longo no nginx
      de referência.
- [x] Reload só ocorre após ação do usuário (ou política documentada equivalente
      que preserve rascunho).
- [x] Runbook/upgrade menciona o fluxo de cache do web.
- [x] Fecha **UX-007** quando o achado estiver registrado.

## Testes

- Unit/Vitest: serviço de versão / reação a `SwUpdate.versionUpdates` (mock).
- Smoke/E2E: após troca simulada de `buildId`, o aviso aparece e o CTA recarrega.
- Teste de headers no nginx do web (`Cache-Control` em `index.html` / version).
- Regressão: login, envio de mensagem e PWA installability continuam ok.

## Riscos

- Reload agressivo perde rascunho → mitigar com confirmação e, se existir, B-086.
- Polling excessivo de versão → checagem no boot, focus/visibility e intervalo
  longo; sem hot path de `SendMessage`.
- Divergência lab (`ng serve` sem SW) vs Compose (SW on) → documentar os dois
  caminhos; em dev sem SW, mismatch via `buildId`/HMR ainda deve ser seguro.
- Cache de CDN/proxy externo fora do nginx de referência → documentar
  responsabilidade do operador.
