# ADR-012: Integração de IA

## Status: Accepted

## Contexto

Recursos de IA (resumo de thread, sugestão de resposta, etc.) são desejáveis, mas não podem acoplar o núcleo do chat a um vendor nem vazar dados entre tenants. Self-hosters devem poder desligar IA completamente.

## Decisão

Integrar IA de forma **opcional e atrás de interface**:

```text
IAiAssistant → NoOpAiAssistant (default)
            → OpenRouterAiAssistant (opt-in)
```

- Feature flags por tenant
- Contexto enviado ao modelo **somente** com dados já autorizados ao usuário
- Sem AI no caminho crítico de envio de mensagem
- Segredos de API só em variáveis de ambiente / secret store
- Logs sem prompts completos por padrão (redação)
- Documentar como adicionar features em `docs/operations/adicionar-feature-ia.md`

OpenRouter é o adapter inicial (múltiplos modelos); outros providers implementam a mesma interface.

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
