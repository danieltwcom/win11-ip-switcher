@echo off
setlocal
cd /d "%~dp0"
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /platform:x64 /codepage:65001 /utf8output /target:winexe /out:IpSwitcher.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll IpSwitcher.cs
exit /b %ERRORLEVEL%
