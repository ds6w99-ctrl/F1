# F1-001 — تأكيد النسخة وحسم نطاق إصلاح Cleaner (تحقق فقط)

> نوع المهمة: تحقق وتحضير فقط. لم يُعدَّل أي كود منتج. التغيير الوحيد هو إنشاء هذا التقرير.

## 1. خط الأساس وحالة Git

- جذر المشروع الفعلي (مكتشف لا مفترض): `G:\F1`
  - الدليل: `G:\F1\SystemCore.sln` موجود، `G:\F1\Directory.Build.props` موجود، `(Get-Location).Path = G:\F1`.
- البيئة التي نُفّذ فيها هذا التحقق:
  - `COMPUTERNAME=DESKTOP-FALCONE`، `USERNAME=Falcone`، `OS=Microsoft Windows NT 10.0.26200.0`.
  - هذه نفس بيئة المستخدم (Falcone)، وليست بيئة سحابية منفصلة في هذه الجلسة.
- الفرع الحالي: `pre-release-fixes` (لم يُغيَّر، ولا stash/reset/clean/commit/push).
- آخر commit: `6cc6e6071567793c304996a17e7d90162f09c4e4` — `feat: add product installer and portable distribution paths` — تاريخ `2026-10-16`.
- الإصدار: `0.9.0-dev`
  - `Directory.Build.props:16` `<Version>0.9.0-dev</Version>`
  - `installer/F1.iss:14-15` `#define ProductName "F1"` / `#define ProductVersion "0.9.0-dev"`
- git status قبل العمل:
  - `## pre-release-fixes`
  - `?? PROJECT_AUDIT_FOR_CHATGPT.md` (ملف غير متتبع موجود مسبقًا من جلسة سابقة، ليس من هذه المهمة).
  - لا تعديلات متتبعة (working tree نظيف عدا الملف المذكور).
- git status بعد العمل (متوقع): نفس ما سبق + `?? F1_TASK_001_REPORT.md` (هذا الملف). لم يُنفَّذ أي commit.
- تعليمات المستودع المقروءة دون تعديل: `README.md`، `ARCHITECTURE.md`، `docs/CLEANER-PROTOCOL.md`، `docs/MANUAL-VERIFICATION.md`، `installer/F1.iss`.

## 2. النتيجة: التعارض قائم ولم يُعالَج

**التعارض P1 قائم بتاريخ هذا الفحص.**

- الكود في الإنتاج يعرّف كاش المتصفحات كأهداف مدعومة ومحددة افتراضيًا وقابلة للتنفيذ.
- النصوص (إنجليزي/عربي) والتوثيق (`CLEANER-PROTOCOL` + `MANUAL-VERIFICATION` + `RELEASE-QA`) تقول إنها `unsupported in V1` / صفوف معطلة / غير مؤهلة.
- لا يوجد في مسار التنفيذ أي فلتر V1 يمنعها. التسجيل في `App.xaml.cs:81-83` يستخدم `WindowsCleanerTargets` مباشرة.
- لم يُعثَر على إصلاح لاحق يزيل التعارض. التوصية السابقة بقيت توصية ولم تُنفَّذ.

طريقة الثبوت: مراجعة كود فقط (لم يُشغَّل Build ولا اختبارات ذات آثار جانبية التزامًا بحدود المهمة). كل ما يلي مثبت بمراجعة الكود ما لم يُذكر أنه يحتاج تشغيلًا.

## 3. الأدلة ومسار التنفيذ (ملف:سطر)

### 3.1 تعريف الهدف — `src/SystemCore.Infrastructure/Cleaner/WindowsCleanerTargets.cs`

