@echo off
:: Wrapper .bat pour lancer furrycord-install.ps1 facilement (double-clic)
title Furrycord — Installation
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0furrycord-install.ps1"
if %errorlevel% neq 0 pause
