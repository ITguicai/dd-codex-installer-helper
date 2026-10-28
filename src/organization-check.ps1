param([switch]$LibraryOnly)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$WarningPreference='SilentlyContinue'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)

function Get-DDWslSetting([string]$Text) {
    # Conservatively recognize the ordinary generated TOML form; never edit it.
    if($Text.Contains('"""') -or $Text.Contains("'''")){return 'unsupported'}
    if($Text -match '(?m)^[ \t]*(?:desktop[ \t]*[.=]|["'']desktop["''][ \t]*=|\[\[[ \t]*desktop|\[[ \t]*["'']desktop["'']|["'']?runCodexInWindowsSubsystemForLinux["''][ \t]*=)'){return 'unsupported'}
    $headers=@([regex]::Matches($Text,'(?m)^[ \t]*\[[ \t]*desktop[ \t]*\][ \t]*(?:#[^\r\n]*)?\r?$'))
    if($headers.Count -gt 1){return 'unsupported'}
    if($headers.Count){
        $header=$headers[0];$next=[regex]::Match($Text.Substring($header.Index+$header.Length),'(?m)^[ \t]*\[')
        $end=if($next.Success){$header.Index+$header.Length+$next.Index}else{$Text.Length}
        $section=$Text.Substring($header.Index,$end-$header.Index)
        $keys=@([regex]::Matches($section,'(?m)^[ \t]*runCodexInWindowsSubsystemForLinux[ \t]*='))
        $values=@([regex]::Matches($section,'(?m)^[ \t]*runCodexInWindowsSubsystemForLinux[ \t]*=[ \t]*(true|false)[ \t]*(?:#[^\r\n]*)?\r?$'))
        if($keys.Count -gt 1 -or $keys.Count -ne $values.Count){return 'unsupported'}
        if($values.Count){return $values[0].Groups[1].Value}
    }
    return 'not-explicit'
}

function Get-DDLegacyWsl([object]$State) {
    # Client configuration migrations read this top-level global-state key.
    $value=$State.runCodexInWindowsSubsystemForLinux
    return ($value -is [bool] -and $value)
}

function Read-DDSmallFile([string]$Path) {
    $file=Get-Item -LiteralPath $Path -ErrorAction Stop
    if($file.Length -gt 8388608){throw 'File too large for this check'}
    return [IO.File]::ReadAllText($Path,[Text.Encoding]::UTF8)
}

function Get-DDPolicyMarkers([string]$Text) {
    # Only closed vocabulary is returned. Do not return raw log/exception text.
    $phases=@('home-creation','preparation','policy-read','policy-validation','node-permissions')
    $reasons=@('unavailable','timeout','transport','process','auth','rpc','config','protocol','home-creation','preparation','policy-read','policy-validation','node-permissions')
    foreach($line in ($Text -split '\r?\n')) {
        if($line -notmatch 'Desktop startup policy read failed'){continue}
        $phase=[regex]::Match($line,'\bphase["'']?\s*[:=]\s*["'']?([a-z-]+)')
        $reason=[regex]::Match($line,'\breason["'']?\s*[:=]\s*["'']?([a-z-]+)')
        '启动检查失败：phase='+$(if($phase.Success -and $phases -contains $phase.Groups[1].Value){$phase.Groups[1].Value}else{'unknown'})+'；reason='+$(if($reason.Success -and $reasons -contains $reason.Groups[1].Value){$reason.Groups[1].Value}else{'unknown'})
    }
    $patterns=[ordered]@{
        '托管设置同步失败'='Failed to synchronize managed preferences'
        '工作区要求加载失败'='failed to load workspace requirements'
        'WSL bwrap 源资源准备失败'='(?im)^.*bundled_executable_relocation_failed.*bwrap.*$'
        '发现 WSL 后台启动记录'='(?im)^.*stdio_transport_spawned.*spawnCommand["'']?\s*[:=]\s*["'']?wsl\.exe.*$'
        '后台初始化成功记录'='initialize_handshake_result[^\r\n]*outcome["'']?\s*[:=]\s*["'']?success'
        '组织设置云端读取超时记录'='(?im)^.*(?:cloudRequirements|cloudConfigBundle).*(?:Timeout|timed out|timeout).*$'
    }
    foreach($item in $patterns.GetEnumerator()){$count=[regex]::Matches($Text,$item.Value).Count;if($count){$item.Key+'：'+$count+' 次'}}
}