- `WindowsCleanerTargets.cs:11` `CacheAge = MinimumAgeFloor = 1h` (تعليق: كاش المتصفحات يتقلب خلال دقائق لذلك يستخدم الحد الأدنى).
- `WindowsCleanerTargets.cs:13-17` `ChromiumCacheLeaves` (Cache, Code Cache, GPUCache, DawnGraphiteCache, DawnWebGPUCache, GrShaderCache, ShaderCache, Media Cache, Service Worker/CacheStorage, Service Worker/ScriptCache).
- `WindowsCleanerTargets.cs:18` `FirefoxCacheLeaves = [cache2, startupCache, shader-cache]`.
- `WindowsCleanerTargets.cs:41-44` `IsAllowed` يسمح صراحة بـ `EdgeCache/ChromeCache/FirefoxCache/UserWer` مع شروط شكلية فقط (مسار تحت مجلد المتصفح + Pattern `*` + Recurse + غير مرتفع).
- `WindowsCleanerTargets.cs:64-66` `Resolve()` يستدعي `AddChromium(EdgeCache)` + `AddChromium(ChromeCache)` + `AddDirectoryLeaves(FirefoxCache)` دون أي شرط V1.
- `WindowsCleanerTargets.cs:73-82` `AddChromium`: إذا وُجد `User Data` يضيف كل ملف موجود بـ `Add(category, root, "*", true, CacheAge, true)` أي `DefaultSelected=true`.
- `WindowsCleanerTargets.cs:84-92` `AddDirectoryLeaves`: نفس الشيء لفايرفوكس بـ `DefaultSelected=true`.
- `WindowsCleanerTargets.cs:67-70` WER: إذا وُجد `ReportQueue/ReportArchive/Temp` يضيف بـ `ConservativeAge (24h)` و`selected=true`.
- النتيجة لكل متصفح:
  - EdgeCache: متاح فعلًا (أ)، محدد افتراضيًا `true` (ب)، قابل للاعتماد (ج، انظر 3.3)، يصل للتنفيذ (د، انظر 3.4).

### 3.2 الفحص — `src/SystemCore.Infrastructure/Cleaner/WindowsCleanerScanner.cs`

- `WindowsCleanerScanner.cs:22-31` `AcceptedTargets`: يحتفظ فقط بما يجتاز `IsAllowed` (الذي يسمح بالمتصفحات) ويسقط الجذر المتداخل المغطى بسلف مقبول.
- `WindowsCleanerScanner.cs:65,69` `accepted = AcceptedTargets(roots, category)`؛ `unavailable = accepted.Length == 0`. أي أن غياب مجلد المتصفح = `Unavailable`، ووجوده = فحص حقيقي. لا يوجد استثناء V1.
- `WindowsCleanerScanner.cs:115-123` فلاتر الملفات: حداثة (`MinimumAge=1h` للمتصفحات)، reparse/hardlink/سمات غير آمنة تُرفَض، ثم فتح حصري وهوية أصلية. هذه ضوابط سلامة عامة، ليست حظرًا للمتصفحات.
- `WindowsCleanerScanner.cs:149-157` الحالة: `Cancelled/Unavailable/Incomplete/Complete`؛ `CleanerCategoryResult:156-157` يحمل `DefaultSelected=accepted.Any(t=>t.DefaultSelected)` و`RequiresElevation=accepted.All(t=>t.RequiresElevation)` و`Review=accepted.All(t=>t.Review)`.
  - للمتصفحات: `DefaultSelected=true` (لأن كل الأهداف المضافة `true`)، `RequiresElevation=false`، `Review=false` (الوسيط `review` الافتراضي `false` في `Add`) — أي تُعرض كـ `CleanLow` لا `CleanReview`.

### 3.3 الاختيار والخطة — `src/SystemCore.App/Cleaner/CleanerViewModel.cs`

