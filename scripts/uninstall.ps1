# Uninstall the app on THIS Windows PC. Run in PowerShell.
# By default personal pairing settings are retained for a later reinstall.
[CmdletBinding()]
param([switch]$PurgeSettings)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$appDir = Join-Path $env:LOCALAPPDATA 'Programs\MouseWithOutBorders'
$uninstaller = Join-Path $appDir 'unins000.exe'
if (Test-Path -LiteralPath $uninstaller) {
    Write-Host 'Uninstalling Mouse, with out borders...'
    $result = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait -PassThru
    if ($result.ExitCode -ne 0) { throw "Uninstall failed (exit $($result.ExitCode))." }
} else {
    Write-Warning 'No installation found at the default location.'
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $runKey -Name 'MouseWithOutBorders' -ErrorAction SilentlyContinue

$rule = 'MouseWithOutBorders Private LAN Receiver'
if (Get-Command Get-NetFirewallRule -ErrorAction SilentlyContinue) {
    $existing = Get-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host 'Removing the private-network firewall rule requires Windows administrator approval (UAC).'
        try {
            $result = Start-Process -FilePath 'netsh.exe' -ArgumentList @(
                'advfirewall', 'firewall', 'delete', 'rule',
                'name="MouseWithOutBorders Private LAN Receiver"'
            ) -Verb RunAs -Wait -PassThru
            if ($result.ExitCode -ne 0) { Write-Warning 'Firewall rule could not be removed.' }
        } catch {
            Write-Warning "Firewall removal was declined or failed: $($_.Exception.Message)"
        }
    }
}

if ($PurgeSettings) {
    $data = Join-Path $env:LOCALAPPDATA 'MouseWithOutBorders'
    Remove-Item -LiteralPath $data -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'Personal settings and diagnostics deleted.'
} else {
    Write-Host 'Settings retained for future reinstall. Use -PurgeSettings to also delete them.'
}
Write-Host 'Uninstall routine finished.'
