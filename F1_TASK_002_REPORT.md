# F1-002 — تقرير تنفيذ إصلاح سياسة Cleaner في V1

> المسار المعتمد: A (تعطيل EdgeCache / ChromeCache / FirefoxCache / UserWer).
> الإصدار ثابت: `0.9.0-dev`. لا Phase 10، لا تغييرات هوية/مثبت/نشر، لا commit/push.

## 1. النتيجة المختصرة وما تغير فعليًا

- نُفّذ المسار A كاملًا بتعديل ملفين فقط:
  1. `src/SystemCore.Infrastructure/Cleaner/WindowsCleanerTargets.cs` (منتج): `IsAllowed()` يرفض الفئات الأربع صراحة، و`Resolve()` لم يعد ينتج أي هدف لها. حُذف الكود الميت المرتبط بها فقط (قوائم أوراق المتصفحات، مسارات `User Data`، `AddChromium`/`AddDirectoryLeaves`/`ChromiumTarget`/`LeafUnder`).
  2. `tests/SystemCore.CleanerTests/TargetTests.cs` (اختبارات): قُلبت توقعات `allowed` إلى `rejected` للفئات الأربع، وأُضيفت فحوص `Resolve()` (لا أهداف غير مدعومة + بقاء `UserTemp`).
- لم تُمسّ `WindowsCleanerScanner.cs` ولا `WindowsCleanerService.cs` ولا `CleanerViewModel.cs` ولا `App.xaml.cs` ولا أي نصوص/توثيق — السلوك الصحيح تحقق تلقائيًا عبر `IsAllowed`/`Resolve` (انظر §5).
- الإثبات: اختبار السياسة فشل قبل الإصلاح (7 FAIL للسبب الصحيح، exit=1) ونجح بعده (كل الفحوص PASS)؛ `executor` و`completion` و`scan-performance` و`UnitTests` خضراء؛ بناء Release للحل الكامل ناجح بلا تحذيرات.

## 2. جذر المشروع والفرع والـcommit والإصدار

- الجذر الفعلي المكتشف: `G:/F1` (عبر `git rev-parse --show-toplevel`)؛ المسار `G:\F1` المرجعي صحيح.
- الفرع: `pre-release-fixes` (لم يُغيَّر، لا worktree/branch جديد).
- الـHEAD: `6cc6e6071567793c304996a17e7d90162f09c4e4` — `feat: add product installer and portable distribution paths` (2026-10-16).
- الإصدار: `0.9.0-dev` (`Directory.Build.props:16` + `installer/F1.iss:14-15`).
- أدوات البناء: SDK `10.0.401` متوفر (`C:\Users\Falcone\.dotnet\dotnet.exe --version = 10.0.401`)؛ الاستعادة بـ `--locked-mode` دون تغيير نسخ.

## 3. السبب الجذري للتعارض

- `WindowsCleanerTargets.Resolve()` كان يضيف أهداف كاش المتصفحات (`AddChromium`/`AddDirectoryLeaves` بـ `DefaultSelected=true`) وأهداف WER، و`IsAllowed()` كان يقبلها شكليًا، والتسجيل في `App.xaml.cs:81` يستخدم هذا المزوّد مباشرة بلا فلتر V1.
- النصوص (`Shell.resx:223-225,227,232` + `Shell.ar.resx`) والتوثيق (`CLEANER-PROTOCOL.md:116-117`، `MANUAL-VERIFICATION.md:314-315,351`، `RELEASE-QA.md:139-140`) تعتبرها `unsupported in V1` وغير مؤهلة — لكن لا شيء في الكود كان يمنعها، فكانت قابلة للفحص والاختيار والحذف الفعلي.
- الجذر: غياب رفض الفئة نفسها في `IsAllowed` + إنتاج الأهداف في `Resolve`.

## 4. الملفات المعدلة وسبب تعديل كل ملف

### 4.1 `src/SystemCore.Infrastructure/Cleaner/WindowsCleanerTargets.cs` (11 إضافة / 69 حذفًا)

