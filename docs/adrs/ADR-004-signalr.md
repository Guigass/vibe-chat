# ADR-004: SignalR

## Status: Accepted

## Contexto

Chat corporativo exige entrega em tempo real (mensagens, typing, presença). É necessário um protocolo com reconexão, grupos por conversa e escala horizontal com backplane.

## Decisão

Usar **ASP.NET Core SignalR** para o canal realtime:

- Hubs autenticados com JWT/OIDC
- Grupos por `tenant` + `conversation` (e/ou channel)
- **Redis** como backplane quando houver múltiplas instâncias de API
- Eventos de domínio chegam ao hub preferencialmente via **outbox → worker → IRealtimePublisher** (não só publish inline no request)

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| WebSockets crus | Reinventar reconexão, grupos, protocol negotiation |
| Socket.IO (Node) | Fora da stack .NET escolhida |
| SSE apenas | Unidirecional; typing/presença e multiplexação piores |
| gRPC streaming para browser | Fricção no cliente web; SignalR cobre o caso |

## Consequências

- **+** Integração natural com ASP.NET e Redis backplane
- **+** Clientes TypeScript oficiais / padrões conhecidos
- **−** History API continua necessária (realtime não é source of truth)
- **−** Multi-instância exige Redis backplane configurado corretamente
