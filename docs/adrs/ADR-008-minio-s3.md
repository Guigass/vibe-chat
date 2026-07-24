# ADR-008: Armazenamento S3-compatible (MinIO)

## Status: Accepted

## Contexto

Anexos de chat (imagens, docs) não devem viver no PostgreSQL. Objetos precisam de APIs padrão, URLs pré-assinadas e caminho para storage gerenciado em produção (AWS S3, GCS via gateway, etc.).

## Decisão

Usar **MinIO** (API S3-compatible) na fase 1 / self-host:

- Metadados no PostgreSQL; bytes no object storage
- Uploads via **presigned URLs** (cliente → MinIO)
- Interface `IFileStorage` abstrai o SDK — troca MinIO→S3 sem mudar domínio
- Políticas de tamanho, MIME e retenção no módulo Files

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Arquivos no disco local da API | Quebra escala horizontal e backup |
| BLOBs no Postgres | Infla DB e backups |
| Apenas AWS S3 | Amarraria cloud; MinIO cobre air-gapped |
| IPFS / outros | Fora do perfil corporativo |

## Consequências

- **+** Padrão S3 portável; Compose local simples
- **+** API não faz proxy de upload pesado
- **−** Operação MinIO (discos, versionamento, credenciais)
- **−** Antivirus/malware scanning fica como job futuro no worker
