# ADR-055: White-label governado por tenant

## Status: Accepted

## Contexto

D-24 permite nome, logos, favicon e tokens de cor por tenant, com contraste
mínimo. CSS, JS e HTML arbitrários ficam de fora. O login público não pode
aceitar um tenant vindo da query, e About/admin precisa continuar identificando
o VibeChat.

## Decisão

1. **Campos fechados.** A linha `directory.brand_settings` guarda nome, duas
   cores `#RRGGBB` e chaves de objeto. Não há coluna de CSS, HTML ou URL externa.
2. **Contraste.** Cada cor precisa de contraste ≥ 3:1 contra as superfícies
   clara (`#f8fafc`) e escura (`#0c0f12`). Fora disso a API responde
   `BrandContrastFailed` e não grava.
3. **Arquivos.** Só PNG e JPEG, com magic byte, scan da política de malware,
   tamanho máximo e encaixe no slot (1–512 px, proporção 1:4–4:1). SVG, HTML e
   script são recusados. O shell desenha o logo com `object-fit: contain` e
   padding, sem cortar a borda.
4. **Host.** O login público resolve o tenant só pelo host em
   `Branding:Hosts`, configurado pelo operador. Query `tenantId` é ignorada.
   Sem mapeamento, a resposta é a marca oficial.
5. **Sessão.** Shell e admin leem o tenant da membership, nunca do body.
   Escrita exige `admin.brand` (Owner/Admin). Auditor e Member recebem 403.
6. **Identidade do produto.** `productName` é sempre `VibeChat`. O admin
   mostra a versão da API. O nome do tenant não substitui essa linha.
7. **Rollback.** Cada gravação bem-sucedida guarda o snapshot anterior. Um
   rollback restaura esse snapshot e esquece o passo. Objetos antigos ficam no
   storage até o lifecycle de B-145; a linha deixa de apontar para eles.
8. **Cache.** `assetVersion` entra na URL (`?v=`) e no `ETag`.

## Consequências

- Sem flag: a ausência de linha já é a marca oficial, e o chat não depende
  desta tabela.
- O `Down` da migration remove só `directory.brand_settings`.
- Phishing entre tenants depende do mapa de hosts do operador, não de um id
  enviado pelo browser.
