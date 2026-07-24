[CmdletBinding()]
param(
    [string] $PackageDirectory = "artifacts/packages",
    [string] $ExpectedVersion
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$resolvedPackageDirectory = Join-Path $root $PackageDirectory

if (-not (Test-Path $resolvedPackageDirectory)) {
    throw "Package directory '$resolvedPackageDirectory' does not exist."
}

if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    $ExpectedVersion = (& dotnet nbgv get-version -v NuGetPackageVersion).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($ExpectedVersion)) {
        throw "Could not resolve the expected package version with NBGV."
    }
}

$packages = @(
    Get-ChildItem $resolvedPackageDirectory -Filter "NexusLabs.Eve.*.nupkg" |
        Where-Object { -not $_.Name.EndsWith(".snupkg", [StringComparison]::OrdinalIgnoreCase) }
)
$symbolPackages = @(Get-ChildItem $resolvedPackageDirectory -Filter "NexusLabs.Eve.*.snupkg")

if ($packages.Count -ne 1) {
    throw "Expected exactly one NexusLabs.Eve .nupkg, found $($packages.Count)."
}

if ($symbolPackages.Count -ne 1) {
    throw "Expected exactly one NexusLabs.Eve .snupkg, found $($symbolPackages.Count)."
}

$expectedPackageName = "NexusLabs.Eve.$ExpectedVersion.nupkg"
$expectedSymbolName = "NexusLabs.Eve.$ExpectedVersion.snupkg"
if ($packages[0].Name -cne $expectedPackageName) {
    throw "Expected package '$expectedPackageName', found '$($packages[0].Name)'."
}

if ($symbolPackages[0].Name -cne $expectedSymbolName) {
    throw "Expected symbol package '$expectedSymbolName', found '$($symbolPackages[0].Name)'."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName })
    $requiredEntries = @(
        "README.md",
        "lib/net10.0/NexusLabs.Eve.dll",
        "lib/net10.0/NexusLabs.Eve.xml"
    )

    foreach ($entry in $requiredEntries) {
        if ($entries -cnotcontains $entry) {
            throw "Package '$($packages[0].Name)' is missing '$entry'."
        }
    }

    $nuspecEntry = $archive.Entries |
        Where-Object { $_.FullName.EndsWith(".nuspec", [StringComparison]::OrdinalIgnoreCase) } |
        Select-Object -First 1
    if ($null -eq $nuspecEntry) {
        throw "Package '$($packages[0].Name)' does not contain a nuspec."
    }

    $reader = [IO.StreamReader]::new($nuspecEntry.Open())
    try {
        [xml] $nuspec = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }

    $metadata = $nuspec.package.metadata
    if ($metadata.id -cne "NexusLabs.Eve") {
        throw "Unexpected package ID '$($metadata.id)'."
    }

    if ($metadata.version -cne $ExpectedVersion) {
        throw "Expected nuspec version '$ExpectedVersion', found '$($metadata.version)'."
    }

    if ($metadata.license.type -cne "expression" -or $metadata.license.'#text' -cne "MIT") {
        throw "The package must use the MIT license expression."
    }

    if ($metadata.readme -cne "README.md") {
        throw "The package readme must be README.md."
    }

    if ($metadata.repository.url -cne "https://github.com/ncosentino/eve-client.git") {
        throw "The package repository URL is missing or incorrect."
    }
}
finally {
    $archive.Dispose()
}

if ($packages[0].Length -le 0 -or $symbolPackages[0].Length -le 0) {
    throw "Package artifacts must not be empty."
}

Write-Host "Validated NexusLabs.Eve package version $ExpectedVersion."