function Get-DDPolicyTail([string]$Path) {
    $stream=New-Object IO.FileStream($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try {
        if($stream.Length -gt 1048576){[void]$stream.Seek(-1048576,[IO.SeekOrigin]::End)}
        $reader=New-Object IO.StreamReader($stream,[Text.Encoding]::UTF8)
        try{return $reader.ReadToEnd()}finally{$reader.Dispose()}
    } finally {$stream.Dispose()}
}

if($LibraryOnly){return}
$lines=New-Object 'System.Collections.Generic.List[string]'
$ok=$true
$lines.Add('仅只读检查：不修改配置、登录、中文缓存、代理、组织策略或系统权限；不启动或结束 Codex。')
try {
    try {
        $package=Get-AppxPackage -Name OpenAI.Codex -ErrorAction Stop | Sort-Object Version -Descending | Select-Object -First 1
        if($package -and $package.Name -ceq 'OpenAI.Codex' -and [string]$package.Version -match '^\d+\.\d+\.\d+\.\d+$'){$lines.Add('当前用户 Codex 客户端版本：'+[string]$package.Version)}
        elseif(!$package){$lines.Add('当前用户尚未检测到已注册的 Codex 包；请确认使用安装客户端的同一 Windows 用户。')}
        else{$lines.Add('客户端包身份或版本格式未识别；未输出原始数据。')}
    }catch{$ok=$false;$lines.Add('客户端版本查询未完成；未输出原始系统错误。')}
    $setting='unknown';$legacy=$false;$legacyKnown=$true;$configKnown=$true
    try {
        if($env:CODEX_HOME -and ![IO.Path]::IsPathRooted($env:CODEX_HOME)){throw 'Relative CODEX_HOME'}
        $codexHome=if($env:CODEX_HOME){[IO.Path]::GetFullPath($env:CODEX_HOME)}else{Join-Path $env:USERPROFILE '.codex'}
        $lines.Add('配置目录来源：'+$(if($env:CODEX_HOME){'CODEX_HOME 自定义目录'}else{'当前用户默认 .codex 目录'}))
        $config=Join-Path $codexHome 'config.toml'
        if(Test-Path -LiteralPath $config -PathType Leaf){$setting=Get-DDWslSetting (Read-DDSmallFile $config)}
        else{$setting='not-explicit';$lines.Add('config.toml：未发现；未新建配置。')}
        $statePath=Join-Path $codexHome '.codex-global-state.json'
        if(Test-Path -LiteralPath $statePath -PathType Leaf){
            try {$legacy=Get-DDLegacyWsl ((Read-DDSmallFile $statePath) | ConvertFrom-Json)}
            catch{$legacyKnown=$false;$ok=$false;$lines.Add('旧运行环境设置：无法解析；内容已省略。')}
        }
        $lines.Add('显式 WSL 设置：'+$setting+'；旧设置明确启用 WSL：'+$legacy)
        if($setting -eq 'true' -or ($setting -eq 'not-explicit' -and $legacyKnown -and $legacy)){
            $lines.Add('运行环境选择：WSL。若准备阶段失败，可在保留配置的前提下切回 Windows 环境复测；本次只检查，不自动切换。')
        }elseif($setting -eq 'false' -or ($setting -eq 'not-explicit' -and $legacyKnown)){
            $lines.Add('运行环境选择：Windows（按本地配置/默认值判断）；无需重复切换 WSL。')
        }else{$ok=$false;$lines.Add('运行环境选择：无法可靠判断；未修改配置。')}
        $authPath=Join-Path $codexHome 'auth.json'
        if(Test-Path -LiteralPath $authPath -PathType Leaf){
            try {
                $auth=(Read-DDSmallFile $authPath) | ConvertFrom-Json
                $lines.Add('凭据存在状态：API 密钥 '+(![string]::IsNullOrWhiteSpace([string]$auth.OPENAI_API_KEY))+'；登录令牌 '+($null -ne $auth.tokens)+'。仅表示字段存在，不代表凭据有效或当前登录方式。')
            }catch{$ok=$false;$lines.Add('凭据格式检查未完成；内容已省略。')}
        }else{$lines.Add('凭据文件：未发现；不代表其他认证来源不可用。')}
    }catch{$ok=$false;$configKnown=$false;$lines.Add('本地配置读取未完成；请核对配置目录是否可读、CODEX_HOME 是否为绝对路径。未输出配置内容或原始错误。')}
    foreach($name in @('CODEX_HOME','CODEX_ELECTRON_USER_DATA_PATH','HTTP_PROXY','HTTPS_PROXY','ALL_PROXY','NO_PROXY','CODEX_CLI_PATH')){
        $lines.Add('环境变量 '+$name+'：'+$(if([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))){'未设置'}else{'已设置（具体内容已省略）'}))
    }
    $roots=[ordered]@{
        '普通目录'=(Join-Path $env:LOCALAPPDATA 'Codex\Logs')
        '应用包目录'=(Join-Path $env:LOCALAPPDATA 'Packages\OpenAI.Codex_2p2nqsd0c76g0\LocalCache\Local\Codex\Logs')
    }
    $markerCount=0
    foreach($root in $roots.GetEnumerator()) {
        try {
            if(!(Test-Path -LiteralPath $root.Value -PathType Container)){$lines.Add('客户端日志（'+$root.Key+'）：尚未发现。');continue}
            $files=@(Get-ChildItem -LiteralPath $root.Value -File -Filter '*.log' -Recurse -ErrorAction Stop | Sort-Object LastWriteTime -Descending | Select-Object -First 8)
            $index=0
            if(!$files.Count){$lines.Add('客户端日志（'+$root.Key+'）：目录存在，未发现 .log 文件。')}
            foreach($file in $files){
                $index++
                try {
                    $markers=@(Get-DDPolicyMarkers (Get-DDPolicyTail $file.FullName) | Select-Object -Unique -First 20)
                    $markerCount+=$markers.Count
                    $lines.Add('客户端日志（'+$root.Key+'）第 '+$index+' 份；更新时间 '+$file.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')+'；相关类别 '+$markers.Count+' 项。')
                    foreach($marker in $markers){$lines.Add('  '+$marker)}
                }catch{$ok=$false;$lines.Add('客户端日志（'+$root.Key+'）第 '+$index+' 份读取未完成；原始错误已省略。')}
            }
        }catch{$ok=$false;$lines.Add('客户端日志（'+$root.Key+'）：查询未完成；原始错误已省略。')}
    }
    if(!$markerCount){$lines.Add('本次范围内未发现指定启动错误类别；不代表客户端启动正常。启动早期可能尚未创建日志。')}
    $lines.Add('检查范围：两处常用客户端日志目录，各取最近 8 份 .log 的末尾最多 1 MiB；可能含历史记录，不等同于本次启动的全部日志。')
    $lines.Add('phase：preparation=准备后台；policy-read=读取组织设置；policy-validation=校验要求；node-permissions=应用网络权限。')
    $lines.Add('reason：process=后台进程类别；timeout=超时；transport=传输；auth=认证；unknown=未识别。类别用于定位，不能单独确定根因。')
    $lines.Add('下一步：保持错误弹窗时点击本检查，再复制本报告。无需重复汉化或重新注册；也不要仅凭此弹窗判定必须使用 VPN。')
}catch{
    $ok=$false
    $lines.Add('组织设置检查未全部完成；系统错误码 0x'+$_.Exception.HResult.ToString('X8')+'。未输出原始异常或客户配置。')
}
[pscustomobject]@{ok=$ok;lines=@($lines.ToArray())} | ConvertTo-Json -Depth 4 -Compress