- `IsAllowed:28-35`: الفئات الأربع تُرفَض صراحة (`=> false`) بدل قواعد الشكل القديمة. السبب: سدّ ثغرة الحقن المباشر — أي هدف يُقدَّم مباشرة بهذه الفئات مرفوض حتى لو شكله مطابق للشروط القديمة. `AcceptedTargets` في الماسح و`ValidTarget` في الخدمة يستدعيان `IsAllowed`، فالمنع يسري على المسح والتنفيذ معًا دون حارس إضافي (أُثبت بالقراءة أن المسار الحالي يكفي، فلم تُضَف طبقات).
- `Resolve:38-57`: حُذفت استدعاءات `AddChromium(Edge/Chrome)` و`AddDirectoryLeaves(Firefox)` وحلقة `WerLeaves`. السبب: منع إنتاج الأهداف أصلًا (تغطية البند «أ»، وليس مجرد `DefaultSelected=false`).
- حُذف الكود الميت الذي لم يعد له أي استخدام (تجنّب `TreatWarningsAsErrors` في Release): `CacheAge`، `ChromiumCacheLeaves`، `FirefoxCacheLeaves`، `WerLeaves`، `EdgeUserData`/`ChromeUserData`/`FirefoxProfiles`/`WerRoot`، `AddChromium`/`AddDirectoryLeaves`/`SafeDirectories`/`ChromiumTarget`/`LeafUnder`/`Relative`. لا تغيير على الفئات المدعومة أو ضوابطها (`UserTemp/WindowsTemp/ShaderCache/CrashDumps/Thumbnails` كما هي).
- تعريفات الفئات في `CleanerCategory` (Core) لم تُحذَف ولم تتغير معرفاتها — المطلوب منع التنظيف لا حذف التعريف.

### 4.2 `tests/SystemCore.CleanerTests/TargetTests.cs` (10 إضافات / 8 حذفًا)

- قُلبت 6 توقعات من `allowed` إلى `rejected` (Edge/Chrome/Firefox/WER + `age at floor`) لتعبر عن سياسة V1.
- أُضيفت 3 فحوص: `Resolve()` لا يتضمن الفئات الأربع، و`UserTemp` ما زال يُحَلّ (عدم كسر المدعوم)، وغياب الفئات الأربع من المحلولة.
- حُوفظ على كل اختبارات السلامة الشكلية (رفض الجذر/الورقة المجهولة/العمر تحت الحد/حذف المجلدات/الارتفاع/النمط).

### 4.3 ملفات فُحصت ولم تُعدَّل (ولماذا)

- `WindowsCleanerScanner.cs`: `AcceptedTargets:22-31` يستدعي `IsAllowed` — المنع يسري تلقائيًا؛ لا حاجة لتعديل.
- `WindowsCleanerService.cs`: `ValidTarget:153-155` يستدعي `IsAllowed` + إعادة حل `Resolve()` عند التنفيذ — المنع يسري تلقائيًا؛ لا حارس إضافي needed.
- `CleanerViewModel.cs`: `CanSelect = Status==Complete && ItemCount>0` + `selected = DefaultSelected && CanSelect` — مع صفر أهداف محلولة تصبح الفئات `Unavailable` (الماسح: `accepted.Length==0 → Unavailable`) فتظهر غير قابلة للتحديد تلقائيًا؛ لا تعديل واجهة بلا حاجة.
- `App.xaml.cs`: التسجيل ما زال صحيحًا (المزوّد نفسه أصبح متوافقًا مع V1).
- `Shell.resx`/`Shell.ar.resx`: النصوص (`unsupported in V1`) أصبحت صادقة الآن؛ لم تُغيَّر (والممنوع تغييرها إلى «مدعومة»).
- `CLEANER-PROTOCOL.md`/`MANUAL-VERIFICATION.md`/`RELEASE-QA.md`: أوصاف `UNSUPPORTED IN V1`/`غير مؤهلة` أصبحت صحيحة تنفيذيًا؛ لا تحتاج توضيحًا إضافيًا في هذه المهمة.

