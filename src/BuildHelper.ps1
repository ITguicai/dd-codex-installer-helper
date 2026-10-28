param([string]$OutputPath = (Join-Path $PSScriptRoot 'DDCodexInstallerHelper.exe'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4 compiler is unavailable.' }
$resource = Join-Path $PSScriptRoot 'offline-chinese.cjs'
& $compiler /nologo /target:winexe /optimize+ /platform:anycpu "/out:$OutputPath" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.Net.Http.dll /r:Microsoft.CSharp.dll /r:System.Web.Extensions.dll `
    "/resource:$resource,DDCodexInstallerHelper.offline-chinese.cjs" `
    "/resource:$(Join-Path $PSScriptRoot 'desktop-startup.ps1'),DDCodexInstallerHelper.desktop-startup.ps1" `
    "/resource:$(Join-Path $PSScriptRoot 'organization-check.ps1'),DDCodexInstallerHelper.organization-check.ps1" `
    (Join-Path $PSScriptRoot 'DDCodexInstallerHelper.cs') `
    (Join-Path $PSScriptRoot 'OfflineChineseSupport.cs') `
    (Join-Path $PSScriptRoot 'DesktopShortcutSupport.cs') `
    (Join-Path $PSScriptRoot 'ComputerUseSupport.cs') `
    (Join-Path $PSScriptRoot 'DesktopProfileSupport.cs') `
    (Join-Path $PSScriptRoot 'ChineseMaintenanceSupport.cs') `
    (Join-Path $PSScriptRoot 'PublicFileSupport.cs') `
    (Join-Path $PSScriptRoot 'DesktopLaunchSupport.cs') `
    (Join-Path $PSScriptRoot 'DesktopRepairSupport.cs') `
    (Join-Path $PSScriptRoot 'OrganizationCheckSupport.cs') `
    (Join-Path $PSScriptRoot 'DiagnosticReportForm.cs') `
    (Join-Path $PSScriptRoot 'OrganizationProbeSupport.cs')
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
Write-Output "Built: $OutputPath"
