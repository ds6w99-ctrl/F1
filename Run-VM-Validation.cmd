@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
rem One-click clean-VM validation for F1. Run AFTER the Hyper-V reboot.
rem It launches the host orchestrator elevated (one UAC prompt), creates the Windows 11
rem Enterprise Evaluation VM, installs it unattended, runs the acceptance kit inside the VM,
rem and collects evidence under artifacts\vm-validation\.
set PS=C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe
echo Starting F1 clean-VM validation (elevated)...
%PS% -NoProfile -Command "Start-Process -FilePath '%PS%' -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','%~dp0scripts\Invoke-F1VmValidation.ps1' -Verb RunAs"
echo.
echo A UAC prompt was shown; accept it to start the validation.
echo Evidence will appear in artifacts\vm-validation\ when the run finishes.
timeout /t 20 >nul