## 5. كيف مُنعت الفئات الأربع في الإنتاج (وليس الواجهة فقط)

1. الإنتاج: `Resolve()` لا ينتج أي `CleanerTarget` بفئات `EdgeCache/ChromeCache/FirefoxCache/UserWer` على أي جهاز (لا يعتمد على تثبيت المتصفح).
2. السياسة: `IsAllowed()` تُرجع `false` لأي هدف بهذه الفئات الأربع مهما كان شكله — تحقق `TargetTests` بمسارات كانت صالحة الشكل وفق السياسة القديمة (مثل `<Edge User Data>\Default\Cache`) مرفوضة الآن.
3. المسح: `AcceptedTargets` يُسقط أي هدف مرفوض من `IsAllowed`، فلا تُنشأ عناصر مؤهلة ولا معرفات؛ الفئات الأربع تنتهي `Unavailable` (لا `Complete` ⇒ لا تنظيف، فـ `CleanAsync` يقبل فقط عناصر فئات `Complete`).
4. الاختيار/المراجعة: `CleanerCategoryRow.CanSelect=false` لغير `Complete`، و`Selection()` تجمع فقط `Selected && CanSelect` — لا يمكن إدراجها في `CleanerCleanRequest`.
5. التنفيذ: حتى لو حُقنت معرفات قديمة، `Execute` يعيد حل التعريفات ويتحقق عبر `ValidTarget → IsAllowed` على المسار الفعلي مع الهوية الأصلية — مرفوضة بـ `SafetyRejected` ولا تُفتح بمقبض حذف.
6. تعريفات `CleanerCategory` محفوظة (لا كسر عقود/تسلسل)، والواجهة تعرضها كصفوف `Unavailable` بنص `unsupported in V1` الموجود — ليس خطأ فحص ولا وعدًا بإعادة المحاولة.

## 6. الاختبار الذي فشل قبل الإصلاح (الأمر والنتيجة)

- الأمر (SDK المثبت `10.0.401`):
  `& 'C:\Users\Falcone\.dotnet\dotnet.exe' run --project 'G:\F1\tests\SystemCore.CleanerTests' -c Release --no-build`
  (بعد `restore --locked-mode` + `build -c Release --no-restore` للمشروع؛ البناء نجح 0 تحذيرات قبل التشغيل)
- النتيجة على السلوك القديم: `EXIT=1` مع 7 فشل كلها للسبب الصحيح (السياسة الجديدة مرفوضة والكود القديم يقبل):
  `FAIL: edge cache V1 policy rejected`، `FAIL: edge code cache V1 policy rejected`، `FAIL: edge service worker cache V1 policy rejected`، `FAIL: chrome cache V1 policy rejected`، `FAIL: firefox cache2 V1 policy rejected`، `FAIL: wer queue V1 policy rejected`، `FAIL: age at floor still rejected by V1 policy`.
- ملاحظة أمانة: فحصا `resolved contains no unsupported categories` و`supported UserTemp still resolves` نجحا قبل الإصلاح أيضًا — لأن `Resolve()` على جهاز الفحص لم ينتج أهداف متصفحات أصلًا (لا بروفايلات مثبتة): نجاح فارغ. الإثبات الحقيقي للمنع هو فحوص `IsAllowed` بالمسارات ذات الشكل الصالح قديمًا (الفاشلة قبل/الناجحة بعد)، وهي لا تعتمد على تثبيت المتصفح. لم تُنشَأ أي مجلدات `User Data` اصطناعية ولم تُمسّ بيانات متصفح حقيقية.

## 7. البناء والاختبارات بعد الإصلاح (أوامر فعلية)

كل الأوامر عبر `C:\Users\Falcone\.dotnet\dotnet.exe` (10.0.401)، وعدّاءات console (ليس `dotnet test`).

