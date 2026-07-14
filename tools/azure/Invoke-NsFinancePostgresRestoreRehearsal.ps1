#requires -Version 7.2

[CmdletBinding()]
param(
    [ValidateSet("Plan", "Start", "Status", "Delete")]
    [string]$Action = "Plan",

    [string]$TargetServerName,
    [string]$RestoreTimeUtc,
    [string]$Confirmation,
    [switch]$AllowMutation,

    [ValidateRange(10, 300)]
    [int]$CommandTimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$expectedSubscriptionName = "NSIreland-Production"
$resourceGroup = "rg-nsfinance-prod"
$sourceServerName = "psql-nsfinance-prod"
$restoreNamePrefix = "psql-nsfinance-restore-"
$postgresResourceType = "Microsoft.DBforPostgreSQL/flexibleServers"

function Resolve-AzRunner {
    $azCmd = Get-Command az.cmd -ErrorAction SilentlyContinue
    if ($azCmd) {
        $pythonPath = [System.IO.Path]::GetFullPath(
            (Join-Path (Split-Path $azCmd.Source -Parent) "..\python.exe"))
        if (Test-Path -LiteralPath $pythonPath) {
            return [pscustomobject]@{
                FilePath = $pythonPath
                Prefix = @("-IBm", "azure.cli")
                IsMsi = $true
            }
        }
    }

    $az = Get-Command az -ErrorAction Stop
    if ($az.Source.EndsWith(".cmd", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Azure CLI was found only as a command script and its Python runtime could not be resolved."
    }

    return [pscustomobject]@{
        FilePath = $az.Source
        Prefix = @()
        IsMsi = $false
    }
}

$azRunner = Resolve-AzRunner

function Invoke-AzCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [switch]$Json
    )

    $allArguments = @($azRunner.Prefix) + $Arguments + @("--only-show-errors")
    if ($Json) {
        $allArguments += @("--output", "json")
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $azRunner.FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $allArguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }
    if ($azRunner.IsMsi) {
        $startInfo.Environment["AZ_INSTALLER"] = "MSI"
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    if (-not $process.WaitForExit($CommandTimeoutSeconds * 1000)) {
        try {
            $process.Kill($true)
        } catch {
            try { $process.Kill() } catch { }
        }
        [void]$process.WaitForExit(5000)

        $operation = ($Arguments | Select-Object -First 3) -join " "
        throw "Azure CLI operation '$operation' exceeded the $CommandTimeoutSeconds-second safety timeout and was terminated."
    }

    $stdout = $stdoutTask.GetAwaiter().GetResult()
    [void]$stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) {
        $operation = ($Arguments | Select-Object -First 3) -join " "
        throw "Azure CLI operation '$operation' failed with exit code $($process.ExitCode)."
    }

    if ($Json) {
        if ([string]::IsNullOrWhiteSpace($stdout)) {
            return $null
        }

        return $stdout | ConvertFrom-Json -Depth 30
    }

    return $stdout.Trim()
}

function Assert-Subscription {
    $subscription = Invoke-AzCommand -Arguments @(
        "account", "show", "--query", "{name:name,state:state}"
    ) -Json
    if (
        -not [string]::Equals(
            [string]$subscription.name,
            $expectedSubscriptionName,
            [StringComparison]::Ordinal) -or
        -not [string]::Equals(
            [string]$subscription.state,
            "Enabled",
            [StringComparison]::OrdinalIgnoreCase)
    ) {
        throw "Azure CLI is not using the enabled '$expectedSubscriptionName' subscription."
    }

    return [string]$subscription.name
}

function Assert-TargetName {
    param([Parameter(Mandatory = $true)][string]$Name)

    if (
        $Name.Length -gt 63 -or
        $Name -notmatch "^psql-nsfinance-restore-[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$" -or
        [string]::Equals($Name, $sourceServerName, [StringComparison]::OrdinalIgnoreCase)
    ) {
        throw "Restore target names must be distinct and match '$restoreNamePrefix<suffix>'."
    }
}

function Get-PostgresResources {
    $resources = Invoke-AzCommand -Arguments @(
        "resource", "list",
        "--resource-group", $resourceGroup,
        "--resource-type", $postgresResourceType
    ) -Json
    return @($resources)
}

