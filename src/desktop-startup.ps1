param([ValidateSet('inspect','repair')][string]$Mode='inspect')
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$WarningPreference='SilentlyContinue'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$lines=New-Object 'System.Collections.Generic.List[string]'
function Code-Summary($failure) {
    $codes=New-Object 'System.Collections.Generic.List[string]'
    $codes.Add(('0x{0:X8}' -f ($failure.Exception.HResult -band 0xffffffffL)))
    foreach($match in [regex]::Matches([string]$failure,'0x[0-9a-fA-F]{8}')) {
        $value='0x'+$match.Value.Substring(2).ToUpperInvariant()
        if(!$codes.Contains($value)){$codes.Add($value)}
        if($codes.Count -ge 8){break}
    }
    return ($codes -join ', ')
}
function Main-Application($manifest) {
    return $manifest.SelectNodes("/*[local-name()='Package']/*[local-name()='Applications']/*[local-name()='Application']") |
        Where-Object {$_.Executable -match '(^|[\\/])(ChatGPT|Codex)\.exe$'} | Select-Object -First 1
}
function Event-Summary {
    foreach($channel in @('Microsoft-Windows-AppModel-Runtime/Admin','Microsoft-Windows-TWinUI/Operational','Microsoft-Windows-AppXDeploymentServer/Operational','Application')) {
        try {
            $events=@(Get-WinEvent -FilterHashtable @{LogName=$channel;StartTime=(Get-Date).AddHours(-2)} -MaxEvents 100 -ErrorAction Stop)
            $count=0
            foreach($event in $events) {
                $document=New-Object Xml.XmlDocument
                $document.XmlResolver=$null
                $document.LoadXml([string]$event.ToXml())
                $payload=@($document.SelectNodes("/*[local-name()='Event']/*[local-name()='EventData' or local-name()='UserData']") | ForEach-Object {$_.InnerText}) -join ' '
                if($payload -notmatch 'OpenAI\.Codex|(?:ChatGPT|Codex)\.exe'){continue}
                $codes=@([regex]::Matches($payload,'0x[0-9a-fA-F]{8}') | ForEach-Object {'0x'+$_.Value.Substring(2).ToUpperInvariant()} | Select-Object -Unique -First 6)
                $modules=@()
                foreach($field in $document.SelectNodes("/*[local-name()='Event']/*[local-name()='EventData' or local-name()='UserData']//*")) {
                    $label=if($field.HasAttribute('Name')){$field.GetAttribute('Name')}else{$field.LocalName}
                    $value=$field.InnerText.Trim()
                    if($label -match '(?i)^(?:ErrorCode|HResult|Result|ExceptionCode|Status)$') {
                        if($value -match '^(?:0x)?([a-fA-F0-9]{8})$'){$codes+='0x'+$Matches[1].ToUpperInvariant()}
                        elseif($value -match '^-?\d{1,10}$'){$codes+=('0x{0:X8}' -f ([Int64]$value -band 0xffffffffL))}
                    }
                    if($label -match '(?i)^(?:ModuleName|FaultingModuleName|FaultModuleName)$' -and $value -match '^[A-Za-z0-9_. -]{1,100}\.(?:dll|exe|node)$'){$modules+=$value}
                }
                $codes=@($codes | Select-Object -Unique -First 6)
                $lines.Add(('系统事件：{0}；ID {1}；{2}；状态/错误码 {3}{4}' -f $channel,[int]$event.Id,$event.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss'),$(if($codes.Count){$codes -join ', '}else{'未提供'}),$(if($modules.Count){'；故障模块 '+($modules[0])}else{''})))
                $count++
                if($count -eq 5){break}
            }
            if(!$count){$lines.Add('系统事件：'+$channel+'；近两小时未发现匹配项。')}
        } catch {$lines.Add('系统事件：'+$channel+'；未发现可读取的匹配记录。')}
    }
}
$ok=$true
try {
    $package=Get-AppxPackage -Name OpenAI.Codex -ErrorAction Stop | Sort-Object Version -Descending | Select-Object -First 1
    if(!$package){throw '当前用户未注册 OpenAI.Codex，请用原安装用户运行助手或通过官方安装器修复。'}
    if($package.Name -cne 'OpenAI.Codex' -or $package.PackageFamilyName -cne 'OpenAI.Codex_2p2nqsd0c76g0') {
        throw '客户端包身份不匹配，未进行注册修复。'
    }
    if(![IO.Path]::IsPathRooted($package.InstallLocation)){throw '客户端安装目录无效，未进行注册修复。'}
    $manifestPath=Join-Path $package.InstallLocation 'AppxManifest.xml'
    $document=New-Object Xml.XmlDocument
    $document.XmlResolver=$null
    $document.Load($manifestPath)
    $identity=$document.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']")
    if($identity.Name -cne 'OpenAI.Codex' -or $identity.Publisher -cne $package.Publisher -or $identity.Version -cne [string]$package.Version) {
        throw '当前包与安装目录的清单身份不一致，未进行注册修复。'
    }
    $app=Main-Application $document
    if(!$app -or $app.Id -notmatch '^[A-Za-z0-9.]+$'){throw '未找到客户端的桌面主入口，未进行注册修复。'}
    $root=[IO.Path]::GetFullPath($package.InstallLocation).TrimEnd('\')+'\'
    $executable=[IO.Path]::GetFullPath((Join-Path $package.InstallLocation $app.Executable))
    if(!$executable.StartsWith($root,[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw '客户端主程序缺失或路径异常，请通过官方安装器修复程序文件。'
    }
    $appId=$package.PackageFamilyName+'!'+$app.Id
    $lines.Add('当前用户客户端：'+$package.PackageFullName)
    $lines.Add('安装目录：'+$package.InstallLocation)
    $status=[string]$package.Status
    if($status -notmatch '^[A-Za-z, ]{1,100}$'){$status='无法识别'}
    $lines.Add('应用包状态：'+$status)
    $lines.Add('清单启动标识：'+$appId)
    $registered=$false
    try {
        $registeredManifest=Get-AppxPackageManifest -Package $package.PackageFullName -ErrorAction Stop
        $registeredApp=Main-Application $registeredManifest
        $registered=$null -ne $registeredApp -and $registeredApp.Id -ceq $app.Id
    } catch { }
    $lines.Add('当前用户注册清单入口：'+$(if($registered){'与安装清单一致'}else{'未读取到一致的入口'}))
    $startMenu=$false
    try{$startMenu=@(Get-StartApps -ErrorAction Stop | Where-Object {$_.AppID -ceq $appId}).Count -gt 0}catch{ }
    $lines.Add('系统开始菜单应用登记：'+$(if($startMenu){'发现匹配标识'}else{'尚未发现匹配标识或系统查询未完成'}))
    foreach($name in @('AppXSvc','ClipSVC','StateRepository')) {
        try {$service=Get-Service -Name $name -ErrorAction Stop; $lines.Add('系统服务 '+$name+'：'+[string]$service.Status)}
        catch {$lines.Add('系统服务 '+$name+'：无法读取。')}
    }
    $lines.Add('服务状态仅供诊断；未启动或修改系统服务。')
    if($Mode -eq 'repair') {
        $running=$false
        foreach($process in @(Get-Process -Name ChatGPT,Codex -ErrorAction SilentlyContinue)) {
            try {
                if([string]::Equals($process.MainModule.FileName,$executable,[StringComparison]::OrdinalIgnoreCase)){$running=$true}
            }catch{ }
        }
        if($running){throw 'Codex 仍在运行，请完全退出后再修复；未强制结束进程。'}
        # Re-register only this user's already installed official package. No
        # Remove/Reset/AllUsers, forced task termination or policy changes.
        Add-AppxPackage -DisableDevelopmentMode -Register $manifestPath -ErrorAction Stop | Out-Null
        $after=Get-AppxPackage -Name OpenAI.Codex -ErrorAction Stop | Where-Object {$_.PackageFullName -ceq $package.PackageFullName} | Select-Object -First 1
        if(!$after){throw '注册命令已结束，但尚未回读到原客户端包，请重启 Windows 后检查。'}
        $afterManifest=Get-AppxPackageManifest -Package $after.PackageFullName -ErrorAction Stop
        $afterApp=Main-Application $afterManifest
        if(!$afterApp -or $afterApp.Id -cne $app.Id -or [string]$after.Status -cne 'Ok') {
            throw '注册命令已结束，但包状态或入口校验未通过，请保留日志继续排查。'
        }
        $lines.Add('当前用户 Codex 应用注册命令已完成，包状态与入口回读通过。')
        $lines.Add('已保留配置、密钥和会话目录；请点击“打开 Codex”验证窗口，注册成功不代表客户端已经能启动。')
    }
    Event-Summary
    $lines.Add('只收集包、入口、服务状态、系统事件编号与错误码，不输出凭据或原始事件内容。')
} catch {
    $ok=$false
    $reason=[string]$_.Exception.Message
    # Only script-authored messages are safe to display; OS error messages can
    # contain unrelated data. Preserve codes separately for all other errors.
    $known=@('当前用户未注册 OpenAI.Codex，请用原安装用户运行助手或通过官方安装器修复。','客户端包身份不匹配，未进行注册修复。','客户端安装目录无效，未进行注册修复。','当前包与安装目录的清单身份不一致，未进行注册修复。','未找到客户端的桌面主入口，未进行注册修复。','客户端主程序缺失或路径异常，请通过官方安装器修复程序文件。','Codex 仍在运行，请完全退出后再修复；未强制结束进程。','注册命令已结束，但尚未回读到原客户端包，请重启 Windows 后检查。','注册命令已结束，但包状态或入口校验未通过，请保留日志继续排查。')
    if($known -contains $reason){$lines.Add($reason)}
    $lines.Add('启动'+$(if($Mode -eq 'repair'){'注册修复'}else{'状态检查'})+'未完成；错误码：'+(Code-Summary $_))
    Event-Summary
}
[pscustomobject]@{ok=$ok;lines=@($lines.ToArray())} | ConvertTo-Json -Depth 4 -Compress
