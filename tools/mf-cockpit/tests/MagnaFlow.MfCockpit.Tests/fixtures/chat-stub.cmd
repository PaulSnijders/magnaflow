@echo off
setlocal
set "RESUMED="

:parseloop
if "%~1"=="" goto afterparse
if "%~1"=="--resume" set "RESUMED=%~2"
shift
goto parseloop

:afterparse
echo {"type":"system","subtype":"init"}
ping -n 2 127.0.0.1 >nul
echo {"type":"assistant","message":{"content":[{"type":"text","text":"thinking..."}]}}
ping -n 2 127.0.0.1 >nul

if defined RESUMED (
  echo {"type":"result","subtype":"success","session_id":"stub-session-001","result":"resumed reply","resumed_from":"%RESUMED%"}
) else (
  echo {"type":"result","subtype":"success","session_id":"stub-session-001","result":"first reply"}
)
exit /b 0
