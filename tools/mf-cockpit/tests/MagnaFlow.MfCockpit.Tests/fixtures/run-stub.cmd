@echo off
setlocal
set "VERB=%~1"
set "HASJSON="

:parseloop
if "%~1"=="" goto afterparse
if "%~1"=="--json" set "HASJSON=1"
shift
goto parseloop

:afterparse
if /I "%VERB%"=="status" if defined HASJSON (
  echo [{"name":"web","running":true,"pid":9999,"url":"http://localhost:5001"}]
  exit /b 0
)
echo %VERB%: ok (stub)
exit /b 0
