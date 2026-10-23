# Run in a disposable Windows account, as in GitHub Actions.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    [Parameter(Mandatory = $true)][string]$LegacyPortablePath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$InstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
$LegacyPortablePath = (Resolve-Path -LiteralPath $LegacyPortablePath).Path
$testDir = Join-Path $env:LOCALAPPDATA 'Programs\MouseWithOutBorders-InstallerCI'
$app = Join-Path $testDir 'MouseWithOutBorders.exe'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'MWB Installer CI.lnk'
$logDir = Join-Path $PSScriptRoot '..\dist\installer-tests'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$logDir = (Resolve-Path -LiteralPath $logDir).Path

# Do not overwrite an existing user's app registration or settings during tests.
$registration = Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{BAA10551-2B65-4E83-B102-1F184AE6C673}_is1' -ErrorAction SilentlyContinue
if ($registration -and $registration.InstallLocation.TrimEnd('\') -ine $testDir.TrimEnd('\')) {
    throw 'Use a disposable Windows account: MWB is already installed elsewhere.'
}
if (Get-Process -Name MouseWithOutBorders -ErrorAction SilentlyContinue) {
    throw 'Use a disposable Windows account: MWB is already running.'
}
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class MwbInstallerSmokeNative {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
'@

function Invoke-Setup([string]$CaseName, [switch]$AppUpdate) {
    Write-Host "Starting installer case: $CaseName"
    $log = Join-Path $logDir ($CaseName + '.log')
    $setupArgs = @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-',
        ('/DIR="' + $testDir + '"'), ('/LOG="' + $log + '"')
    )
    if ($AppUpdate) { $setupArgs += '/MWBUPDATE=1' }
    $setup = Start-Process -FilePath $InstallerPath -ArgumentList $setupArgs -WindowStyle Hidden -PassThru
    if (-not $setup.WaitForExit(45000)) {
        $setup.Kill()
        throw "Setup stalled during $CaseName."
    }
    if ($setup.ExitCode -ne 0) { throw "Setup exited $($setup.ExitCode) during $CaseName. See $log" }
    if (-not (Test-Path -LiteralPath $app)) { throw 'Installed EXE not found.' }
    return $log
}

function Start-TestApp([switch]$Tray) {
    $process = Start-Process -FilePath $app -WorkingDirectory $testDir -PassThru
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
        if ($process.HasExited) { throw 'The test app exited before setup.' }
        if ($process.MainWindowHandle -ne 0) { break }
    }
    if ($process.MainWindowHandle -eq 0) { throw 'The test app did not open its window.' }
    if ($Tray) {
        # Reproduce the window's Close command. CloseMainWindow sends WM_CLOSE,
        # which WinForms can interpret as a task-manager exit instead of a click.
        if (-not [MwbInstallerSmokeNative]::PostMessage($process.MainWindowHandle, 0x0112, [IntPtr]0xF060, [IntPtr]::Zero)) {
            throw 'Could not close the test app to the tray.'
        }
        for ($attempt = 0; $attempt -lt 50; $attempt++) {
            Start-Sleep -Milliseconds 100
            $process.Refresh()
            if ($process.HasExited) { throw 'Closing the test window quit the app instead of hiding it.' }
            if ($process.MainWindowHandle -eq 0) { break }
        }
        if ($process.MainWindowHandle -ne 0) { throw 'The test app did not hide in the tray.' }
        Start-Sleep -Milliseconds 250
        if ($process.HasExited) { throw 'The test app exited after closing its window.' }
    }
    return $process
}

try {
    [void](Invoke-Setup 'fresh-install')
    $installedVersion = [Version](Get-Item -LiteralPath $app).VersionInfo.FileVersion
    $expectedIconPath = Join-Path $testDir ('app-' + $installedVersion.ToString(3) + '.ico')
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $app
    $shortcut.IconLocation = $app + ',0'
    $shortcut.Save()

    foreach ($mode in @('window', 'tray', 'legacy-tray')) {
        if ($mode -eq 'legacy-tray') {
            $legacyDir = Join-Path $logDir 'legacy'
            Expand-Archive -LiteralPath $LegacyPortablePath -DestinationPath $legacyDir -Force
            Copy-Item -LiteralPath (Join-Path $legacyDir 'MouseWithOutBorders.exe') -Destination $app -Force
        }
        $runningApp = Start-TestApp -Tray:($mode -ne 'window')
        $log = Invoke-Setup ('upgrade-' + $mode)
        $shortcut = $shell.CreateShortcut($shortcutPath)
        if ($shortcut.IconLocation.Replace('"', '') -ine ($expectedIconPath + ',0') -or
            -not (Test-Path -LiteralPath $expectedIconPath)) {
            throw 'Setup did not migrate the existing desktop shortcut to its versioned icon.'
        }
        if (-not $runningApp.WaitForExit(5000)) { throw "Setup left the $mode app running." }
        $logText = Get-Content -LiteralPath $log -Raw
        if (-not $logText.Contains('Requesting MWB exit before replacing its files.')) {
            throw "Setup did not request an exit during $mode."
        }
        $forced = $logText.Contains('Closing an older or unresponsive MWB process.')
        if ($forced -ne ($mode -eq 'legacy-tray')) {
            throw "Unexpected shutdown method during $mode."
        }
        Write-Output "PASS: automatic upgrade while MWB is running ($mode)."
    }

    $updatingApp = Start-TestApp -Tray
    [void](Invoke-Setup 'in-app-update' -AppUpdate)
    if (-not $updatingApp.WaitForExit(5000)) { throw 'In-app update left the old app running.' }
    $restartedApp = $null
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        $restartedApp = Get-Process -Name MouseWithOutBorders -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -ieq $app -and $_.Id -ne $updatingApp.Id } | Select-Object -First 1
        if ($restartedApp -and $restartedApp.MainWindowHandle -ne 0) { break }
        Start-Sleep -Milliseconds 100
    }
    if (-not $restartedApp -or $restartedApp.MainWindowHandle -eq 0) {
        throw 'In-app update did not reopen the updated application.'
    }
    if (-not [MwbInstallerSmokeNative]::PostMessage($restartedApp.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)) {
        throw 'Could not close the restarted test application.'
    }
    if (-not $restartedApp.WaitForExit(5000)) { throw 'The restarted test application did not exit.' }
    Write-Output 'PASS: in-app update closes the tray app and reopens the installed executable.'

    $runningApp = Start-TestApp -Tray
    $uninstaller = Join-Path $testDir 'unins000.exe'
    $uninstall = Start-Process -FilePath $uninstaller -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'
    ) -WindowStyle Hidden -PassThru
    if (-not $uninstall.WaitForExit(45000)) {
        $uninstall.Kill()
        throw 'Uninstall stalled while MWB was running.'
    }
    if ($uninstall.ExitCode -ne 0) { throw "Uninstall exited $($uninstall.ExitCode)." }
    if (-not $runningApp.WaitForExit(5000)) { throw 'Uninstall left MWB running.' }
    if (Test-Path -LiteralPath $app) { throw 'EXE still present after uninstall.' }
    Write-Output 'PASS: uninstall while MWB is running in the tray.'
} finally {
    Remove-Item -LiteralPath $shortcutPath -Force -ErrorAction SilentlyContinue
    Get-Process -Name MouseWithOutBorders -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -ieq $app } | Stop-Process -Force
}