| الأمر | Exit | النتيجة |
|---|---|---|
| `build tests/SystemCore.CleanerTests -c Release --no-restore` (بعد الإصلاح) | 0 | نجاح، 0 تحذيرات / 0 أخطاء |
| `run --project CleanerTests -c Release --no-build` (افتراضي: فحص fixtures + `TargetTests`) | 0 | كل الفحوص PASS (≈46 سطرًا)؛ `Fixture retained (no deletion)` |
| `run --project CleanerTests ... -- --executor` | 0 | كل الفحوص PASS (22 سطرًا)؛ `Synthetic execution only; surviving fixture directories retained` — سلامة التنفيذ (محمي/متغير/مقفل/مكرر/منتهي/junction) بلا تراجع |
| `run --project CleanerTests ... -- --completion` | 0 | كل الفحوص PASS؛ `real Recycle Bin/Windows Temp were not emptied` — سلة المحذوفات والمساعد لم يُمسّا فعليًا |
| `run --project CleanerTests ... -- --scan-performance` (5 إعدادات workers) | 0 | كل الفحوص PASS؛ `fixtures retained; scan never deletes` |
| `run --project UnitTests -c Release --no-build` | 0 | `Orchestration assertions: 59 passed, 0 failed` |
| `build SystemCore.sln -c Release --no-restore` (الحل الكامل) | 0 | نجاح، 0 تحذيرات / 0 أخطاء، `F1.dll` مُنتَج |

- المخرجات الاصطناعية: ملفات fixtures داخل `%LOCALAPPDATA%\Temp\SystemCore-*` فقط، وأُبقيت (لا حذف) ما عدا ما يحذفه `ExecutorTests` من fixtures الخاصة به؛ لا مساس ببيانات المستخدم/المتصفحات/النظام.
- NOT RUN (مقصود): `--real-scan` (يفحص Temp الحقيقي) و`Test-CleanerRealScan.ps1` — خارج النطاق المسموح والآمن لهذه المهمة.

## 8. التحقق المرئي: NOT RUN

- لم يُشغَّل واجهة النسخة المبنية (لا بيئة تفاعلية في هذه الجلسة لفتح WinUI والتنقل لصفحة Cleaner بالعربية/الإنجليزية).
- السبب: تعذّر التشغيل التفاعلي الآمن، وليس بديلًا عنه مراجعة `ViewModel` (التزامًا بحدود المهمة: المراجعة ليست إثبات تشغيل).
- يُترك للتحقق اليدوي في مهمة لاحقة على VM/جهاز اختبار: صفحة Cleaner تُظهر الفئات الأربع `Unavailable`/غير قابلة للتحديد مع نص `unsupported in V1` باللغتين، دون ضغط أي تنظيف حقيقي.

## 9. جدول معايير القبول

| # | المعيار | الحالة | الدليل |
|---|---|---|---|
| 1 | `Resolve()` لا ينتج أي هدف للفئات الأربع | ثبت | `TargetTests` (فحص `no unsupported categories`) PASS بعد الإصلاح + قراءة `Resolve:38-57` |
| 2 | `IsAllowed` يرفض كل فئة منها حتى بشكل صالح قديمًا | ثبت | 7 فحوص V1: FAIL قبل → PASS بعد |
| 3 | `AcceptedTargets` يستبعدها | ثبت بالمراجعة + اختبار | `Scanner:22-31` يستدعي `IsAllowed`؛ لا عناصر للفئات الأربع في أي مسح محلول |
| 4 | لا عناصر/معرفات قابلة للاختيار والتنفيذ لها | ثبت بالمراجعة + اختبار | `Unavailable ⇒ CanSelect=false ⇒ Selection()` لا تشملها؛ `CleanAsync` يقبل فئات `Complete` فقط |
| 5 | تمريرها عبر مسار الاختبار لا يحذف ملفاتها | ثبت جزئيًا | المنع مثبت عند بوابة `IsAllowed` المشتركة (مسح/تنفيذ) بمسارات صالحة الشكل؛ لم تُجرَ كتابة/حذف داخل بروفايلات متصفح حقيقية (ممنوع)، والـfixtures الاصطناعية لم تُستخدم لتمثيل المتصفحات زورًا — يبقى إثبات الحذف-المرفوض على بروفايل حقيقي لمهمة VM لاحقة إن طُلب |
| 6 | الفئات المدعومة تعمل بلا انحدار | ثبت | `supported UserTemp still resolves` + `multi-root/nested` + `executor/completion/scan-performance/UnitTests` كلها PASS |
| 7 | سلامة التنفيذ بلا تراجع | ثبت | `executor` (22/22) + `completion` + بناء الحل 0 تحذيرات |
| 8 | التحقق المرئي للصفوف والنصوص | لم يُنفَّذ | NOT RUN — يحتاج جلسة UI تفاعلية (§8) |
| 9 | الإصدار `0.9.0-dev` ثابت، لا Phase 10، لا نشر | ثبت | لا تغيير لأي ملف إصدار/هوية/مثبت |