- `CleanerViewModel.cs:16` `CanSelect = Status==Complete && ItemCount>0`. لا فحص V1.
- `CleanerViewModel.cs:19-26` `CleanerCategoryRow`: `selected = result.DefaultSelected && CanSelect`. أي أن صف المتصفح يُحدَّد تلقائيًا إذا اكتمل الفحص ووجدت ملفات.
- `CleanerViewModel.cs:74` `CanClean`: أي فئة `Selected && CanSelect` وفيها عناصر غير مستثناة تكفي. لا استثناء للمتصفحات.
- `CleanerViewModel.cs:94-95` `Selection()`: يجمع كل `Selected && CanSelect` إلى `CleanerCleanRequest(SessionId, ItemIds[])`. مسار المتصفحات داخل.
- `CleanerViewModel.cs:63` `SafetyNotice = L["CleanSafety"]` (النص الذي يقول غير مدعوم — تناقض عرضي: التحذير يقول غير مدعوم بينما الصف قابل للتحديد).

### 3.4 التنفيذ — `src/SystemCore.Infrastructure/Cleaner/WindowsCleanerService.cs`

- `WindowsCleanerService.cs:67-69` المؤهلون = عناصر الفئات `Complete` فقط. لا فلتر فئة.
- `WindowsCleanerService.cs:73` `Execute(selected.Where(i=>Category != WindowsTemp))` — المتصفحات تنفَّذ محليًا دون مساعد مرتفع.
- `WindowsCleanerService.cs:104-163` `ExecuteValidated` + `ValidTarget:153-162`: يعيد حل `targets.Resolve()` ويتحقق من `IsAllowed` + تطابق المسار + الهوية + العمر. لا يوجد رفض لفئة المتصفح. `RequiresElevation` للمتصفحات `false` فيمر مباشرة.
- الخلاصة (د): لا يمنع أي تحقق لاحق كاش المتصفحات؛ يصل إلى `MarkFileForDeletion` على نفس المقبض المتحقق منه (`:128`).

### 3.5 التسجيل — `src/SystemCore.App/App.xaml.cs:81-83`

```cs
registrations.AddSingleton<ICleanerTargetProvider, WindowsCleanerTargets>();
registrations.AddSingleton<ICleanerScanner, WindowsCleanerScanner>();
registrations.AddSingleton<ICleanerService, WindowsCleanerService>();
```

- الإنتاج يستخدم المزوّد الكامل مباشرة. `#if CLEANER_VALIDATION:87-89` يستبدل الخدمة فقط في builds التحقق، ولا يؤثر على الإنتاج.

### 3.6 النصوص والتوثيق (هـ)

- `src/SystemCore.App/Resources/Shell.resx:223` `CleanEdgeCache = Microsoft Edge cache — unsupported in V1`
- `Shell.resx:224` Chrome نفس الصيغة، `:225` Firefox، `:227` `User Windows Error Reporting — unsupported in V1`
- `Shell.resx:232` `CleanSafety = ... Browser caches and WER are unsupported in V1.`
- `Shell.ar.resx:223-225,227,232` نفس المعنى بالعربية (`غير مدعومة في V1`).
- `docs/CLEANER-PROTOCOL.md:116-117` `Browser caches and user WER are UNSUPPORTED IN V1: disabled rows, no guessed profile roots and no browser personal-data deletion.`
- `docs/MANUAL-VERIFICATION.md:314-315` يجب أن تكون غير مؤهلة؛ `:351` جدول `unsupported in V1 لمخازن المتصفحات`.
- `docs/RELEASE-QA.md:139-140` `Browser caches and user WER are UNSUPPORTED IN V1, not partially enabled features.`
- التعارض صريح: الكود يخمّن جذور البروفايل (`SafeDirectories(userData)` + `SafeDirectories(parent)`) وينفّذ حذف بيانات متصفح، بينما البروتوكول يمنع تخمين الجذور وحذف بيانات المتصفح الشخصية.

### 3.7 الضوابط والاختبارات الموجودة (و)

مثبت بمراجعة الكود:

