@echo off
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process PowerShell -Verb RunAs -ArgumentList '-ExecutionPolicy Bypass -NoProfile -File ""%~dp0Uninstall-MelhorWindows-MSIX.ps1"" -RemoveCertificate'"
