# Windows twin of build.sh: publish MyVoice, stop the running copy, install it for this user and add a
# Start-menu shortcut. No autostart — launch MyVoice from the Start menu.
#   powershell -ExecutionPolicy Bypass -File windows\build.ps1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$publish = Join-Path $PSScriptRoot 'publish'
$install = Join-Path $env:LOCALAPPDATA 'Programs\MyVoice'

Write-Host 'Publishing MyVoice...'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish src/MyVoice.Windows/MyVoice.Windows.csproj -c Release -o $publish --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Write-Host "Installing to $install..."
Get-Process MyVoice -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
if (Test-Path $install) { Remove-Item $install -Recurse -Force }
Copy-Item $publish $install -Recurse

$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'MyVoice.lnk'
$link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
$link.TargetPath = Join-Path $install 'MyVoice.exe'
$link.WorkingDirectory = $install
$link.Description = 'MyVoice dictation (Ctrl+Shift+D)'
$link.Save()

$mb = [math]::Round(((Get-ChildItem $install -Recurse | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "Done ($mb MB). Start MyVoice from the Start menu."
