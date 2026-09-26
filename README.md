# F1 — Made By Falcone

**ابدأ المحادثة الجديدة من [NEW_CHAT_START_HERE](docs/NEW_CHAT_START_HERE.md).**
الحالة الحالية: [CURRENT-STATUS](docs/CURRENT-STATUS.md). إصلاحات ما قبل الإصدار جزئية؛ النسخة ليست Production.

واجهة WinUI 3 بهوية **Gaming / Premium** فوق خدمات SystemCore الحالية. نسخة التطوير:
`artifacts/current/F1/F1.exe` — الإصدار `0.9.0-dev`، CURRENT DEVELOPMENT BUILD.
تقرير التنفيذ والتحقق: [F1 UI Report](docs/F1-UI-IMPLEMENTATION-REPORT.md)،
والخطة: [F1 UI](docs/F1-UI-IMPLEMENTATION-PLAN.md)، وسجل الأصول:
[F1 Assets](docs/F1-ASSETS.md). الخلفية المحلية مؤقتة باختيار المالك؛ أصل الجبال
النظيف غير متوفر، واعتماد المطابقة البصرية ما زال Pending.

**Windows 11 فقط — x64.** الحد التقني build 22631؛ الدعم الرسمي يتبع دورة حياة كل إصدار عند إطلاق المنتج.
الإصدارات المستهدفة: Home / Pro / Enterprise / Education.
Windows 10 غير مدعوم صراحةً، وأُزيلت متطلبات واختبارات توافقه.

هذا نموذج تحقق أصلي C# / .NET 10 / WinUI 3، وليس إصدار المنتج.
حالة البوابة والأدلة: [TEST-RESULTS.md](TEST-RESULTS.md).

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
تفاصيل التقوية السابقة: [تقرير المرحلة 9](docs/PHASE-9-REPORT.md). حزمة المرحلة 9 في `artifacts/phase9/app/SystemCore.App.exe` محفوظة كخط أساس سابق؛ هوية F1 الجديدة في المسار أعلاه. قد تنتهي عملية تحسين الذاكرة دون تنفيذ إجراء؛ لا توجد حاليًا cache إنتاجية ثبتت أهليتها للتحرير المستقل.

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
[SECURITY.md](SECURITY.md) · [TEST-RESULTS.md](TEST-RESULTS.md) ·
[ROADMAP.md](ROADMAP.md) · [DEPENDENCIES.md](DEPENDENCIES.md) ·
[تقرير المرحلة](docs/PHASE-0-REPORT.md) · [Signing](docs/SIGNING.md).

الهوية الظاهرة F1 وتوقيع Made By Falcone؛ الناشر التجاري وهوية التوقيع غير معينين. لا شهادة أو جاهزية تجارية مُدّعاة.
Phase 0 Development Gate: **PASS**. Release Gate: **PENDING** وفق [سياسة البوابات](docs/GATES.md).
Phase 2 Development Gate: **PASS**. [تقرير المرحلة 2](docs/PHASE-2-REPORT.md).
Phase 3 Development Gate: **PASS**؛ [تقرير المرحلة 3](docs/PHASE-3-REPORT.md).
Phase 4 وPhase 5 وPhase 6 Development: **PASS**. Phase 7A وPhase 7 Development: **PASS**. **Phase 8 Development: PASS ومعتمدة من المالك**؛ أدلتها محفوظة في `artifacts/phase8/summary.json`. **Phase 9A و9B و9C وPhase 9 Development: PASS** بعد 1,291 فحصًا و1,000 مدخل اختبار مشوّه وثبات 24 دورة. توقف العمل لمراجعة المالك؛ التفاصيل في [تقرير المرحلة 9](docs/PHASE-9-REPORT.md) و`artifacts/phase9/summary.json`. Release / Production: **PENDING**. لم تبدأ Phase 10. المثبت وحزمة المرحلة 0 أدلة تاريخية لا تحتوي الواجهة الحالية.
