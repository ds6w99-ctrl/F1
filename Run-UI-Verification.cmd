@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

set APP=artifacts\publish\App\F1.exe
set EVID=artifacts\f1-ui\test-results\shell-regression
set SDK=C:\Users\Falcone\.dotnet\dotnet.exe

echo ================================================================
echo  F1 - الاختبار اليدوي المتبقي (نقرة واحدة)
echo ================================================================
echo.
echo هذا الملف يشغّل اختبار الواجهة الحقيقي على جهازك بنفسك.
echo سيفتح التطبيق، ويتنقّل بين كل صفحاته، ثم يُغلقه وحده.
echo.
echo لن يحذف أي ملف: الاختبار للقراءة فقط، ويُعيد إعداداتك كما كانت.
echo سيستغرق بين 6 و10 دقائق. لا تضغط على شيء داخل النافذة.
echo.

if not exist "%SDK%" set SDK=dotnet

if not exist "%APP%" (
  echo [1/2] جارٍ نشر التطبيق لأول مرة...
  "%SDK%" publish src\SystemCore.App\SystemCore.App.csproj -c Release -r win-x64 -o artifacts\publish\App
  if errorlevel 1 goto :failed
) else (
  echo [1/2] استُخدمت نسخة منشورة موجودة بالفعل.
)

echo [2/2] جارٍ تشغيل الاختبار الآن...
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test-Shell.ps1 -AppPath "%APP%" -EvidencePath "%EVID%"

if errorlevel 1 (
  echo.
  echo ---------------------------------------------------------------
  echo توقّف الاختبار قبل نهايته.
  echo.
  echo غالباً السبب أن النوافذ لا تستطيع أن تصبح أمامية إلا عندما
  echo تشغّل هذا الملف بالنقر المزدوج بنفسك، وليس من جلسة خلفية.
  echo بقية النتائج مسجّلة في:
  echo   %EVID%\ui-results.json
  echo ---------------------------------------------------------------
  timeout /t 120 >nul 2>nul
  exit /b 1
)

echo.
echo ================================================================
echo اكتمل الاختبار كاملاً بنجاح.
echo النتائج الكاملة:
echo   %EVID%\ui-results.json
echo ================================================================
timeout /t 30 >nul 2>nul
exit /b 0

:failed
echo.
echo فشل نشر التطبيق. راجع الرسائل أعلاه.
timeout /t 60 >nul 2>nul
exit /b 1