function Test-TargetExists {
    param([Parameter(Mandatory = $true)][string]$Name)

    return [bool](Get-PostgresResources | Where-Object {
        [string]::Equals($_.name, $Name, [StringComparison]::OrdinalIgnoreCase)
    })
}

function Resolve-RestoreTime {
    param(
        [Parameter(Mandatory = $true)]$Source,
        [string]$RequestedRestoreTimeUtc
    )

    $restoreTime = if ([string]::IsNullOrWhiteSpace($RequestedRestoreTimeUtc)) {
        [DateTimeOffset]::UtcNow.AddMinutes(-10)
    } else {
        $parsed = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse(
                $RequestedRestoreTimeUtc,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::AssumeUniversal,
                [ref]$parsed)) {
            throw "RestoreTimeUtc must be a valid ISO-8601 timestamp."
        }
        $parsed.ToUniversalTime()
    }

    $earliestRestore = [DateTimeOffset]::Parse(
        [string]$Source.backup.earliestRestoreDate,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal)
    $latestSafeRestore = [DateTimeOffset]::UtcNow.AddMinutes(-5)
    if ($restoreTime -lt $earliestRestore -or $restoreTime -gt $latestSafeRestore) {
        throw "RestoreTimeUtc must be between the earliest retained point and five minutes before now."
    }

    return $restoreTime.ToString(
        "yyyy-MM-ddTHH:mm:ss'Z'",
        [Globalization.CultureInfo]::InvariantCulture)
}

function Get-RehearsalPlan {
    $subscriptionName = Assert-Subscription
    $source = Invoke-AzCommand -Arguments @(
        "postgres", "flexible-server", "show",
        "--resource-group", $resourceGroup,
        "--name", $sourceServerName
    ) -Json
    if (-not [string]::Equals($source.state, "Ready", [StringComparison]::OrdinalIgnoreCase)) {
        throw "The source PostgreSQL server is not Ready."
    }

    $backups = @(Invoke-AzCommand -Arguments @(
        "postgres", "flexible-server", "backup", "list",
        "--resource-group", $resourceGroup,
        "--server-name", $sourceServerName
    ) -Json)
    if ($backups.Count -eq 0) {
        throw "Azure returned no retained backup records for the source server."
    }

    $target = if ([string]::IsNullOrWhiteSpace($TargetServerName)) {
        "$restoreNamePrefix$([DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss'))"
    } else {
        $TargetServerName.Trim().ToLowerInvariant()
    }
    Assert-TargetName -Name $target

    $resolvedRestoreTimeUtc = Resolve-RestoreTime `
        -Source $source `
        -RequestedRestoreTimeUtc $RestoreTimeUtc
    $latestBackup = $backups |
        Sort-Object { [DateTimeOffset]$_.completedTime } -Descending |
        Select-Object -First 1
    $targetExists = Test-TargetExists -Name $target

    return [pscustomobject][ordered]@{
        Action = "Plan"
        MutatesAzure = $false
        SubscriptionName = $subscriptionName
        ResourceGroup = $resourceGroup
        SourceServerName = $sourceServerName
        SourceState = [string]$source.state
        SourceVersion = [string]$source.version
        SourceSku = [string]$source.sku.name
        SourceStorageGb = [int]$source.storage.storageSizeGb
        BackupRetentionDays = [int]$source.backup.backupRetentionDays
        GeoRedundantBackup = [string]$source.backup.geoRedundantBackup
        EarliestRestoreUtc = ([DateTimeOffset]$source.backup.earliestRestoreDate).ToUniversalTime().ToString("O")
        RetainedBackupCount = $backups.Count
        LatestFullBackupUtc = ([DateTimeOffset]$latestBackup.completedTime).ToUniversalTime().ToString("O")
        RestoreTimeUtc = $resolvedRestoreTimeUtc
        TargetServerName = $target
        TargetExists = $targetExists
        StartConfirmation = "RESTORE $sourceServerName TO $target AT $resolvedRestoreTimeUtc"
        BillingBoundary = "The restored B1ms/32-GB server is billable until deleted; stopping does not remove storage charges."
    }
}

