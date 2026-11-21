@echo off
rem Installs Velocita for SimHub: copies Velocita.SimHub.Plugin.dll and VelocitaSimHub.exe into the SimHub folder.
setlocal
cd /d "%~dp0"
if not exist "Velocita.SimHub.Plugin.dll" goto incomplete
if not exist "VelocitaSimHub.exe" goto incomplete
set "target="
for /f "usebackq delims=" %%p in (`powershell -NoProfile -Command "$p = Get-Process SimHubWPF -ErrorAction SilentlyContinue | Select-Object -First 1; if ($p -and $p.Path) { Split-Path $p.Path }"`) do set "target=%%p"
if not defined target if exist "%ProgramFiles(x86)%\SimHub\SimHubWPF.exe" set "target=%ProgramFiles(x86)%\SimHub"
if not defined target if exist "%ProgramFiles%\SimHub\SimHubWPF.exe" set "target=%ProgramFiles%\SimHub"
if not defined target set /p "target=SimHub folder (the one with SimHubWPF.exe in it): "
if not exist "%target%\SimHubWPF.exe" (
  echo SimHub wasn't found in "%target%".
  pause
  exit /b 1
)
tasklist /fi "imagename eq SimHubWPF.exe" 2>nul | find /i "SimHubWPF.exe" >nul
if not errorlevel 1 echo SimHub is running. Close it; this goes on once it has closed.
:waitclosed
tasklist /fi "imagename eq SimHubWPF.exe" 2>nul | find /i "SimHubWPF.exe" >nul
if not errorlevel 1 (
  ping -n 3 127.0.0.1 >nul
  goto waitclosed
)
taskkill /f /im VelocitaSimHub.exe >nul 2>&1
ping -n 2 127.0.0.1 >nul
copy /y "Velocita.SimHub.Plugin.dll" "%target%\" >nul || goto failed
copy /y "VelocitaSimHub.exe" "%target%\" >nul || goto failed
echo.
echo Installed in %target%.
echo Start SimHub and allow the Velocita plugin when it asks. The widgets are in Dash Studio, Overlays, named "Velocita - ...".
pause
exit /b 0

:incomplete
echo Velocita.SimHub.Plugin.dll and VelocitaSimHub.exe must be in the same folder as this file. Unzip everything first.
pause
exit /b 1

:failed
echo Couldn't copy into %target%. Right-click Install.cmd and choose Run as administrator.
pause
exit /b 1
