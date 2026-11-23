@echo off
setlocal
pushd "%~dp0" || exit /b 1

set "G_HUB_CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%G_HUB_CSC%" set "G_HUB_CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%G_HUB_CSC%" (
    echo .NET Framework C# compiler was not found.
    goto failed
)
if not exist dist mkdir dist
if not exist obj mkdir obj

set "G_HUB_OPTIONS=/nologo /codepage:65001 /optimize+ /warnaserror+ /platform:anycpu"
set "G_HUB_REFS=/reference:System.Drawing.dll /reference:System.Windows.Forms.dll"
"%G_HUB_CSC%" %G_HUB_OPTIONS% %G_HUB_REFS% /target:winexe /out:dist\Logitech-GHUB-Startup-Fix.exe ^
    /win32manifest:src\app.manifest src\*.cs
if errorlevel 1 goto failed
"%G_HUB_CSC%" %G_HUB_OPTIONS% %G_HUB_REFS% /target:exe /main:GHubStartupFix.SmokeTests ^
    /out:obj\ServiceTests.exe src\*.cs tests\SmokeTests.cs
if errorlevel 1 goto failed

set "G_HUB_TEST_ARGS="
if /i "%~1"=="--verify-installed" set "G_HUB_TEST_ARGS=--installed"
obj\ServiceTests.exe %G_HUB_TEST_ARGS%
if errorlevel 1 goto failed

powershell.exe -NoProfile -Command ^
    "$ErrorActionPreference = 'Stop';" ^
    "$ghubExe = 'dist\Logitech-GHUB-Startup-Fix.exe'; $ghubZip = 'dist\Logitech-GHUB-Startup-Fix-win.zip';" ^
    "Compress-Archive -LiteralPath @($ghubExe, 'README.md') -DestinationPath $ghubZip -Force;" ^
    "Get-FileHash -LiteralPath @($ghubExe, $ghubZip) -Algorithm SHA256 | ForEach-Object { '{0}  {1}' -f $_.Hash.ToLowerInvariant(), (Split-Path -Leaf $_.Path) } | Set-Content -LiteralPath 'dist\SHA256SUMS.txt' -Encoding ASCII;" ^
    "Get-ChildItem -LiteralPath dist -File | Select-Object Name,Length"
if errorlevel 1 goto failed

popd
exit /b 0

:failed
popd
exit /b 1
