@echo off
:: Wrapper .bat pour lancer furrycord-uninstall.ps1 facilement (double-clic)
title Furrycord — Désinstallation
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0furrycord-uninstall.ps1"
if %errorlevel% neq 0 pause
