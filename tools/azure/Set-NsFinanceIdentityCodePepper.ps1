[CmdletBinding()]
param(
    [string]$VaultName = "kv-nsfinance-prod",
    [string]$WebAppName = "nsfinance-api",
    [string]$ResourceGroup = "rg-nsfinance-prod",
    [string]$SecretName = "identity-code-pepper"
)

$ErrorActionPreference = "Stop"

$secretId = (& az keyvault secret list `
    --vault-name $VaultName `
    --query "[?name=='$SecretName'].id | [0]" `
    --only-show-errors `
    -o tsv | Out-String).Trim()

if (-not $secretId) {
    $pepperBytes = New-Object byte[] 64
    $randomNumberGenerator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $randomNumberGenerator.GetBytes($pepperBytes)
        $pepper = [Convert]::ToBase64String($pepperBytes)
        & az keyvault secret set `
            --vault-name $VaultName `
            --name $SecretName `
            --value $pepper `
            --only-show-errors `
            --output none
        if ($LASTEXITCODE -ne 0) {
            throw "The identity-code pepper could not be stored in Key Vault."
        }
    } finally {
        $randomNumberGenerator.Dispose()
        [Array]::Clear($pepperBytes, 0, $pepperBytes.Length)
        $pepper = $null
    }
}

$vaultUri = (& az keyvault show `
    --name $VaultName `
    --query properties.vaultUri `
    --only-show-errors `
    -o tsv | Out-String).Trim()
if (-not $vaultUri) {
    throw "The Key Vault URI could not be resolved."
}

$reference = "@Microsoft.KeyVault(SecretUri=$($vaultUri.TrimEnd('/'))/secrets/$SecretName/)"
$temporarySettings = Join-Path ([System.IO.Path]::GetTempPath()) "nsfinance-identity-appsettings.json"
try {
    [ordered]@{
        NSFINANCE_IDENTITY_CODE_PEPPER = $reference
    } | ConvertTo-Json | Set-Content -LiteralPath $temporarySettings -Encoding utf8

    & az webapp config appsettings set `
        --name $WebAppName `
        --resource-group $ResourceGroup `
        --settings "@$temporarySettings" `
        --only-show-errors `
        --output none
    if ($LASTEXITCODE -ne 0) {
        throw "The API Key Vault reference could not be configured."
    }
} finally {
    Remove-Item -LiteralPath $temporarySettings -Force -ErrorAction SilentlyContinue
}

Write-Output "Configured the versionless Key Vault reference for the NSFinance identity-code pepper."
