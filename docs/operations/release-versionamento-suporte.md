# Política de Release, Versionamento e Suporte

Política padrão para releases OSS/self-hosted do VibeChat. Não constitui SLA
comercial. Exceção exige ADR e atualização deste documento.

## Versionamento

O projeto usa Semantic Versioning:

```text
MAJOR.MINOR.PATCH[-prerelease]
```

- `MAJOR`: quebra de compatibilidade pública deliberada.
- `MINOR`: capacidade compatível adicionada.
- `PATCH`: correção compatível, documentação ou hardening.
- prerelease: `alpha.N`, `beta.N` ou `rc.N`.

### Antes de 1.0

- Linha `0.y.z` é evolutiva; breaking change ainda exige migration e release note.
- Apenas o minor mais recente recebe correções regulares.
- Correção crítica pode ser aplicada ao minor anterior quando o patch for seguro.
- 1.0 só é elegível após Waves 7–10 concluídas, nenhum finding Critical/High
  aberto e drills de install/upgrade/restore reproduzíveis.

### Depois de 1.0

- Último minor do major atual: suporte funcional e de segurança.
- Minor anterior do mesmo major: correções críticas de segurança e integridade.
- Majors anteriores: sem suporte regular, salvo política LTS publicada.
- Não existe LTS implícita; LTS exige decisão e capacidade operacional após B-144.

## Compatibilidade

| Superfície | Regra |
|------------|-------|
| API `/api/v1` | mudança additive por padrão; remoção exige depreciação |
| Eventos | campos novos opcionais; rename exige dual-publish |
| Banco | migration forward-compatible dentro da janela de rolling upgrade |
| API/Worker | versões adjacentes precisam coexistir na janela definida por B-144 |
| Web/PWA | servidor informa incompatibilidade; cache antigo não pode corromper dados |
| Desktop/mobile | matriz de versões em B-141/B-063; nenhum endpoint privilegiado |
| Plugins | versão de contrato + capabilities; incompatibilidade bloqueia ativação |
| Federação | negotiation explícita; peer incompatível falha fechado |

## Depreciação

1. marcar superfície como deprecated em contrato e release note;
2. disponibilizar substituto;
3. emitir telemetria local e warning sem PII;
4. manter ao menos um minor compatível após o anúncio em 1.x;
5. remover somente em major, salvo vulnerabilidade crítica;
6. fornecer migration e rollback.

Antes de 1.0, o prazo pode ser menor, mas nunca silencioso.

## Cadência

- Release é baseada em evidência, não em calendário obrigatório.
- Uma intenção por PR; `main` deve permanecer liberável.
- Release candidate é recomendada quando migration, cliente ou operação mudarem.
- Patch de segurança crítico pode interromper a fila normal.
- Nenhuma release é publicada automaticamente sem credencial R4 explicitamente
  configurada pelo owner.

## Artefatos

Quando aplicável, uma release deve produzir:

- tag Git assinada ou protegida;
- release notes;
- SBOM;
- checksums;
- imagens OCI por digest, nunca somente `latest`;
- provenance/attestation quando a infraestrutura suportar;
- migrations incluídas;
- matriz de compatibilidade;
- instruções de upgrade e rollback;
- lista de breaking/deprecated;
- resultado dos gates da classe de risco.

## Processo

```text
main verde
  → selecionar SHA
  → build reproduzível
  → testes + security
  → migration/upgrade/restore drill proporcional
  → gerar artefatos e SBOM
  → release candidate quando necessário
  → tag/release por credencial autorizada
  → smoke pós-release
  → publicar notas
```

## Release notes

Seções obrigatórias:

- Highlights;
- Added;
- Changed;
- Fixed;
- Security;
- Deprecated;
- Removed;
- Migrations;
- Configuration;
- Compatibility;
- Upgrade;
- Rollback;
- Known issues.

Não incluir secret, dado real de tenant ou detalhe explorável sem mitigação.

## Segurança

- Vulnerabilidade segue `SECURITY.md`.
- Correção pode permanecer privada até artefato seguro estar disponível.
- CVE/aviso público depende de coordenação responsável.
- Rotação de secret comprometido é ação do operador; o repo fornece runbook.
- Dependência revogada bloqueia release.

## Rollback

Rollback só é declarado suportado quando:

- binário anterior aceita o schema atual, ou existe migration reversa testada;
- eventos não foram publicados em shape incompatível;
- cache/service worker não mantém estado inválido;
- efeitos externos idempotentes são reconciliáveis;
- backup necessário foi concluído.

Se rollback de banco for inseguro, usar roll-forward e declarar isso antes da
release.

## Matriz de suporte da instalação

Cada release registra:

| Item | Valor exigido |
|------|---------------|
| VibeChat | versão/tag |
| PostgreSQL | intervalo testado |
| Redis | intervalo testado |
| Keycloak | intervalo testado |
| MinIO/S3 | intervalo/protocolo |
| Browsers | últimas versões testadas |
| Node/.NET | apenas para build/DX |
| Compose | versão mínima |
| Plugins | contrato mínimo/máximo |
| Perfil operacional | Basic/Dev, Standard ou HA conforme D-28 |

Declarar perfil não basta: Standard exige evidência PITR/restore e HA exige
B-146/B-170/B-144. Instalação Basic/Dev não herda objetivo de disponibilidade.

## Gates para 1.0

- Waves 7–10 terminalmente concluídas;
- install limpo em menos de 30 minutos;
- upgrade da última prerelease;
- restore drill;
- cross-tenant e E2E verdes;
- CSP, supply chain e limites de payload fechados;
- catálogo de configuração coerente;
- política de release aplicada a uma RC;
- nenhum Critical/High aberto.
