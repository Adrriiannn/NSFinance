[CmdletBinding()]
param(
    [string]$ApplicationId = "c6469beb-92b8-4927-805a-01f685431449",
    [string]$ScopeId = "2a785c9d-1c14-4557-91ee-85c707920c1c",
    [string]$ResourceGroup = "rg-nsfinance-prod",
    [string]$AppServiceName = "nsfinance-api"
)

$ErrorActionPreference = "Stop"

function Invoke-AzureCliJson {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI failed: az $($Arguments -join ' ')"
    }

    return $output | ConvertFrom-Json
}

$application = Invoke-AzureCliJson -Arguments @(
    "ad", "app", "show",
    "--id", $ApplicationId,
    "--output", "json"
)

$scope = @{
    adminConsentDescription = "Allow the NSFinance Android app to sign the current user into the NSFinance API."
    adminConsentDisplayName = "Sign in to NSFinance"
    id = $ScopeId
    isEnabled = $true
    type = "User"
    userConsentDescription = "Allow NSFinance to sign you in and access your NSFinance account."
    userConsentDisplayName = "Sign in to NSFinance"
    value = "access_as_user"
}

$patchBody = @{
    identifierUris = @("api://$ApplicationId")
    api = @{
        requestedAccessTokenVersion = 2
        oauth2PermissionScopes = @($scope)
    }
} | ConvertTo-Json -Depth 8

$requestBodyPath = Join-Path ([System.IO.Path]::GetTempPath()) "nsfinance-microsoft-app-$([Guid]::NewGuid().ToString('N')).json"
try {
    [System.IO.File]::WriteAllText($requestBodyPath, $patchBody, [System.Text.UTF8Encoding]::new($false))
    & az rest `
        --method PATCH `
        --url "https://graph.microsoft.com/v1.0/applications/$($application.id)" `
        --headers "Content-Type=application/json" `
        --body "@$requestBodyPath" `
        --output none
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to expose the NSFinance Microsoft delegated API scope."
    }
} finally {
    Remove-Item -LiteralPath $requestBodyPath -Force -ErrorAction SilentlyContinue
}

$verifiedApplication = Invoke-AzureCliJson -Arguments @(
    "ad", "app", "show",
    "--id", $ApplicationId,
    "--query", "{identifierUris:identifierUris,scopes:api.oauth2PermissionScopes,redirects:publicClient.redirectUris}",
    "--output", "json"
)

$verifiedScope = @($verifiedApplication.scopes) | Where-Object {
    $_.value -eq "access_as_user" -and $_.isEnabled -eq $true
}
if ($verifiedScope.Count -ne 1) {
    throw "The NSFinance delegated API scope could not be verified."
}

if (@($verifiedApplication.redirects).Count -eq 0) {
    throw "The production Android redirect was lost while updating the registration."
}

& az webapp config appsettings set `
    --resource-group $ResourceGroup `
    --name $AppServiceName `
    --settings "NSFINANCE_MICROSOFT_CLIENT_ID=$ApplicationId" `
    --output none
if ($LASTEXITCODE -ne 0) {
    throw "Failed to configure the NSFinance API Microsoft client ID."
}

$health = Invoke-RestMethod -Uri "https://api.finance.nsireland.ie/health" -TimeoutSec 30
if ($health.status -ne "Healthy") {
    throw "The production API health check did not return Healthy."
}

[pscustomobject]@{
    Application = "NSFinance Mobile"
    DelegatedScope = "api://$ApplicationId/access_as_user"
    AndroidRedirectCount = @($verifiedApplication.redirects).Count
    ApiConfiguration = "Configured"
    ProductionHealth = $health.status
}