- لا تُوضَع Release Gate في PASS بناءً على هذا الإصلاح وحده.

## 10. نتائج Git الفعلية (ليست متوقعة)

- قبل العمل:
  `G:/F1` / `pre-release-fixes` / `6cc6e60` / `## pre-release-fixes` + غير متتبعَين موجودَين مسبقًا: `?? F1_TASK_001_REPORT.md`، `?? PROJECT_AUDIT_FOR_CHATGPT.md` / `git diff --stat`: فارغ (نظيف).
- `git diff --check` بعد الإصلاح: نظيف (exit 0، لا أخطاء مسافات).
- `git diff --numstat` بعد الإصلاح:
  `11  69  src/SystemCore.Infrastructure/Cleaner/WindowsCleanerTargets.cs`
  `10  8   tests/SystemCore.CleanerTests/TargetTests.cs`
- `git status --short --branch` بعد الإصلاح:
  `## pre-release-fixes`
  ` M src/SystemCore.Infrastructure/Cleaner/WindowsCleanerTargets.cs`
  ` M tests/SystemCore.CleanerTests/TargetTests.cs`
  `?? F1_TASK_001_REPORT.md` (موجود مسبقًا — لم يُستبدَل)
  `?? PROJECT_AUDIT_FOR_CHATGPT.md` (موجود مسبقًا — لم يُحذَف)
  + هذا الملف `F1_TASK_002_REPORT.md` عند الحفظ.
- لا commit/push/merge/reset/clean/stash/worktree/branch — التزامًا بالحدود.

## 11. المخاطر والعوائق والملاحظات المتبقية

1. التحقق المرئي لم يُنفَّذ (§8) — يُوصَى بجلسة UI تفاعلية لاحقة (عربي/إنجليزي) دون أي تنظيف حقيقي.
2. فحص `Resolve()` على جهاز بلا متصفحات مثبتة ينجح فراغيًا؛ الضمان الحقيقي هو `IsAllowed` (مُثبت)، ويُوصَى بإعادة تشغيل `TargetTests` على VM فيها بروفايلات Edge/Chrome/Firefox حقيقية للتأكيد الميداني (قراءة فقط: الفئات `Unavailable`).
3. أيقونة/اسم `CleanerCategory` ما زالت تتضمن الفئات الأربع في الـenum والقوائم — مقصود (منع التنظيف لا حذف التعريف) حتى لا تنكسر العقود.
4. خارج النطاق سُجّل ولم يُمسّ: Uninstaller/Startup/MemoryTools/ElevatedHost، سياسة استثناء الألعاب، قسم «إصلاح ويندوز»، المثبت والنشر، التقارير السابقة.
5. ملاحظة: مجلدات fixtures اصطناعية (`SystemCore-Cleaner-Test-*`, `SystemCore-ScanPerf-*`) أُبقيت عمدًا بتصميم العدّاء (لا حذف) داخل `%LOCALAPPDATA%\Temp`؛ إزالتها (إن لزمت) مهمة تنظيف منفصلة وليست جزءًا من هذه المهمة.
