$ErrorActionPreference = 'Stop'

$expectedPlugins = @('emby', 'jellyfin', 'plex', 'localai', 'quickconnect')
$expectedProviders = @('huggingface', 'theporndb', 'stashdb', 'extractor-metadata', 'extractor-thumbnail')

function Test-ConfigFile {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $ExpectedId
    )

    $config = Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
    if ($config.id -ne $ExpectedId) { throw "$Path has id '$($config.id)' instead of '$ExpectedId'." }
    if ([int]$config.schemaVersion -lt 2) { throw "$Path has an outdated schemaVersion." }
    if ([int]$config.version -lt 2) { throw "$Path has an outdated version." }
    if ([string]::IsNullOrWhiteSpace([string]$config.name)) { throw "$Path is missing a name." }
    if ($null -eq $config.settings) { throw "$Path is missing settings." }

    foreach ($property in $config.settings.PSObject.Properties) {
        if ($property.Name -match '(?i)(apiKey|token|password|secret)$' -and
            $property.Name -notmatch '(?i)(EnvironmentVariable|SecureStorageKey)$' -and
            -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            throw "$Path contains a plaintext credential in settings.$($property.Name)."
        }
        if ($property.Name -match '(?i)(endpoint|Url)$' -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            $uri = $null
            if (-not [Uri]::TryCreate([string]$property.Value, [UriKind]::Absolute, [ref]$uri) -or
                $uri.Scheme -notin @('http', 'https')) {
                throw "$Path contains an invalid HTTP/HTTPS URL in settings.$($property.Name)."
            }
        }
    }
}

foreach ($id in $expectedPlugins) {
    Test-ConfigFile -Path (Join-Path $PSScriptRoot "../ConfigDefaults/plugins/$id.json") -ExpectedId $id
}
foreach ($id in $expectedProviders) {
    Test-ConfigFile -Path (Join-Path $PSScriptRoot "../ConfigDefaults/providers/$id.json") -ExpectedId $id
}

Write-Host "Verified $($expectedProviders.Count) providers and $($expectedPlugins.Count) persistent add-ins."
