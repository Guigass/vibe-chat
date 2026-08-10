# ADR-012: Integração de IA

## Status: Accepted

## Contexto

Recursos de IA (resumo de thread, sugestão de resposta, etc.) são desejáveis, mas não podem acoplar o núcleo do chat a um vendor nem vazar dados entre tenants. Self-hosters devem poder desligar IA completamente.

## Decisão

Integrar IA de forma **opcional e atrás de interface**:

```text
IAiCompletionProvider → NullAiProvider (default, Ai:Enabled=false)
                      → MockAiProvider (lab / seed)
                      → OpenRouterAiProvider (opt-in + API key)
```

- Feature flags por tenant
- Contexto enviado ao modelo **somente** com dados já autorizados ao usuário
- Sem AI no caminho crítico de envio de mensagem
- Segredos de API: env/secret store **ou** envelope AES-GCM por workspace no
  PostgreSQL (ADR-020), com chave mestra só no env; API admin nunca devolve
  plaintext — só máscara + rotação dedicada
- `Ai:Enabled` permanece kill switch global de processo (SoT env)
- `OpenRouter:BaseUrl` permanece no env (sem URL arbitrária na UI)
- Logs sem prompts completos por padrão (redação)
- Documentar como adicionar features em `docs/operations/adicionar-feature-ia.md`

OpenRouter é o adapter inicial (múltiplos modelos); outros providers implementam a mesma interface.

### Emenda (ADR-020, 2026-08-10)

A decisão original “só env” é emendada para permitir a API key OpenRouter
criptografada em `ai.settings` quando `RuntimeSettings:DatabaseOverridesEnabled=true`.
Infraestrutura (Postgres, OIDC, MinIO) e o kill switch `Ai:Enabled` **não** mudam.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| SDK de um único LLM acoplado ao Messaging | Vendor lock-in; dificulta self-host air-gapped |
| IA obrigatória | Viola self-host conservative |
| Processar IA no request HTTP do send | Aumenta latência e falhas no caminho crítico |
| Treinar modelo próprio na fase 1 | Fora de escopo |

## Consequências

- **+** Núcleo funciona offline de AI; compliance mais simples
- **+** Troca de provider sem mudar domínio
- **−** Qualidade depende do provider externo quando habilitado
- **−** Política de retenção/PII com terceiros exige aceite do admin (decisão humana)
