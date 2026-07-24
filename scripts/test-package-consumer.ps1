[CmdletBinding()]
param(
    [string] $PackageDirectory = "artifacts/packages",
    [string] $ExpectedVersion
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$resolvedPackageDirectory = (Resolve-Path (Join-Path $root $PackageDirectory)).Path

if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    $ExpectedVersion = (& dotnet nbgv get-version -v NuGetPackageVersion).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($ExpectedVersion)) {
        throw "Could not resolve the expected package version with NBGV."
    }
}

$temporaryDirectory = Join-Path `
    ([IO.Path]::GetTempPath()) `
    "nexuslabs-eve-consumer-$([Guid]::NewGuid().ToString('N'))"

try {
    & dotnet new console `
        --framework net10.0 `
        --output $temporaryDirectory `
        --no-restore | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create the package consumer project."
    }

    & dotnet add `
        (Join-Path $temporaryDirectory "$([IO.Path]::GetFileName($temporaryDirectory)).csproj") `
        package NexusLabs.Eve `
        --version $ExpectedVersion `
        --source $resolvedPackageDirectory `
        --no-restore | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not add NexusLabs.Eve to the package consumer."
    }

    @'
using NexusLabs.Eve;

using HttpClient transport = new();
EveClient client = new(transport, new EveClientOptions("http://127.0.0.1:43125"));
EveSession session = client.CreateSession();

Console.WriteLine($"{EveProtocol.ReferenceEveVersion}:{session.State.StreamIndex}");
'@ | Set-Content `
        (Join-Path $temporaryDirectory "Program.cs") `
        -Encoding utf8

    & dotnet restore `
        $temporaryDirectory `
        --source $resolvedPackageDirectory | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "The package consumer restore failed."
    }

    & dotnet build `
        $temporaryDirectory `
        --configuration Release `
        --no-restore `
        --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "The package consumer build failed."
    }
}
finally {
    Remove-Item $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Built a clean consumer against NexusLabs.Eve $ExpectedVersion."
