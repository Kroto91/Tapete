@echo off
rem Startet das Reparaturskript daneben. Muss aus dem Explorer gestartet
rem werden, nicht aus Claude heraus - sonst landen die Aenderungen wieder
rem in der Umleitung des Containers statt im echten Benutzerprofil.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Reparatur.ps1"
