# F1 — Made By Falcone

**نسخة تطوير `0.9.0-dev` — ليست Production.** سياسة البوابات: [docs/GATES.md](docs/GATES.md).

واجهة WinUI 3 بهوية **Gaming / Premium** فوق خدمات SystemCore الحالية. نسخة التطوير:
`artifacts/current/F1/F1.exe` — الإصدار `0.9.0-dev`، CURRENT DEVELOPMENT BUILD.
سجل الأصول: [F1 Assets](docs/F1-ASSETS.md). الخلفية المحلية مؤقتة باختيار المالك؛
أصل الجبال النظيف غير متوفر، واعتماد المطابقة البصرية ما زال Pending.

**Windows 11 فقط — x64.** الحد التقني build 22631؛ الدعم الرسمي يتبع دورة حياة كل إصدار عند إطلاق المنتج.
الإصدارات المستهدفة: Home / Pro / Enterprise / Education.
Windows 10 غير مدعوم صراحةً، وأُزيلت متطلبات واختبارات توافقه.

هذا نموذج تحقق أصلي C# / .NET 10 / WinUI 3، وليس إصدار المنتج.

## ما يوجد الآن

- نافذة `asInvoker` مع MVVM وDI وقائمة تنقل وDashboard بقراءات حقيقية.
- صفحة System Information ببيانات Windows والعتاد، Providers مستقلة وتحديث يدوي غير متزامن.
- صفحة Performance بقراءات CPU/RAM ونشاط الأقراص والشبكة والعمليات، ورسوم محدودة بآخر 60 ثانية.
- موارد عربية/إنجليزية، نصوص RTL/LTR مع شريط جانبي ثابت على اليسار، وثيمات Light/Dark/System وأسـطح معتمة وخلفية محلية ثابتة.
- Cleaner بفحص ومراجعة وتأكيد يدوي، ومسار Shell لسلة المحذوفات، ومساعد محدود لتنظيف Windows Temp.
- Uninstaller لجرد Win32/MSI/MSIX والإزالة الأصلية والتحقق بالهوية، ثم فحص بقايا موثقة وحذف مختار مع نسخ Registry احتياطية.
- Startup Manager لجرد Run/RunOnce ومجلدات Startup ومهام Logon/Boot، مع تعطيل واستعادة قابلين للعكس وتأكيد لكل عنصر.
- Memory Tools لقياسات RAM وCommit والـcache، سجل 60 ثانية، أكبر مستهلكي RAM/CPU، وتقييم ضغط الذاكرة وقرار تحسين يدوي صريح.
- Tools باختصارات موثوقة لأربع عشرة أداة Windows، مع فحص التوفر ونتائج تشغيل صريحة.
- Settings وAbout بالإصدار التطويري `0.9.0-dev`، وسجلات محلية محدودة وتصدير تشخيصي يدوي منقّح دون رفع بيانات.
- اختبارات Unit وأخرى تفحص الجهاز الحالي وبيانات الحزمة الحقيقية.
- وصفة مثبت تجريبي EXE في `installer/SystemCore.Phase0.iss`، بمعرّف منفصل ومسار Program Files ثابت.
- توثيق التوافق والأمن والتراخيص وSigning المؤجل إلى Release/Production.

Dashboard تقرأ CPU وRAM وسعة قرص النظام والجهاز ومدة التشغيل وعدد تسجيلات البرامج وبدء التشغيل.
تُحفظ تفضيلات اللغة والثيم وبيانات الاستعادة محليًا. بيانات Startup للمستخدم في `%LOCALAPPDATA%\SystemCore\StartupState`؛ وحالة المصادر المرتفعة الصلاحيات في `%PROGRAMDATA%\SystemCore-StartupState-v1` بصلاحيات كتابة مقيدة.
قد تنتهي عملية تحسين الذاكرة دون تنفيذ إجراء؛ لا توجد حاليًا cache إنتاجية ثبتت أهليتها للتحرير المستقل.

## بناء قابل للإعادة

المطلوب .NET SDK **10.0.401** على Windows 11 x64. انطلق من جذر المشروع:

```powershell
dotnet restore SystemCore.sln --locked-mode
dotnet build SystemCore.sln -c Release --no-restore
dotnet run --project tests/SystemCore.UnitTests -c Release --no-build
dotnet publish src/SystemCore.App -c Release --no-restore -o artifacts/publish/App
dotnet publish src/SystemCore.ElevatedHost -c Release --no-restore -o artifacts/publish/App/ElevatedHost
dotnet run --project tests/SystemCore.CompatibilityTests -c Release --no-build -- .
dotnet run --project tests/SystemCore.SystemInformationTests -c Release --no-build
dotnet run --project tests/SystemCore.PerformanceTests -c Release --no-build
dotnet run --project tests/SystemCore.UninstallerTests -c Release --no-build -- --synthetic --leftover-delete
dotnet run --project tests/SystemCore.StartupTests -c Release --no-build
dotnet run --project tests/SystemCore.StartupTests -c Release --no-build -- --mutation
dotnet run --project tests/SystemCore.MemoryToolsTests -c Release --no-build -- --integration
dotnet run --project tests/SystemCore.ProductTests -c Release --no-build -- --integration
```

أوقف الخطوات عند أي exit code غير صفر. متاح أيضًا `scripts/Build.ps1 -Dotnet <path>`؛
لا يتطلب تغيير سياسة PowerShell على مستوى الجهاز. اختباراتنا console runners؛ `dotnet test`
ليس بديلًا لتشغيلها. Native UI smoke:

```powershell
./scripts/Invoke-F1ProductUx.ps1 -AppPath artifacts/publish/App/F1.exe -EvidencePath artifacts/f1-ui/local-validation/ui
```

المثبت مخصص لبيئات الاختبار؛ اتبع [قائمة الاختبارات](docs/SMOKE-TESTS.md) داخل VM قابلة
للاسترجاع. لا تعتبر نجاح تجميع المثبت نجاحًا في Install/Uninstall أو تشغيل على جهاز نظيف.

## وثائق التسليم

[COMPATIBILITY.md](COMPATIBILITY.md) · [ARCHITECTURE.md](ARCHITECTURE.md) ·
[SECURITY.md](SECURITY.md) · [ROADMAP.md](ROADMAP.md) ·
[DEPENDENCIES.md](DEPENDENCIES.md) · [Signing](docs/SIGNING.md).

الهوية الظاهرة F1 وتوقيع Made By Falcone؛ الناشر التجاري وهوية التوقيع غير معينين. لا شهادة أو جاهزية تجارية مُدّعاة.
Release Gate: **PENDING** وفق [سياسة البوابات](docs/GATES.md)؛ هذه نسخة تطويرية وليست إصدار إنتاج.