- ضوابط شكلية قوية لكنها لا تغني عن قرار السياسة: `IsAllowed` (مسار دقيق + Pattern + Recurse + Elevation)، `MinimumAgeFloor=1h`، رفض `CanDeleteDirectories` (`TargetTests.cs:34`)، رفض الارتفاع غير المتوقع (`:35`)، رفض النمط الخاطئ (`:36`)، رفض العمر <1h (`:32`)، رفض جذر `User Data` نفسه (`:22`) ورفض `Login Data` (`:21`) و`bookmarkbackups` (`:28`)، pinned handles + رفض reparse/hardlink + إعادة التحقق من الهوية والعمر عند التنفيذ.
- `TargetTests.cs:18-39` يثبت أن الشكل مسموح/مرفوض بدقة (code-review + يُتوقع PASS عند التشغيل، لكن لم يُشغَّل في هذه المهمة).
- `ExecutorTests.cs:1-100` يثبت ضمانات التنفيذ العامة (حذف المحدد، حماية المتغير/المقفل/الحديث، رفض الجلسة المستهلكة/المجهولة/المكررة/المنتهية 10د، رفض junction، رفض الجذر المحمي) — كلها على `FixtureTargets(UserTemp)` وليست على مجلدات متصفح حقيقية.
- ما لم يُثبَت بعد (يحتاج مهمة إصلاح + بيئة آمنة):
  - لا اختبار يثبت أن المتصفحات `Unavailable/غير مؤهلة` (العكس هو المُختبَر: `allowed`).
  - لا اختبار تكاملي على نسخ اصطناعية من بنية `User Data` يثبت سلوك المسح/الاختيار/الحذف للمتصفحات.
  - لا دليل على سلامة حذف كاش متصفح يعمل (ملفات مقفلة/قاعدة بيانات نشطة) ولا على عدم المساس ببيانات الجلسة/تسجيل الدخول.
  - لم يُراجَع سلوك الواجهة الفعلي (لقطة شاشة/تشغيل) في هذه المهمة.

## 4. مقارنة المسارين والتوصية

### المسار A — الالتزام بسياسة V1 (تعطيل كاش المتصفحات + WER)

- ما الذي سيتغير للمستخدم؟ صفوف Edge/Chrome/Firefox/WER تصبح `Unavailable` أو مخفية/غير قابلة للتحديد؛ لا حذف لأي ملف متصفح؛ النصوص والتوثيق تصبح صادقة.
- الملفات المتوقع تعديلها (تقدير أولي، ليس تكليفًا):
  - `src/SystemCore.Infrastructure/Cleaner/WindowsCleanerTargets.cs:64-70` (إيقاف إضافة أهداف المتصفحات/WER أو وضعها خلف راية معطلة مع إبقاء `IsAllowed` كحارس دفاعي).
  - `tests/SystemCore.CleanerTests/TargetTests.cs` (قلب توقعات `allowed` إلى مرفوض/غير محلول، + فحص `Resolve()` لا يتضمن الفئات الأربع).
  - نصوص `Shell.resx/ar.resx` تبقى صحيحة؛ قد تُضاف صياغة `Unavailable` للصف المعطل.
  - `docs/MANUAL-VERIFICATION.md:314-315,351` تبقى مرجع القبول (غير مؤهلة).
- المخاطر والأمان: منخفضة — تقليص سطح الحذف؛ الخطر الرئيسي هو كسر توقع اختبار موجود (مقصود) ونسيان فئة (يُعالَج بفحص `Resolve()` + `AcceptedTargets`).
- الاختبارات والقبول: `TargetTests` (مرفوض)، `ScanPerformanceTests/CompletionTests` (لا انحدار)، فحص يدوي: لا صفوف متصفح قابلة للتحديد على جهاز فيه متصفحات، `Unavailable`، لا IDs مؤهلة للمتصفحات في الجلسة.
- الجهد: صغير (تعديل موضعي + تحديث اختبارات + تحقق يدوي). التقدير: جلسة واحدة.

### المسار B — دعم الميزة رسميًا (استكمال متطلبات الدعم)

