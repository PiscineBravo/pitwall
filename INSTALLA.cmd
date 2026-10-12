@echo off
REM Doppio clic qui per installare Pitwall.
REM Questo file esiste perche Windows blocca gli script PowerShell scaricati o non firmati:
REM lanciandolo cosi, la restrizione vale solo per questa esecuzione.
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0INSTALLA.ps1" %*
echo.
pause
