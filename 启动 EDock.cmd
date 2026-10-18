@echo off
setlocal
set "EDOCK_EXE=%~dp0publish\win-x64\EDock.exe"
if not exist "%EDOCK_EXE%" (
  echo EDock.exe was not found in publish\win-x64.
  echo Keep this launcher beside the publish folder.
  pause
  exit /b 1
)
start "" /D "%~dp0publish\win-x64" "%EDOCK_EXE%"
endlocal
