# ADR-003: ASP.NET Core (.NET 10 LTS)

## Status: Accepted

## Contexto

O backend precisa de performance alta em I/O (WebSockets/SignalR), tipagem forte, ecossistema maduro para identidade, EF/Postgres, observabilidade e suporte LTS para ambientes corporativos self-hosted.

## Decisão

Usar **ASP.NET Core em .NET 10 LTS** para `apps/api` e `apps/worker`.

- Minimal APIs ou Controllers de forma consistente por módulo
- DI nativo como composition root
- OpenTelemetry packages oficiais
- C# moderno; nullable reference types habilitado

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Node (Nest/Fastify) | SignalR/.NET backplane e tipagem corporativa menos alinhados à escolha de stack |
| Go | Excelente perf, mas menos produtividade para domínio rico + ecossistema SignalR |
| Java/Spring | Válido, porém time e ADRs deste projeto priorizam .NET |
| .NET não-LTS / preview apenas | Inadequado para self-host enterprise |

## Consequências

- **+** SignalR de primeira classe; performance e tooling excelentes
- **+** LTS facilita suporte e patches de segurança
- **−** Requer SDK .NET no DX e imagens Docker adequadas
- **−** Worker e API devem alinhar versões de contratos na mesma release
