# ADR-024: Guest é ChannelMember sem WorkspaceMember

## Status: Accepted

## Contexto

W10-10 / B-040 pede trazer alguém de fora para **um canal**, sem membership de
workspace (D-07 revisado). A spec é R3: flag off por default, ADR, threat model
e rollback no mesmo PR. `Role.Guest` já existia no catálogo, mas estava fora do
fluxo de membership.

## Decisão

1. **Não gravar `Role.Guest` em `tenancy.workspace_members`.** Guest é
   `conversations.channel_members` sem linha de workspace.
2. **Convite em `directory.channel_invites`.** Token de uso único, persistido só
   como SHA-256; o valor cru sai uma vez no `url`.
3. **`PermissionChecker.GetRolesAsync`** devolve `[Guest]` quando não há
   workspace membership e existe `ChannelMember` ativo no tenant.
4. **`ResolveWorkspaceAsync` permanece fechado.** Listagem de canais e unread
   usam `ResolveWorkspaceOrGuestAsync`. Demais rotas `/workspaces/{id}`
   continuam 403 para guest.
5. **Kill switch** `Directory:Invites:Enabled` default `false`. Lab/Development
   e TestHost ligam a flag.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| `WorkspaceMember` com `Role.Guest` | Viola “nunca membership de workspace” |
| Coluna `Role` em `ChannelMember` | Guest é o único papel de canal; ausência de workspace basta |
| Aceite anônimo | Spec exige identidade real no IdP |

## Rollback

1. `Directory:Invites:Enabled=false` — create/list/revoke/accept → 404.
   Memberships já aceitas continuam até revogação manual.
2. Reverter a migration só em lab; em dados reais preferir flag off + revoke.

## Consequências

- **+** Mesmo messaging, RLS e hub do canal convidado
- **+** Suíte negativa cobre as superfícies de workspace
- **−** Guest não abre DM nem usa busca global (mais restrito que “mesmo canal”)
