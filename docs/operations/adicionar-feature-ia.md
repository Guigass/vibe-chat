# Como Adicionar uma Feature de IA — VibeChat

## Princípios (ADR-012)

1. IA é **opcional** — default off
2. Sempre atrás de `IAiAssistant` (ou especializações estreitas)
3. Nunca no caminho crítico síncrono de `SendMessage` sem fila
4. Contexto **só** com dados autorizados ao usuário/tenant
5. Sem vazamento cross-tenant em batches/caches de prompt

## Passos para uma nova feature

### 1. Especificar o caso de uso

Ex.: “Resumir thread”, “Sugerir resposta”, “Extrair action items”.

Definir:

- Entrada (ids de conversation/message)
- Saída (DTO)
- Quem pode acionar (permission)
- Se o resultado é efêmero ou persistido

### 2. Feature flag

```text
AI__Enabled=true|false
AI__Features__ThreadSummary=true|false
AI__Provider=None|OpenRouter
```

Flags por tenant em settings (quando existir).

### 3. Contrato

Em `VibeChat.Contracts` (ou extensão AI):

- Request com `TenantId`, `UserId`, `ConversationId`, escopo
- Response tipada
- Erros: `AiDisabled`, `ProviderError`, `ContextTooLarge`

### 4. Coleta de contexto (ACL first)

```text
Request → AuthZ CanAccess → carregar N mensagens → redigir segredos óbvios → Prompt
```

Nunca aceitar “coleção livre de messageIds” sem checar cada uma.

### 5. Execução

Preferências:

| Modo | Quando |
|------|--------|
| Síncrono curto | UX de sugestão &lt; poucos segundos, com timeout |
| Async via outbox/job | Resumos longos, fan-out, custo alto |

Implementar adapter `OpenRouterAiAssistant` sem vazar HTTP client para o domínio.

### 6. Persistência (se houver)

- Tabela `ai_artifacts` com `tenant_id`, autor, input refs, modelo, tokens
- Não sobrescrever mensagens de usuários com texto de IA sem marcação clara (`author_type=ai` / bot user)

### 7. Observabilidade e privacidade

- Métricas: `ai_requests_total{feature,provider,result}`
- Traces sem prompt completo no atributo default
- Logs: tamanhos, latência, error codes — não corpo da conversa

### 8. Testes

- Unit: montagem de prompt / truncamento
- Integration: flag off → NoOp / 404 feature
- Security: usuário sem acesso à thread → 403; tenant B ids → 403
- Contrato: provider falha → erro controlado, sem quebrar hub

### 9. UX

- Opt-in visível; disclaimer se dados saem do perímetro
- Loading / cancel
- Estética alinhada ao design system (sem “pill roxo de AI”)

## Template de checklist de PR

- [ ] Flag default false
- [ ] AuthZ + tenant
- [ ] Adapter via interface
- [ ] Timeout e limites de tokens
- [ ] Sem PII extra em logs
- [ ] Docs de ops atualizadas se config nova
- [ ] Custo estimado documentado para o admin

## O que evitar

- Chamar OpenRouter direto do módulo Messaging
- Enviar o workspace inteiro “por garantia”
- Treinar / logar prompts em terceiros sem aceite do admin
- Bloquear envio de mensagem se IA estiver down
