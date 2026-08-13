#!/usr/bin/env bash
# Generate a VAPID P-256 key pair for Web Push (B-095). Prints env lines; do not commit.
set -euo pipefail

IMAGE="${DOTNET_SDK_IMAGE:-mcr.microsoft.com/dotnet/sdk:10.0-noble}"

docker run --rm -i "$IMAGE" bash <<'INNER'
set -euo pipefail
WORKDIR=$(mktemp -d)
cat > "$WORKDIR/VapidGen.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
EOF
cat > "$WORKDIR/Program.cs" <<'EOF'
using System.Security.Cryptography;

using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var parameters = ecdsa.ExportParameters(includePrivateParameters: true);
var publicKey = new byte[65];
publicKey[0] = 0x04;
Buffer.BlockCopy(parameters.Q.X!, 0, publicKey, 1, 32);
Buffer.BlockCopy(parameters.Q.Y!, 0, publicKey, 33, 32);
static string ToBase64Url(byte[] bytes) =>
    Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
Console.WriteLine("Push__Enabled=true");
Console.WriteLine("Push__Vapid__PublicKey=" + ToBase64Url(publicKey));
Console.WriteLine("Push__Vapid__PrivateKey=" + ToBase64Url(parameters.D!));
Console.WriteLine("Push__Vapid__Subject=mailto:ops@localhost");
EOF
dotnet run --project "$WORKDIR/VapidGen.csproj" -c Release --nologo -v q
INNER
