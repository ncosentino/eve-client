[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [switch] $PreserveRootIndex
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
}
else {
    Join-Path $root $OutputDirectory
}
$assemblyDirectory = Join-Path $root "src/NexusLabs.Eve/bin/Release/net10.0"
$assemblyPath = Join-Path $assemblyDirectory "NexusLabs.Eve.dll"
$documentationPath = Join-Path $assemblyDirectory "NexusLabs.Eve.xml"
$packageOutput = Join-Path $resolvedOutput "NexusLabs.Eve"

if (-not (Test-Path $assemblyPath) -or -not (Test-Path $documentationPath)) {
    throw "Build NexusLabs.Eve in Release configuration before generating API documentation."
}

Remove-Item $packageOutput -Recurse -Force -ErrorAction SilentlyContinue
New-Item $packageOutput -ItemType Directory -Force | Out-Null

& dotnet defaultdocumentation `
    --AssemblyFilePath $assemblyPath `
    --DocumentationFilePath $documentationPath `
    --OutputDirectoryPath $packageOutput `
    --ConfigurationFilePath (Join-Path $root "defaultdocumentation.json")
if ($LASTEXITCODE -ne 0) {
    throw "DefaultDocumentation failed."
}

$indexPath = Join-Path $packageOutput "index.md"
if (-not (Test-Path $indexPath)) {
    $namespacePath = Join-Path $packageOutput "NexusLabs.Eve.md"
    if (Test-Path $namespacePath) {
        Copy-Item $namespacePath $indexPath
    }
    else {
        $files = @(
            Get-ChildItem $packageOutput -Filter "*.md" -File |
                Sort-Object Name
        )
        if ($files.Count -eq 0) {
            throw "No API documentation pages were generated."
        }

        $lines = @("# NexusLabs.Eve", "")
        foreach ($file in $files) {
            $name = [IO.Path]::GetFileNameWithoutExtension($file.Name)
            $lines += "- [$name]($($file.Name))"
        }
        Set-Content $indexPath $lines -Encoding utf8
    }
}

if (-not $PreserveRootIndex) {
    $rootIndex = Join-Path $resolvedOutput "index.md"
    $relativePackageIndex = "NexusLabs.Eve/index.md"
    @(
        "# NexusLabs.Eve API Reference"
        ""
        "[Browse the NexusLabs.Eve namespace and public types]($relativePackageIndex)."
    ) | Set-Content $rootIndex -Encoding utf8
}

Write-Host "Generated API documentation at '$resolvedOutput'."
