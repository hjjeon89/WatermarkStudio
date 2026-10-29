$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
. (Join-Path $repository 'scripts\setup.ps1')
$testRoot = Join-Path $repository '.artifacts\test-check\setup'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$checks = 0
function Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw $Name }
    $script:checks++
    Write-Host "PASS $script:checks : $Name"
}
function Throws([scriptblock]$Action) {
    try { & $Action | Out-Null; return $false } catch { return $true }
}

$originalLocalAppData = $env:LOCALAPPDATA
$originalArchitecture = $env:PROCESSOR_ARCHITECTURE
$originalNativeArchitecture = $env:PROCESSOR_ARCHITEW6432
$originalSetupScript = $env:WATERMARK_SETUP_SCRIPT
$originalEncodingProbe = $env:WATERMARK_ENCODING_PROBE
try {
    foreach ($name in @('build.bat', 'install.bat', 'scripts\setup.ps1')) {
        $bytes = [IO.File]::ReadAllBytes((Join-Path $repository $name))
        Check (@($bytes | Where-Object { $_ -gt 127 }).Count -eq 0) "$name remains ASCII without a BOM"
    }
    Check ([Console]::InputEncoding.CodePage -eq 65001 -and [Console]::OutputEncoding.CodePage -eq 65001 -and
        $OutputEncoding.CodePage -eq 65001) 'PowerShell console and native pipelines use UTF-8'
    $korean = -join ([char[]]@(0xD55C, 0xAE00))
    $env:LOCALAPPDATA = Join-Path $testRoot 'user profile'
    $env:PROCESSOR_ARCHITECTURE = 'x86'
    $env:PROCESSOR_ARCHITEW6432 = 'AMD64'
    Check ((Get-RuntimeId) -eq 'win-x64') 'uses native OS architecture from a 32-bit shell'
    $env:PROCESSOR_ARCHITEW6432 = 'ARM64'
    Check ((Get-RuntimeId) -eq 'win-arm64') 'detects Windows ARM64'
    $env:PROCESSOR_ARCHITEW6432 = 'AMD64'

    $script:sdkLines = @('9.0.320 [C:\dotnet\sdk]', '10.0.403 [C:\dotnet\sdk]')
    $script:publishExitCode = 0
    function Invoke-FakeDotNet {
        if ($args[0] -eq '--list-sdks') { $script:sdkLines; $global:LASTEXITCODE = 0; return }
        $script:publishArguments = $args
        $global:LASTEXITCODE = $script:publishExitCode
        if ($script:publishExitCode -ne 0) { return }
        $directory = $args[[array]::IndexOf($args, '--output') + 1]
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        foreach ($file in @('WatermarkStudio.exe', 'WatermarkStudio.runtimeconfig.json', 'coreclr.dll', 'PresentationFramework.dll')) {
            Set-Content -LiteralPath (Join-Path $directory $file) -Value 'test fixture' -Encoding ASCII
        }
    }
    function Get-Command { param($Name, $CommandType, $ErrorAction) return [pscustomobject]@{ Source = 'Invoke-FakeDotNet' } }
    Check ((Get-DotNetSdk) -eq 'Invoke-FakeDotNet') 'reuses an installed .NET 10 SDK without downloading'
    $script:sdkLines = @('10.0.100-preview.7 [C:\dotnet\sdk]', '11.0.100 [C:\dotnet\sdk]')
    Check (-not (Test-DotNetSdk 'Invoke-FakeDotNet')) 'requires a stable .NET 10 SDK'
    $script:sdkLines = @('10.0.403 [C:\dotnet\sdk]')

    $projectRoot = Join-Path $testRoot "$korean source with spaces & !"
    New-Item -ItemType Directory -Path (Join-Path $projectRoot 'src\WatermarkStudio'), (Join-Path $projectRoot 'scripts') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $projectRoot 'src\WatermarkStudio\WatermarkStudio.csproj') -Value 'fixture' -Encoding ASCII
    Copy-Item -LiteralPath (Join-Path $repository 'install.bat'), (Join-Path $repository 'README.md') -Destination $projectRoot -Force
    Copy-Item -LiteralPath (Join-Path $repository 'scripts\setup.ps1') -Destination (Join-Path $projectRoot 'scripts') -Force
    $publish = Build-App
    Check ($script:publishArguments -contains '--self-contained' -and $script:publishArguments -contains 'true' -and
        $script:publishArguments -contains 'Release' -and $script:publishArguments -contains 'win-x64') 'publishes Release with the runtime included'
    Check (($script:publishArguments | Where-Object { $_ -like '-p:OutDir=*' }) -like '*/.artifacts/test-check/*') 'reuses persistent verification output'
    Check ((Test-Path -LiteralPath (Join-Path $publish 'install.bat')) -and
        (Test-Path -LiteralPath (Join-Path $publish 'scripts\setup.ps1'))) 'distribution contains its standalone installer'
    $script:publishExitCode = 1
    Check (Throws { Build-App }) 'build failures stop installation'
    $script:publishExitCode = 0

    $destination = Join-Path $testRoot "$korean installed app with spaces & !"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $note = Join-Path $destination 'user-note.txt'
    Set-Content -LiteralPath $note -Value 'keep me' -Encoding ASCII
    $executable = Install-App -PublishDirectory $publish -InstallDirectory $destination
    Check ((Test-Path -LiteralPath $executable) -and (Test-Path -LiteralPath (Join-Path $destination 'coreclr.dll'))) 'installs application and runtime into paths with spaces'
    Check ((Get-Content -LiteralPath $note -Raw).Trim() -eq 'keep me') 'preserves unrelated existing files'
    Check ((Install-App -PublishDirectory $destination -InstallDirectory $destination) -eq $executable) 'installing from the destination is harmless'
    $bad = Join-Path $testRoot 'incomplete package'
    New-Item -ItemType Directory -Path $bad -Force | Out-Null
    Check (Throws { Install-App -PublishDirectory $bad -InstallDirectory $destination }) 'rejects incomplete deployment before copying'
    function Get-Process { param($Name, $ErrorAction) return [pscustomobject]@{ Path = $executable } }
    Check (Throws { Install-App -PublishDirectory $publish -InstallDirectory $destination }) 'refuses to overwrite a running installed application'
    Remove-Item -LiteralPath Function:\Get-Process

    $shortcutPath = Join-Path $testRoot 'Watermark Studio.lnk'
    New-AppShortcut -ShortcutPath $shortcutPath -Executable $executable
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
    Check ($shortcut.TargetPath -eq $executable -and $shortcut.WorkingDirectory -eq $destination) 'shortcut points to the installed application'

    $smokeRoot = Join-Path $testRoot "$korean batch path with spaces & brackets (1)"
    New-Item -ItemType Directory -Path (Join-Path $smokeRoot 'scripts') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repository 'build.bat'), (Join-Path $repository 'install.bat') -Destination $smokeRoot -Force
    $env:WATERMARK_SETUP_SCRIPT = Join-Path $repository 'scripts\setup.ps1'
    $env:WATERMARK_ENCODING_PROBE = Join-Path $smokeRoot 'probe.json'
    $probeScript = @'
