[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,

    [string[]]$ArgumentList = @(),

    [ValidateRange(1, 3600)]
    [int]$TimeoutSeconds = 10,

    [string]$WorkingDirectory = (Get-Location).Path
)

$resolvedCommand = Get-Command -Name $FilePath -ErrorAction Stop
$executablePath = if ($resolvedCommand.Source) {
    $resolvedCommand.Source
} else {
    $resolvedCommand.Path
}

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $executablePath
$startInfo.WorkingDirectory = (Resolve-Path -LiteralPath $WorkingDirectory).Path
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.RedirectStandardInput = $true

foreach ($argument in $ArgumentList) {
    [void]$startInfo.ArgumentList.Add($argument)
}

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo

try {
    if (-not $process.Start()) {
        throw "Failed to start '$FilePath'."
    }

    # Prevent commands from silently waiting for an interactive answer.
    $process.StandardInput.Close()

    $standardOutput = $process.StandardOutput.ReadToEndAsync()
    $standardError = $process.StandardError.ReadToEndAsync()
    $completed = $process.WaitForExit($TimeoutSeconds * 1000)

    if (-not $completed) {
        try {
            $process.Kill($true)
        } catch {
            $process.Kill()
        }

        [void]$process.WaitForExit(5000)
        $timeoutMessage = "Timed out after ${TimeoutSeconds}s: $FilePath $($ArgumentList -join ' ')"
        [Console]::Error.WriteLine($timeoutMessage)
        exit 124
    }

    # A descendant can inherit a redirected pipe even after the parent exits.
    # Never let output draining become an unbounded second wait.
    $output = if ($standardOutput.Wait(3000)) {
        $standardOutput.GetAwaiter().GetResult()
    } else {
        ""
    }
    $errorOutput = if ($standardError.Wait(3000)) {
        $standardError.GetAwaiter().GetResult()
    } else {
        "Output collection stopped after its 3s drain deadline.`n"
    }

    if ($output) {
        [Console]::Out.Write($output)
    }

    if ($errorOutput) {
        [Console]::Error.Write($errorOutput)
    }

    exit $process.ExitCode
} finally {
    $process.Dispose()
}