switch ($Action) {
    "Plan" {
        Get-RehearsalPlan | ConvertTo-Json -Depth 5
        break
    }

    "Start" {
        if (-not $AllowMutation) {
            throw "Start requires -AllowMutation in addition to the exact Plan confirmation token."
        }
        if (
            [string]::IsNullOrWhiteSpace($TargetServerName) -or
            [string]::IsNullOrWhiteSpace($RestoreTimeUtc)
        ) {
            throw "Start requires the exact TargetServerName and RestoreTimeUtc emitted by Plan."
        }

        $plan = Get-RehearsalPlan
        if ($plan.TargetExists) {
            throw "The restore target already exists; choose a new Plan target or inspect it with Status."
        }
        if (-not [string]::Equals(
                $Confirmation,
                $plan.StartConfirmation,
                [StringComparison]::Ordinal)) {
            throw "Start requires the exact confirmation token emitted by Plan."
        }

        [void](Invoke-AzCommand -Arguments @(
            "postgres", "flexible-server", "restore",
            "--resource-group", $resourceGroup,
            "--name", $plan.TargetServerName,
            "--source-server", $sourceServerName,
            "--restore-time", $plan.RestoreTimeUtc,
            "--no-wait",
            "--yes",
            "--output", "none"
        ))

        [pscustomobject][ordered]@{
            Action = "Start"
            MutatesAzure = $true
            RestoreRequested = $true
            SourceServerName = $sourceServerName
            TargetServerName = $plan.TargetServerName
            RestoreTimeUtc = $plan.RestoreTimeUtc
            NextAction = "Run Status until the target is Ready; do not change App Service, DNS, or production firewall settings."
        } | ConvertTo-Json -Depth 4
        break
    }

    "Status" {
        if ([string]::IsNullOrWhiteSpace($TargetServerName)) {
            throw "Status requires TargetServerName."
        }
        $targetName = $TargetServerName.Trim().ToLowerInvariant()
        Assert-TargetName -Name $targetName
        [void](Assert-Subscription)
        if (-not (Test-TargetExists -Name $targetName)) {
            [pscustomobject][ordered]@{
                Action = "Status"
                MutatesAzure = $false
                TargetServerName = $targetName
                Exists = $false
            } | ConvertTo-Json
            break
        }

        $target = Invoke-AzCommand -Arguments @(
            "postgres", "flexible-server", "show",
            "--resource-group", $resourceGroup,
            "--name", $targetName
        ) -Json
        [pscustomobject][ordered]@{
            Action = "Status"
            MutatesAzure = $false
            TargetServerName = $targetName
            Exists = $true
            State = [string]$target.state
            Version = [string]$target.version
            Location = [string]$target.location
            Sku = [string]$target.sku.name
            StorageGb = [int]$target.storage.storageSizeGb
            AuditHost = "$targetName.postgres.database.azure.com"
            DeleteConfirmation = "DELETE RESTORE SERVER $targetName"
        } | ConvertTo-Json -Depth 4
        break
    }

    "Delete" {
        if (-not $AllowMutation) {
            throw "Delete requires -AllowMutation in addition to the exact Status confirmation token."
        }
        if ([string]::IsNullOrWhiteSpace($TargetServerName)) {
            throw "Delete requires TargetServerName."
        }
        $targetName = $TargetServerName.Trim().ToLowerInvariant()
        Assert-TargetName -Name $targetName
        [void](Assert-Subscription)
        $deleteConfirmation = "DELETE RESTORE SERVER $targetName"
        if (-not [string]::Equals(
                $Confirmation,
                $deleteConfirmation,
                [StringComparison]::Ordinal)) {
            throw "Delete requires the exact confirmation token emitted by Status."
        }

        if (-not (Test-TargetExists -Name $targetName)) {
            [pscustomobject][ordered]@{
                Action = "Delete"
                MutatesAzure = $false
                TargetServerName = $targetName
                AlreadyAbsent = $true
            } | ConvertTo-Json
            break
        }

        [void](Invoke-AzCommand -Arguments @(
            "postgres", "flexible-server", "delete",
            "--resource-group", $resourceGroup,
            "--name", $targetName,
            "--yes",
            "--output", "none"
        ))
        [pscustomobject][ordered]@{
            Action = "Delete"
            MutatesAzure = $true
            TargetServerName = $targetName
            DeletionRequested = $true
            NextAction = "Run Status until Exists is false, then close the evidence record."
        } | ConvertTo-Json
        break
    }
}
