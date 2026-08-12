# B-185 — Personalização visual do usuário (wallpapers e cores)

> Wave 11 · Trilha B/D · Deps: B-049 · Soft-deps: B-167 · Decisões: D-11 · Risco R1
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

O shell já tem light/dark e densidade, mas o usuário não escolhe fundo da área
de conversa nem accent pessoal. A identidade visual fica só na marca do produto
(ou, depois, no white-label do tenant em B-140) — sem delight individual.

## Escopo

- Preferências **pessoais** (por `tenant` + `user`), não por canal:
  - **Wallpaper da conversa** — catálogo curado (assets oficiais + variantes
    light/dark alinhadas ao design system). Aplicar na superfície da timeline/
    chat, com fallback aos fundos padrão (`light_chat` / `dark_chat`).
  - **Cor accent pessoal** — escolha em paleta fechada de tokens seguros
    (contraste AA no tema ativo). Afeta destaques locais (ex. bolha própria,
    focus ring secundário) sem quebrar brand do shell/login.
- UI em Configurações / Meu perfil: preview, reset ao padrão do produto.
- Persistência no perfil do usuário (`GET/PUT /me` ou endpoint dedicado);
  aplicar no cliente via CSS variables / `data-*` sem CSS arbitrário.
- Respeitar `data-theme` e `data-density`; wallpaper tem variante light e dark
  ou filtro/overlay que mantém legibilidade.

## Fora de escopo

- Branding / white-label **por tenant** (B-140) — logos e brand da organização.
- CSS/JS/HTML arbitrário; upload livre de imagem como wallpaper nesta fatia
  (malware/scan → follow-up se houver demanda).
- Fonte tipográfica custom, temas completos de terceiros, skins por canal.
- Alterar tokens globais do design system para todos os usuários.
- Status (B-116), DND (B-097), locale (B-100), perfil público (B-167) — só
  reutilizar a superfície de “preferências” se já existir.

## Contratos

Preferências no perfil do usuário (tenant-aware, RLS):

| Campo | Tipo | Notas |
|-------|------|-------|
| `chatWallpaperId` | string? | id do catálogo; `null` = padrão |
| `accentColorId` | string? | id da paleta; `null` = brand padrão |

Catálogo versionado no web (e espelhado na API se validar server-side). IDs
desconhecidos → fallback ao padrão. Atualizar `contratos.md` e glossário na PR.

## UX

- Preview ao vivo antes de salvar; botão “Restaurar padrão”.
- Wallpaper nunca reduz contraste do texto da timeline abaixo de AA.
- Accent inválido/reprovado por contraste não salva.
- Preferência é só do próprio usuário: outros não veem o wallpaper/accent dele
  na própria sessão (exceto se no futuro houver “tema compartilhado” — fora).

## Multi-tenant e authZ

Só o dono lê/escreve as próprias preferências. Admin não altera accent/wallpaper
de outro usuário. Cross-tenant → 403. Sem impacto em authZ de canal.

## Aceite

- [ ] Usuário escolhe wallpaper do catálogo e vê na área de conversa (light/dark)
- [ ] Usuário escolhe accent da paleta; destaques locais respeitam AA
- [ ] Reset restaura fundos/cores padrão do produto
- [ ] Preferência persiste entre sessões no mesmo tenant
- [ ] IDs inválidos fazem fallback; sem CSS arbitrário
- [ ] Distinto de B-140 (tenant) e de light/dark global (B-049)

## Testes

- Unit: aplicar/reset wallpaper e accent; fallback de id inválido
- Visual light/dark × wallpaper × accent (contraste)
- API: GET/PUT próprio; PUT de outro usuário → 403; cross-tenant → 403

## Riscos

Accent que quebra contraste ou identity da marca — paleta fechada + validação.
Wallpaper ocupando banda — só assets estáticos do catálogo nesta fatia.