- ما الذي سيتغير للمستخدم؟ تبقى الصفوف مفعّلة ومحددة افتراضيًا (أو تُنقَل إلى Review/افتراضي-معطل بقرار منفصل)، لكن النصوص والتوثيق تُوحَّد إلى `مدعوم` مع تحذيرات صريحة (إغلاق المتصفح، بيانات الجلسة، WER).
- الملفات المتوقع تعديلها:
  - `Shell.resx:223-225,227,232` + `Shell.ar.resx` (إزالة `unsupported in V1` + صياغة دقيقة لكل فئة).
  - `docs/CLEANER-PROTOCOL.md:116-117` + `docs/MANUAL-VERIFICATION.md:314-315,351` + `docs/RELEASE-QA.md:139-140` (توثيق النطاق الجديد + شروط الأمان).
  - ربما `CleanerViewModel.cs` (نقل المتصفحات إلى `Review=true` / افتراضي-معطل) + `WindowsCleanerTargets.cs` (العمر الأدنى، القوائم البيضاء) حسب قرار السياسة.
  - اختبارات جديدة: مسح/تنفيذ اصطناعي لبنية `User Data` + حالات المتصفح المفتوح/المقفل + حماية `Login Data` وأشباهها.
- المخاطر والأمان: متوسطة-مرتفعة — حذف بيانات طرف ثالث نشط (متصفح يعمل، SQLite مقفل، Service Worker، ملفات الجلسة)؛ خطر حذف بيانات شخصية أو كسر جلسة المستخدم؛ يتطلب إثباتًا لا نصوصًا. تحديث النصوص وحده ليس دليل أمان.
- الاختبارات والقبول: اختبارات اصطناعية جديدة + `TargetTests` موسعة + تحقق يدوي على VM نظيفة مع متصفحات حقيقية (مغلقة/مفتوحة) + مراجعة أمنية لحدود `IsAllowed` + توثيق WER (بيانات تشخيصية قد تحوي مسارات).
- الجهد: متوسط-كبير (نصوص + توثيق + اختبارات + تحقق VM + مراجعة أمان). التقدير: عدة جلسات + قرار سياسة WER.

### التوصية وأسبابها

- **أوصي بالمسار A (الالتزام بـ V1) كإصلاح تالٍ**، للأسباب:
  1. النصوص والتوثيق والبوابة كلها متسقة على `UNSUPPORTED IN V1` (ثلاثة مواضع توثيق + نصوص عربي/إنجليزي)، بينما الكود هو الطرف الشاذ.
  2. `MANUAL-VERIFICATION:314-315` تجعل `غير مؤهلة` معيار نجاح يدوي قائمًا — الإبقاء على الميزة يكسر معيارًا معتمدًا.
  3. سلامة حذف كاش متصفح نشط لم تُثبَت (لا اختبار، لا تحقق VM)، ومبدأ السلامة هنا `fail-closed`: عند الشك نعطّل الحذف لا نوثّقه.
  4. الجهد صغير وقابل للمراجعة، ولا يوسّع سطح الحذف قبل Release Gate (PENDING).
- المسار B يبقى خيارًا منتجيًا مشروعًا لكنه يتطلب تكليفًا منفصلًا + إثباتات أمان + تحقق VM، ولا يجب اعتباره مغلقًا بتحديث نصوص فقط.
- تعطيل الميزة في الكود ليس قرارًا معتمدًا بعد — هذه المهمة تحقق فقط؛ القرار بيد المستخدم وChatGPT (انظر §6).

## 5. المعلومات غير المتحققة (حدود هذا الفحص)

