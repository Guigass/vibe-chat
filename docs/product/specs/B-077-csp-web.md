# B-077 — Content Security Policy no web

> Wave W7-4 · Trilha D/E · Deps: W6-8 · Decisões: D-05

## Problema

O proxy já envia HSTS, `nosniff`, `X-Frame-Options` e `Referrer-Policy`, mas não
há CSP. Isso deixa a defesa contra XSS e carregamento indevido de recursos
incompleta e pode divergir entre acesso direto ao web e acesso pelo proxy.

## Escopo

- Inventariar origens realmente usadas por web, API, SignalR, Keycloak, MinIO e
  fontes/assets.
- Definir CSP mínima com `default-src 'self'` e diretivas específicas.
- Cobrir conexão WebSocket/HTTP em `connect-src`.
- Aplicar política no caminho oficial self-host e documentar diferenças do lab.
- Começar em report-only apenas se houver prazo explícito para enforcement.
- Adicionar teste automatizado dos headers.

## Fora de escopo

- Esconder vulnerabilidade com `unsafe-eval`.
- Permitir `*` em produção.
- Introduzir serviço externo de coleta de relatórios como requisito.
- Substituir authZ, sanitização ou validação de entrada.

## Contratos

Contrato HTTP operacional: respostas do web/proxy devem conter a CSP
documentada. URLs públicas configuráveis precisam ser refletidas na política sem
interpolação insegura.

## UX

Login OIDC, upload/download, PWA, SignalR, temas, imagens e fontes continuam
funcionando. Violação de CSP deve produzir diagnóstico operacional, não uma tela
silenciosamente quebrada.

## Multi-tenant e authZ

CSP é defesa do browser e não substitui isolamento. Nenhum tenant pode fornecer
origens arbitrárias que ampliem a política global.

## Aceite

- [ ] Header CSP existe no caminho oficial.
- [ ] Não usa wildcard global, `unsafe-eval` ou origem HTTP em produção HTTPS.
- [ ] OIDC, SignalR, MinIO e PWA passam smoke/E2E.
- [ ] Acesso direto e via proxy têm comportamento documentado.
- [ ] Teste falha quando o header desaparece.
- [ ] `modelo-ameacas.md` marca o controle com evidência.

## Testes

- Teste de headers no nginx web e proxy.
- E2E de login, envio realtime e anexo.
- Smoke em modo HTTPS.
- Revisão manual do console sem violações inesperadas.

## Riscos

- Quebrar OIDC/WebSocket por `connect-src` incompleto.
- Mascarar política fraca com wildcards.
- Divergência entre nginx do web e proxy; escolher e documentar a fonte canônica.