param([string]$Action)
. $env:WATERMARK_SETUP_SCRIPT -Action $Action
$message = -join ([char[]]@(0xD55C, 0xAE00, 0x20, 0xCD9C, 0xB825, 0x20, 0xD655, 0xC778))
$probe = @{ Action = $Action; Path = $PSScriptRoot; Input = [Console]::InputEncoding.CodePage; Output = [Console]::OutputEncoding.CodePage; Pipeline = $OutputEncoding.CodePage }
[IO.File]::WriteAllText($env:WATERMARK_ENCODING_PROBE, ($probe | ConvertTo-Json), $utf8)
Write-Host $message
exit 73
'@
    Set-Content -LiteralPath (Join-Path $smokeRoot 'scripts\setup.ps1') -Value $probeScript -Encoding ASCII
    foreach ($action in @('Build', 'Install')) {
        $caller = @"
@echo off
chcp 949 >nul
call "%~dp0$($action.ToLowerInvariant()).bat" --no-pause
set "probe_result=%errorlevel%"
chcp
exit /b %probe_result%
"@
        $callerPath = Join-Path $smokeRoot 'caller.bat'
        Set-Content -LiteralPath $callerPath -Value $caller -Encoding ASCII
        $process = New-Object System.Diagnostics.Process
        $process.StartInfo.FileName = $env:ComSpec
        $process.StartInfo.Arguments = '/d /s /c ""{0}""' -f $callerPath
        $process.StartInfo.UseShellExecute = $false
        $process.StartInfo.CreateNoWindow = $true
        $process.StartInfo.RedirectStandardOutput = $true
        $process.StartInfo.RedirectStandardError = $true
        $process.StartInfo.StandardOutputEncoding = $utf8
        $process.StartInfo.StandardErrorEncoding = $utf8
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEnd()
        $stderr = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        $probe = Get-Content -LiteralPath $env:WATERMARK_ENCODING_PROBE -Raw -Encoding UTF8 | ConvertFrom-Json
        Check ($process.ExitCode -eq 73 -and $probe.Action -eq $action -and
            $probe.Path -eq (Join-Path $smokeRoot 'scripts')) "$action batch forwards action and exit code from a Korean path"
        Check ($probe.Input -eq 65001 -and $probe.Output -eq 65001 -and $probe.Pipeline -eq 65001 -and
            $stdout.Contains($korean) -and $stderr.Length -eq 0) "$action batch preserves Korean output in UTF-8"
        Check ($stdout -match '949') "$action batch restores the caller's original code page"
        $process.Dispose()
    }

    $script:installedSdk = $false
    $script:installerExitCode = 0
    $script:downloadFails = $false
    function Test-DotNetSdk { param($Executable) return $script:installedSdk }
    function Invoke-WebRequest {
        param([switch]$UseBasicParsing, $Uri, $OutFile)
        if ($script:downloadFails) { throw 'simulated network failure' }
        $script:downloadUri = $Uri
        $script:downloadFile = $OutFile
        Set-Content -LiteralPath $OutFile -Value '# mock installer; never executed' -Encoding ASCII
    }
    function powershell.exe {
        $script:installerArguments = $args
        $script:installedSdk = $script:installerExitCode -eq 0
        $global:LASTEXITCODE = $script:installerExitCode
    }
    $sdk = Get-DotNetSdk
    Check ($sdk -eq (Join-Path $env:LOCALAPPDATA 'WatermarkStudio\dotnet\dotnet.exe') -and
        $script:downloadUri -eq 'https://dot.net/v1/dotnet-install.ps1') 'missing SDK downloads only from the official source into the user profile'
    Check ($script:installerArguments -contains '10.0' -and $script:installerArguments -contains 'GA' -and
        $script:installerArguments -contains 'x64' -and $script:installerArguments -contains '-NoPath') 'bootstrap selects stable SDK and leaves system PATH unchanged'
    Check (-not (Test-Path -LiteralPath $script:downloadFile)) 'downloaded installer script is cleaned up'
    $script:installedSdk = $false
    $script:installerExitCode = 1
    Check (Throws { Get-DotNetSdk }) 'SDK installer failures propagate'
    $script:downloadFails = $true
    Check (Throws { Get-DotNetSdk }) 'network failures propagate without attempting a build'
    Write-Host "All $checks setup checks passed. No real SDK was downloaded or installed."
}
finally {
    $env:LOCALAPPDATA = $originalLocalAppData
    $env:PROCESSOR_ARCHITECTURE = $originalArchitecture
    $env:PROCESSOR_ARCHITEW6432 = $originalNativeArchitecture
    $env:WATERMARK_SETUP_SCRIPT = $originalSetupScript
    $env:WATERMARK_ENCODING_PROBE = $originalEncodingProbe
    [Console]::InputEncoding = $previousInputEncoding
    [Console]::OutputEncoding = $previousOutputEncoding
    $OutputEncoding = $previousPipelineEncoding
}