1. السلوك المرئي الفعلي للصفوف (معطلة/مخفية/قابلة للتحديد) — يحتاج تشغيل واجهة أو لقطة، لم يُنفَّذ.
2. نتائج تشغيل `TargetTests/ExecutorTests` على هذه الشجرة — رُوجعت الكود فقط؛ التشغيل مؤجَّل لمهمة الإصلاح (بدون آثار جانبية: `TargetTests` + `ExecutorTests` على fixtures اصطناعية فقط، لا على مجلدات متصفح حقيقية).
3. وجود متصفحات فعلية على جهاز الفحص وعدد الأهداف المحلولة عبر `Resolve()` — لم يُستدعَ الكود.
4. سلوك WER التفصيلي (حساسية محتويات `ReportQueue/Archive`) — خارج نطاق هذه المهمة، لكنه مشمول بنفس التعارض.
5. أي تغييرات غير ملتزمة خارج `git status` المذكور — لا يوجد؛ `git status` نظيف عدا ملفي التقريرين غير المتتبعين.

## 6. القرار المطلوب من المستخدم وChatGPT

1. اعتماد أحد المسارين (A تعطيل ليوافق V1، أو B دعم رسمي بشروطه) — لا يُنفَّذ أي تعديل حتى يصل تكليف منفصل يسمّي المسار.
2. إن اعتُمِد A: هل يشمل WER مع المتصفحات (الموصى به، لأن النصوص والبروتوكول يجمعهما) أم المتصفحات فقط؟
3. إن اعتُمِد B: تحديد السياسة المطلوبة قبل أي كود — (أ) افتراضي-محدد أم افتراضي-معطل؟ (ب) `Review` أم `Low`؟ (ج) هل يُشترط إغلاق المتصفح؟ (د) هل WER داخل النطاق؟
4. تثبيت أن الإصدار يبقى `0.9.0-dev` ولا تبدأ Phase 10 ضمن مهمة الإصلاح.

## 7. معايير قبول مهمة الإصلاح التالية (تُستخدم بعد التكليف المنفصل)

- للمسار A:
  1. `WindowsCleanerTargets.Resolve()` على جهاز فيه Edge/Chrome/Firefox لا يُنتج أي هدف `EdgeCache/ChromeCache/FirefoxCache` (و`UserWer` حسب القرار) — يُثبَت باختبار وحدة.
  2. `TargetTests` المحدَّثة خضراء؛ `ExecutorTests` و`CompletionTests` بلا انحدار.
  3. فحص يدوي موثق: الفئات الأربع `Unavailable`/غير قابلة للتحديد، ولا IDs مؤهلة لها في الجلسة، والنصوص مطابقة.
  4. لا تغيير في الإصدار، لا ميزات جديدة، لا تعديل خارج الملفات المعتمدة.
- للمسار B:
  1. نصوص EN/AR وتوثيق (`CLEANER-PROTOCOL`, `MANUAL-VERIFICATION`, `RELEASE-QA`) موحدة على الدعم + شروطه.
  2. اختبارات اصطناعية جديدة لبنية المتصفحات (مغلقة/مفتوحة/مقفلة) خضراء + حماية `Login Data` وأشباهها مثبتة.
  3. تحقق VM نظيفة موثق + مراجعة أمان لحدود الحذف.
  4. قرارات السياسة الأربع (§6.3) مسجلة في التقرير قبل الدمج.

---

### ملحق: أوامر القراءة فقط المستخدمة (لا آثار جانبية)

- `git rev-parse --show-toplevel`، `branch --show-current`، `rev-parse HEAD`، `log -1`، `status --short --branch`.
- قراءة ملفات: `WindowsCleanerTargets.cs`، `WindowsCleanerScanner.cs`، `WindowsCleanerService.cs`، `CleanerModels.cs`، `App.xaml.cs`، `CleanerViewModel.cs`، `TargetTests.cs`، `ExecutorTests.cs`، `Shell.resx/ar.resx`، `CLEANER-PROTOCOL.md`، `MANUAL-VERIFICATION.md`، `RELEASE-QA.md`.
- لم يُشغَّل Build أو اختبار أو تنظيف حقيقي أو حذف أو تثبيت اعتماديات.
