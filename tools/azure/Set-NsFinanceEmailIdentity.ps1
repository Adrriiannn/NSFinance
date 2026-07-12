[CmdletBinding()]
param(
    [string]$ResourceGroup = "rg-nsfinance-prod",
    [string]$WebAppName = "nsfinance-api",
    [string]$CommunicationServiceName = "nsfinance-communications",
    [string]$RoleName = "NSFinance ACS Email Sender"
)

$ErrorActionPreference = "Stop"

function Invoke-AzTsv {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = & az @Arguments --only-show-errors -o tsv
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI failed: az $($Arguments -join ' ')"
    }

    return ($output | Out-String).Trim()
}

$subscriptionId = Invoke-AzTsv -Arguments @("account", "show", "--query", "id")
$principalId = Invoke-AzTsv -Arguments @(
    "webapp", "identity", "show",
    "--name", $WebAppName,
    "--resource-group", $ResourceGroup,
    "--query", "principalId"
)
$communicationServiceId = Invoke-AzTsv -Arguments @(
    "resource", "show",
    "--resource-group", $ResourceGroup,
    "--resource-type", "Microsoft.Communication/communicationServices",
    "--name", $CommunicationServiceName,
    "--api-version", "2023-03-31",
    "--query", "id"
)

if (-not $subscriptionId -or -not $principalId -or -not $communicationServiceId) {
    throw "The subscription, API managed identity, or Communication Services resource could not be resolved."
}

$resourceGroupScope = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup"
$definition = [ordered]@{
    Name = $RoleName
    IsCustom = $true
    Description = "Allows the NSFinance API to authenticate to its ACS resource for transactional email without keys or resource deletion."
    Actions = @(
        "Microsoft.Communication/CommunicationServices/Read",
        "Microsoft.Communication/CommunicationServices/Write",
        "Microsoft.Communication/EmailServices/Write"
    )
    NotActions = @()
    DataActions = @()
    NotDataActions = @()
    AssignableScopes = @($resourceGroupScope)
}

$temporaryDefinition = Join-Path ([System.IO.Path]::GetTempPath()) "nsfinance-acs-email-sender-role.json"
try {
    $definition | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $temporaryDefinition -Encoding utf8
    $existingRoleId = Invoke-AzTsv -Arguments @(
        "role", "definition", "list",
        "--name", $RoleName,
        "--query", "[0].name"
    )

    if ($existingRoleId) {
        & az role definition update --role-definition $temporaryDefinition --only-show-errors --output none
    } else {
        & az role definition create --role-definition $temporaryDefinition --only-show-errors --output none
    }

    if ($LASTEXITCODE -ne 0) {
        throw "The NSFinance ACS email sender role could not be created or updated."
    }

    & az role assignment create `
        --assignee-object-id $principalId `
        --assignee-principal-type ServicePrincipal `
        --role $RoleName `
        --scope $communicationServiceId `
        --only-show-errors `
        --output none
    if ($LASTEXITCODE -ne 0) {
        throw "The NSFinance API managed identity could not be assigned the ACS email sender role."
    }

    Write-Output "Configured custom role '$RoleName' for '$WebAppName' on '$CommunicationServiceName'."
} finally {
    Remove-Item -LiteralPath $temporaryDefinition -Force -ErrorAction SilentlyContinue
}
