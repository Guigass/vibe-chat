# Guia de Integrações, Bots e Plugins

Contrato de experiência para quem estende o VibeChat.

## Caminho de maturidade

```text
webhook outbound
  → bot/token
  → plugin local
  → capabilities avançadas
  → SDK + contract kit
  → registry assinado
  → bridges governadas
```

Não pule direto para código remoto ou registry.

## Princípios

- capability mínima;
- tenant e canal explícitos;
- token apenas uma vez; hash no banco;
- idempotência;
- rate limit;
- timeout/circuit breaker;
- audit;
- eventos versionados;
- nenhum secret em manifesto/log;
- nenhum DLL/JS remoto dentro da API/web.

## Webhook

Entrega atual mínima usa outbox, HMAC e delivery id. Extensões futuras incluem
mais eventos, filtros e retry controlado.

Consumidor:

1. valida assinatura;
2. deduplica delivery id;
3. valida versão;
4. responde rápido;
5. processa assíncrono;
6. não confia em texto/URL recebido.

## Bot/token

Token futuro:

- pertence a tenant/plugin;
- tem capabilities;
- tem grant de canais;
- possui expiry/rotação;
- é rate-limited;
- não herda papel do criador;
- pode ser revogado imediatamente.

## Manifesto

Campos e defaults estão em
[`pacotes-decisao-r3.md`](../../architecture/pacotes-decisao-r3.md).

Manifesto descreve integração; não é pacote de código executável dentro do
VibeChat.

## Compatibilidade

- contrato de API/evento tem versão;
- SDK deriva do contrato;
- plugin declara intervalo suportado;
- breaking change segue depreciação;
- registry bloqueia pacote incompatível/revogado;
- contract tests são requisito de publicação.

## Dados

Plugin declara:

- dados lidos;
- dados escritos;
- destinos externos;
- retenção;
- finalidade;
- capabilities;
- canais.

Delete/revogação e export precisam cobrir artefatos que permanecem no VibeChat.
Dados copiados para serviço externo seguem política explícita do integrador.

## Segurança

Testes mínimos:

- token inválido/expirado/revogado;
- outro tenant;
- canal fora do grant;
- capability ausente;
- replay;
- quota;
- SSRF/callback;
- payload malicioso;
- secret não aparece em resposta/log.
