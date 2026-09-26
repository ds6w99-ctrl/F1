# F1 — Pre-Release Fixes: Sidebar + Startup Manager + Cleaner Performance

أريد تنفيذ **3 إصلاحات محددة فقط** في مشروع F1 الحالي.

لا تبدأ Phase 10.
لا تعمل redesign جديد.
لا تغيّر بقية الصفحات إلا إذا كان هناك Shared component مطلوب مباشرة لهذه الإصلاحات.
لا تخفف أي قواعد Safety موجودة من المراحل السابقة.

المشاكل الحالية:

1. بطاقة `F1 Premium` الموجودة أسفل الـSidebar غير مطلوبة.
2. صفحة `إدارة بدء التشغيل` تعرض عناصر لا يستطيع المستخدم تعطيلها، ولا تعرض أيقونات البرامج.
3. فحص `Cleaner` للملفات بطيء ويحتاج تحسين أداء حقيقي.

---

# FIX 1 — REMOVE F1 PREMIUM CARD

احذف بطاقة:

```text
F1 Premium
```

الموجودة أسفل الـSidebar بالكامل.

البطاقة تشمل حاليًا عناصر مثل:

```text
F1 Premium
معلومات الجهاز
إدارة البرامج
Windows أدوات
عن F1
```

لا أريد هذه البطاقة نهائيًا.

## المطلوب بعد حذفها

- لا تترك مساحة فارغة كبيرة مكانها.
- اسمح للـSidebar باستغلال المساحة بشكل طبيعي.
- احتفظ بعبارة:

```text
Made By Falcone
```

في أسفل الـSidebar بشكل بسيط وأنيق.

- لا تحذف صفحة `About` نفسها.
- إذا كانت بطاقة F1 Premium هي المسار الوحيد للوصول إلى About، أبقِ الوصول إلى About بطريقة صغيرة وغير مزعجة:
  - إما داخل Settings
  - أو كـnavigation item صغير في أسفل الـSidebar
- **لا تنشئ Card بديلة.**

## Important

لا تغيّر:

- Logo
- اسم F1
- Sidebar navigation الرئيسية
- Theme
- بقية الواجهة

فقط احذف مربع F1 Premium ونظّف المساحة الناتجة.

---

# FIX 2 — STARTUP MANAGER: SHOW ONLY MANAGEABLE ITEMS

صفحة:

```text
إدارة بدء التشغيل
```

تعرض حاليًا عناصر نظامية مثل Tasks لا يستطيع المستخدم تعطيلها من F1.

هذا غير مفيد.

أريد القائمة الرئيسية تعرض فقط العناصر التي يستطيع F1 تنفيذ عملية فعلية عليها:

```text
Enable
or
Disable
```

بشكل آمن.

---

# Main List Eligibility

لا يظهر Startup item في القائمة الرئيسية إلا إذا:

```text
CanManage == true
```

أي أن F1 لديه مسار موثوق ومدعوم لتنفيذ Enable/Disable لهذا العنصر.

العنصر الذي لا يمكن تعديله فعليًا:

```text
HIDE FROM MAIN LIST
```

يمكن إبقاؤه في inventory الداخلي للاختبارات/التصنيف فقط.

---

# Hide Non-Manageable / Protected Items

اخفِ من القائمة الرئيسية:

- Windows protected scheduled tasks
- Microsoft system tasks
- Driver/hardware critical tasks
- Windows Security / Defender related tasks
- System maintenance tasks
- Tasks whose Enabled state cannot be safely changed
- Items classified as Protected
- Read-only items
- Entries with unsupported mutation strategy
- Entries where identity changed / cannot be verified
- Broken entries that cannot be safely restored
- Any entry that F1 cannot actually Enable or Disable

مثال من الصورة الحالية:

```text
AutomaticOfflineMemoryDiagnostic
AD RMS Rights Policy Template Management
```

وأي Windows/system task مشابه لا أريده في القائمة الرئيسية إذا F1 لا يستطيع أو لا يجب أن يعطله.

---

# Allowed Startup Sources

اعرض فقط العناصر القابلة للتحكم فعليًا من المصادر المدعومة:

```text
Registry Run
Registry RunOnce
User Startup Folder
Common Startup Folder
Supported third-party Scheduled Tasks
```

---

# Registry Run / RunOnce

اعرض العنصر إذا:

- identity واضحة
- source موثوق
- F1 يستطيع تعطيله/استعادته عبر الـreversible mechanism الموجود
- ليس Protected/System

HKLM items يمكن أن تظهر إذا F1 يدعم تعديلها عبر الـElevated Helper الآمن.

إذا لا يوجد مسار آمن:

```text
HIDE
```

---

# Startup Folder

اعرض العناصر التي يمكن:

```text
Disable -> reversible move
Enable -> exact restore
```

بأمان.

إذا path أو item غير موثوق أو Reparse:

```text
HIDE
```

---

# Scheduled Tasks

اعرض فقط third-party/user tasks التي:

- لها supported Logon/Boot trigger حسب تصميم Phase 6
- ليست Microsoft/System protected
- Task identity ثابتة
- Task Scheduler API يسمح بتغيير Enabled state
- read-back verification يعمل

إذا لا يمكن تعطيل Task:

```text
HIDE FROM MAIN LIST
```

---

# No Fake Disable

ممنوع إظهار زر:

```text
تعطيل
```

على عنصر لن يتمكن F1 من تعطيله فعليًا.

---

# Startup Counts

إذا تعرض عدد العناصر:

استخدم:

```text
Manageable startup items count
```

وليس raw inventory count.

مثال:

```text
12 startup items
```

يعني 12 عنصرًا يمكن للمستخدم Enable/Disable فعليًا من F1.

---

# ADD REAL APPLICATION ICONS TO STARTUP MANAGER

أريد أيقونة البرنامج الحقيقية بجانب كل Startup item.

التصميم:

```text
[ ICON ] Application Name
         Publisher • Source
```

الحجم المناسب:

```text
32x32
```

أو:

```text
36x36
```

بما يتناسب مع F1 UI الحالي.

---

# Startup Icon Source Priority

استخدم حسب Source:

## Registry Run / RunOnce

1. Parse executable path من command بشكل آمن.
2. استخرج icon من executable.
3. إذا غير متوفر -> fallback.

## Startup Folder

إذا `.lnk`:

1. Resolve shortcut target بأمان.
2. استخدم shortcut icon location إذا موثوقة.
3. ثم target executable icon.
4. fallback.

إذا `.exe`:

استخدم executable icon مباشرة.

## Scheduled Task

استخدم executable الخاص بـAction الموثوق فقط إذا:

- Action type مدعومة
- path قابلة للتحليل
- file موجود
- لا يتم تنفيذ الملف

ثم استخرج icon.

---

# Icon Safety

ممنوع:

- تنفيذ executable
- shell command
- cmd.exe
- PowerShell
- تشغيل scheduled task للحصول على icon

إذا icon metadata:

```text
missing
malformed
access denied
unsupported
```

استخدم:

```text
generic F1 startup icon
```

بدل ترك المساحة فارغة.

---

# Icon Performance

استخدم:

```text
Lazy loading
Bounded cache
Async/background extraction where appropriate
Cancellation on refresh/navigation
```

لا تجعل تحميل icons يجمد UI.

لا تعيد استخراج نفس icon في كل refresh إذا source لم تتغير.

---

# Startup Row Design

حافظ على Gaming / Premium الحالي.

كل row:

```text
[Icon]  Name                              Enabled
        Publisher • Registry / Task
```

يمكن عرض Scope بشكل صغير:

```text
Current user
All users
```

لكن لا تزدحم الصفحة.

---

# Selection State

عند اختيار item:

- neon blue accent
- subtle selected background
- واضح جدًا العنصر المحدد

---

# Buttons

فقط:

```text
Enable
Disable
Open file location
Details
```

حسب availability.

إذا العنصر Enabled:

```text
Disable
```

إذا DisabledBySystemCore:

```text
Enable
```

لا تعرض زرين فعالين متناقضين في نفس الوقت.

---

# Refresh

عند الضغط على:

```text
تحديث
```

نفذ:

1. raw inventory
2. safety classification
3. manageability classification
4. filter main list
5. icon refresh/cache validation
6. sorting/filtering

بدون stale rows.

---

# Tests — Startup Filtering

أضف tests:

```text
HKCU Run manageable => visible
HKLM Run manageable via helper => visible
User Startup manageable => visible
Common Startup manageable via helper => visible
third-party supported task => visible

Microsoft protected task => hidden
Windows maintenance task => hidden
unsupported scheduled task => hidden
read-only item => hidden
protected item => hidden
unknown/unverifiable identity => hidden
broken non-restorable item => hidden
```

---

# Tests — Startup Icons

اختبر:

```text
Registry executable icon
Shortcut icon
Shortcut target icon
Scheduled task executable icon
Missing executable
Malformed command
Missing icon
Fallback icon
Cache reuse
```

---

# Real Device Verification — Startup

على الجهاز الحالي:

اعرض في التقرير:

```text
Raw startup inventory:
Protected/non-manageable hidden:
Visible manageable items:
Registry:
Startup folders:
Scheduled tasks:
```

لا تعدل أي Startup item حقيقي أثناء automated validation.

ثم Screenshot فعلي للصفحة بعد التصفية.

---

# FIX 3 — CLEANER SCAN PERFORMANCE

الفحص الحالي للـCleaner بطيء.

أريد تحسين **سرعة Scan** بشكل ملموس مع الحفاظ الكامل على Safety.

ممنوع تحسين السرعة عبر:

- حذف Safety checks
- اتباع symlinks/junctions
- تقليل protected-path validation
- حذف age filtering
- تجاهل revalidation
- استخدام unsafe parallel deletion
- تغيير clean policy

هذه المرحلة تخص:

```text
SCAN PERFORMANCE
```

وليس تخفيف الأمان.

---

# First — Profile Before Optimizing

قبل تعديل الخوارزمية:

قِس الـscan الحالي.

سجل:

```text
Total scan duration
Items visited
Eligible items
Skipped items
Items/sec
Time per category
Slowest category
CPU average during scan
Approx memory delta
```

استخدم نفس dataset عند المقارنة بعد التعديل قدر الإمكان.

لا تبدأ optimization عشوائيًا قبل معرفة bottleneck.

---

# Category Timing

قِس كل Category منفصلة:

مثلاً:

```text
User Temp
Windows Temp
DirectX Shader Cache
Browser cache candidates
Crash Dumps
Thumbnail Cache
Recycle Bin query
```

حدد أي Category تستهلك أغلب الوقت.

---

# Scanner Optimization Principles

استخدم enumeration streaming.

يفضل:

```text
Directory.EnumerateFiles
Directory.EnumerateDirectories
Directory.EnumerateFileSystemEntries
```

أو `FileSystemEnumerable<T>` إذا كان مناسبًا وموثوقًا.

لا تستخدم:

```text
Directory.GetFiles
Directory.GetDirectories
```

إذا كانت ستنشئ arrays كبيرة بلا داعٍ.

---

# Avoid Excessive Allocations

لا تنشئ لكل file عدة objects بلا حاجة.

تجنب:

```text
FileInfo
full normalized path copies
multiple LINQ collections
ToList()
ToArray()
```

في hot scan path إذا ليست ضرورية.

استخدم streaming قدر الإمكان.

---

# Metadata Reads

لا تقرأ metadata أكثر من مرة لنفس file.

مثلاً:

إذا policy لـTemp هي:

```text
LastWriteTime > 24h
```

تحقق من العمر أولًا.

لا تقرأ Size إلا إذا الملف أصبح Candidate فعليًا أو الحجم مطلوب للنتيجة.

---

# Early Rejection

ارفض مبكرًا:

```text
Reparse points
Excluded patterns
Recent files
Unsupported file type if applicable
Protected target
Outside trusted root
```

قبل عمليات metadata الأغلى.

---

# Trusted Root Optimization

Trusted roots نفسها يمكن:

- resolve مرة واحدة لكل scan/category
- canonicalize مرة واحدة

ولا تعيد canonicalize root نفسها لكل item.

لكن:

كل candidate يجب أن يبقى ضمن root بشكل آمن.

ولا تخفف boundary-aware containment validation.

---

# Path Handling

لا تبنِ path strings متعددة بلا حاجة.

استخدم Windows-safe comparisons الموجودة حاليًا.

لا تستبدل SafetyValidator بخوارزمية أسرع لكنها أضعف.

---

# Parallelism

يمكن فحص Categories المستقلة بالتوازي إذا كان ذلك آمنًا.

لكن استخدم:

```text
Bounded concurrency
```

مثلاً:

```text
2-4 independent category workers maximum
```

واختر العدد بناءً على benchmark الفعلي.

لا تنشئ Task لكل File.

لا تستخدم unbounded parallelism.

---

# Disk-Aware Behavior

الـparallelism الزائد قد يجعل HDD/SSD أبطأ.

قارن:

```text
1 worker
2 workers
4 workers
```

على الجهاز الحالي.

اختر الأفضل فعليًا.

---

# UI Thread

كل filesystem enumeration الثقيلة خارج UI thread.

UI updates تكون throttled.

لا ترسل update لكل ملف.

مثلاً حدث UI:

```text
every 100-250ms
```

أو عند تغير Category.

لا تعمل Dispatcher call لكل item.

---

# Progress Updates

اعرض:

```text
Current category
Items found
Data found
```

لكن لا update UI آلاف المرات بالثانية.

---

# Cancellation

Cancel يجب أن يبقى سريعًا.

خلال loops:

تحقق من:

```text
CancellationToken
```

بفواصل مناسبة.

لا تنتظر نهاية category ضخمة قبل الاستجابة.

---

# Recycle Bin

استخدم Shell query فقط.

لا enumerate `$Recycle.Bin` يدويًا.

---

# Browser Cache

لا تعمل recursive scan واسع داخل profile.

ابدأ فقط من cache roots المعروفة والمسموحة.

لا تمس:

```text
Cookies
History
Passwords
Sessions
Extensions
Local Storage
IndexedDB
```

---

# Scan Result Storage

الـUI لا تحتاج جميع rows دفعة واحدة.

احتفظ بالـmodel الضروري للتنظيف الآمن، لكن:

- لا تنسخ collections بلا حاجة
- لا تنشئ display models duplicate لكل item إذا يمكن lazy mapping
- preview يظل bounded/virtualized

---

# Hashing

لا تعمل file hashing أثناء Cleaner scan العادي.

غير مطلوب.

---

# Logging

ممنوع log لكل file أثناء scan.

سجل summary فقط:

```text
Category
Visited
Eligible
Skipped
Bytes
Duration
Errors
```

logging لكل item سيبطئ الفحص.

---

# Exceptions in Hot Path

لا تعتمد على exception/catch كجزء طبيعي لكل file.

استخدم pre-checks / Try patterns حيث توجد.

Access denied أو locked:

```text
Skip
```

لكن لا تسجل stack trace آلاف المرات.

---

# Benchmark After Optimization

بعد التعديل، أعد نفس benchmark.

اعرض:

| Metric | Before | After | Change |
|---|---:|---:|---:|
| Total duration | | | |
| Items/sec | | | |
| User Temp | | | |
| Slowest category | | | |
| CPU avg | | | |
| Memory delta | | | |

---

# Performance Goal

لا أريد رقمًا مزيفًا ثابتًا.

المطلوب:

```text
Material, measured improvement
```

على نفس dataset.

إذا أمكن:

```text
2x faster
```

بدون زيادة CPU/Memory غير معقولة فهذا ممتاز.

لكن لا تفشل فقط لأن 2x لم تتحقق.

الأهم:

- bottleneck معروف
- improvement مثبت
- safety unchanged
- UI responsive

---

# Safety Regression — REQUIRED

بعد تحسين Scanner، أعد:

```text
Path traversal tests
Protected path tests
Reparse tests
Root deletion protection
Age filtering
Incomplete scan protection
Session integrity
Changed-since-scan
Cleaner executor/security
```

كلها يجب أن تبقى PASS.

---

# Cleaner Functional Regression

تحقق:

```text
Scan
Cancel
Rescan
Select
Synthetic clean
Actual bytes removed
Dashboard summary
```

---

# No Real User Destructive Test

لا تحذف ملفات المستخدم الحقيقية لأغراض performance test.

Scan الحقيقي مسموح Read-only.

Destructive tests تستخدم Synthetic files فقط.

---

# Build / Verification

بعد الإصلاحات الثلاثة شغّل:

```text
Debug Build
Release Build
x64 Publish

Sidebar UI smoke
Startup targeted tests
Startup UI regression
Cleaner performance benchmark
Cleaner safety regression
Cleaner integration
Full targeted regression for touched shared components
```

---

# Runtime Verification

شغّل F1 فعليًا.

أعطني Screenshots لـ:

```text
1. Sidebar بعد حذف F1 Premium
2. Startup Manager بعد التصفية والأيقونات
3. Cleaner أثناء/بعد Scan إذا تغير UX
```

---

# Important

لا تبدأ Phase 10.

لا تغير صفحات غير مطلوبة.

لا تغير F1 logo.

لا تغير Made By Falcone.

لا تغير version الحالية.

لا تنفذ أي destructive operation على بيانات المستخدم أثناء الاختبار.

---

# Final Report

أنشئ تقرير Markdown مثل:

```text
docs/PRE-RELEASE-FIXES-01.md
```

ويحتوي:

## Sidebar

```text
F1 Premium card removed: YES/NO
Made By Falcone preserved: YES/NO
About still accessible: YES/NO
```

## Startup Inventory

```text
Raw items:
Hidden protected/non-manageable:
Visible manageable:
```

## Startup Icon Sources

```text
Registry:
Startup Folder:
Scheduled Tasks:
Fallback:
```

## Cleaner Performance

| Metric | Before | After | Change |
|---|---:|---:|---:|
| Total scan | | | |
| Items/sec | | | |
| Slowest category | | | |
| CPU | | | |
| Memory | | | |

## Safety

أكد أن Cleaner/Startup safety regressions كلها PASS.

## Builds

```text
Debug:
Release:
x64 Publish:
```

## Screenshots

أعطني المسارات للصور الفعلية.

ثم توقف وانتظر مراجعتي.

**لا تبدأ Phase 10.**


---

# FIX 4 — REDUCE TEXT CLUTTER ACROSS THE APP

أريد تقليل حشو الكلام في التطبيق بشكل واضح.

القاعدة الجديدة للـUI:

```text
Show the minimum text needed to understand and use the feature.
```

أريد الواجهة:

- مختصرة جدًا.
- واضحة.
- مباشرة.
- بدون شروحات طويلة.
- بدون جمل تعليمية غير ضرورية.
- بدون تكرار نفس المعلومة بصيغ مختلفة.
- بدون وصف واضح أصلًا من اسم العنصر.

---

# Product Copy Rule

كل نص في الواجهة يجب أن يحقق واحدة من هذه الوظائف فقط:

```text
Name
Value
Status
Action
Required warning
Required error
```

إذا النص لا يضيف واحدة من هذه:

```text
REMOVE IT
```

---

# Keep Text Short

استخدم عبارات قصيرة جدًا.

بدل:

```text
قم بفحص النظام للبحث عن الملفات غير الضرورية التي قد تكون قابلة للإزالة لتحرير مساحة التخزين.
```

استخدم:

```text
فحص الملفات غير الضرورية
```

أو فقط:

```text
بدء الفحص
```

---

# Dashboard

لا أريد وصفًا طويلًا أسفل كل Card.

مثال:

بدل:

```text
استخدام المعالج الحالي وإجمالي عدد الأنوية المنطقية ومعلومات الأداء الحالية
```

يكفي:

```text
CPU
12%
```

بدل:

```text
كل ما تحتاجه للحفاظ على نظامك في أفضل حال
```

إذا كانت تسبب ازدحامًا:

اختصرها إلى:

```text
حالة النظام
```

أو احذفها إذا العنوان واضح بدونها.

---

# Cleaner

لا تضع شروحات طويلة لكل Category.

اعرض فقط:

```text
Name
Items
Size
Risk if needed
```

مثال:

```text
الملفات المؤقتة
1.2 GB
1,420 ملف
```

لا نحتاج paragraph يشرح ما هي الملفات المؤقتة.

الشرح يظهر فقط إذا كانت هناك مخاطرة حقيقية أو معلومة مهمة جدًا.

---

# Uninstaller

في القائمة:

يكفي:

```text
Icon
Name
Publisher
Version
Size
```

لا تعرض شرحًا إضافيًا لكل تطبيق.

في Details:

اعرض القيم مباشرة.

مثال:

```text
الإصدار      1.2.3
الناشر       Valve
الحجم        4.8 GB
النوع        Win32
```

بدون جمل مثل:

```text
هذا البرنامج مثبت على جهازك ويمكن إزالته باستخدام أداة الإزالة الأصلية.
```

إلا إذا هناك حالة خاصة.

---

# Startup Manager

اختصر الصف إلى:

```text
Icon
Name
Publisher / Source
Status
```

لا تعرض وصفًا طويلًا لكل عنصر.

مثال:

بدل:

```text
هذا العنصر يبدأ تلقائيًا عند تسجيل الدخول إلى Windows ويمكن تعطيله لتحسين سرعة بدء التشغيل.
```

استخدم:

```text
يبدأ مع Windows
```

أو لا تعرض أي وصف إذا Status واضحة.

---

# Performance / Memory

لا تشرح المفاهيم على الصفحة الرئيسية باستمرار.

مثال:

بدل:

```text
Committed memory is memory Windows has promised to processes and is backed by RAM or the page file.
```

خله Tooltip أو Info icon فقط.

في الواجهة الرئيسية:

```text
Commit
12.4 / 40.0 GB
```

فقط.

---

# System Information

اعرض:

```text
Label    Value
```

بدون فقرات.

مثال:

```text
المعالج      Intel Core i5-10400F
الأنوية      6
الخيوط       12
```

لا تضف وصفًا يشرح معنى كل قيمة إلا Tooltip عند الحاجة.

---

# Tools

كل Tool:

```text
Icon
Name
Open
```

الوصف يكون سطرًا قصيرًا جدًا فقط إذا الاسم غير واضح.

مثال:

```text
إدارة الأجهزة
عرض أجهزة وتعريفات النظام
```

ولا أكثر.

---

# Settings

لا أريد descriptions طويلة تحت كل setting.

استخدم:

```text
Setting name
Control
```

مثال:

```text
المظهر        [النظام ▼]
اللغة         [العربية ▼]
```

إذا احتاج setting شرحًا:

سطر واحد قصير جدًا.

---

# About

اختصرها إلى:

```text
F1
Version
Made By Falcone
Windows 11 x64
```

وباقي التفاصيل داخل روابط/أزرار:

```text
Licenses
Logs
Diagnostics
```

لا تضع فقرات تعريفية طويلة.

---

# Warnings

التحذيرات المهمة تبقى.

لكن اختصرها.

بدل:

```text
تحذير: عملية حذف هذه الملفات نهائية ولا يمكن للتطبيق ضمان إمكانية استعادتها لاحقًا.
```

استخدم:

```text
سيتم حذف الملفات المحددة نهائيًا.
```

---

# Errors

Error message تكون:

```text
What happened
What to do
```

في سطر أو سطرين.

مثال:

```text
تعذر حذف الملف.
قد يكون قيد الاستخدام.
```

لا تعرض شرحًا تقنيًا طويلًا.

---

# Empty States

اختصرها.

مثال:

```text
لا توجد برامج قابلة للإزالة.
```

بدل paragraph طويل.

---

# Loading States

استخدم:

```text
جارٍ الفحص...
جارٍ التحميل...
جارٍ التحديث...
```

فقط.

لا تضف جمل شرح إضافية.

---

# Tooltips Instead of Paragraphs

أي شرح ثانوي أو تقني:

انقله إلى:

```text
Tooltip
Info icon
Details flyout
```

بدل إبقائه دائمًا في الصفحة.

---

# Maximum Text Guidelines

استخدم هذه الحدود كهدف:

```text
Page title: 1-4 words
Card title: 1-4 words
Button: 1-3 words
Status: 1-5 words
Description: one short line maximum
Warning: 1-2 short lines maximum
```

إذا النص أطول:

اختصره أو انقله إلى Tooltip/Details.

---

# Arabic / English

طبّق نفس مبدأ الاختصار في اللغتين.

لا تكون العربية مختصرة والإنجليزية طويلة أو العكس.

---

# Remove Redundant Text

ابحث في كل UI text عن:

```text
duplicate explanations
marketing filler
obvious descriptions
repeated status
repeated page title
technical explanations visible by default
```

واحذفها.

---

# Keep Necessary Safety Text

مهم جدًا:

لا تحذف:

- confirmation for destructive actions
- required safety warnings
- error reason/action
- accessibility labels
- legal/license text إذا مطلوب
- privacy-critical notices
- state that prevents user confusion

الهدف:

```text
Less text, not less safety.
```

---

# Visual Result

أريد التطبيق يبدو:

```text
Clean
Premium
Fast to read
Low text density
Information-first
```

خصوصًا:

- Dashboard
- Cleaner
- Uninstaller
- Startup
- Performance
- Memory
- System Information
- Tools
- Settings
- About

---

# Verification for Text Reduction

أعطني قبل/بعد على الأقل لـ:

```text
Dashboard
Uninstaller
Startup
Cleaner
Settings
```

وتأكد أن:

- الوظائف ما زالت مفهومة.
- لا يوجد نص مقطوع.
- لا يوجد فراغات غريبة بعد حذف الشروحات.
- RTL/LTR صحيح.
- Tooltips تعمل للمعلومات الثانوية.

---

# Update Final Report

أضف قسم:

## UI Text Reduction

ويحتوي:

```text
Strings removed:
Strings shortened:
Moved to tooltips:
Pages updated:
```

وأرفق screenshots بعد الاختصار.



---

# FIX 5 — SMART GAME LIBRARY DISCOVERY IN UNINSTALLER

قسم إزالة البرامج يجب أن يكون أذكى من Windows Uninstall Registry فقط.

أريد F1 يكتشف الألعاب المثبتة من Game Launchers تلقائيًا، حتى لو اللعبة نفسها لا تظهر كتطبيق Win32/MSI عادي في Windows Installed Apps.

الهدف:

```text
Installed Apps + Installed Games = One unified removable library
```

لكن بدون تخمين أو Whole-Disk Search عشوائي.

## Supported Game Sources — V1

ادعم قدر الإمكان وبمصادر موثوقة:

```text
Steam
Epic Games Launcher
Xbox / Microsoft Store games
EA app
Ubisoft Connect
Battle.net
```

وهيكل الـArchitecture يسمح بإضافة Launchers أخرى لاحقًا عبر Providers منفصلة.

مثال:

```text
IGameLibraryProvider
SteamGameLibraryProvider
EpicGameLibraryProvider
XboxGameLibraryProvider
EaGameLibraryProvider
UbisoftGameLibraryProvider
BattleNetGameLibraryProvider
```

## Important Discovery Principle

ممنوع اكتشاف الألعاب عبر:

```text
Search entire C:\ for game folders
Guess game by folder name
Search for .exe files across drives
```

استخدم فقط:

```text
Launcher manifests
Launcher library metadata
Windows package identity
Registered uninstall metadata
Official launcher configuration
```

إذا لم يوجد مصدر موثوق:

```text
Do not guess.
```

## Steam

اكتشف Steam installation من Registry/metadata ثم اقرأ مكتبات Steam الفعلية.

ادعم تعدد المكتبات على أكثر من قرص.

استخدم:

```text
steamapps/libraryfolders.vdf
steamapps/appmanifest_*.acf
```

لاستخراج:

```text
AppId
Name
InstallDir
Install state
Library path
Size when reliably available
```

لا تفترض أن Steam موجود على C:.

اكتشف كل المكتبات المعرّفة في `libraryfolders.vdf`.

### Steam Uninstall

لا تحذف مجلد اللعبة مباشرة.

استخدم آلية Steam الأصلية/المسجلة للإزالة فقط إذا أمكن إثباتها فعليًا.

لا تخترع URI أو command غير موثق.

إذا uninstall mechanism غير قابلة للإثبات:

```text
Managed by Steam
Open in Steam
```

بدل fake uninstall.

## Epic Games

اكتشف الألعاب من manifests الفعلية التي ينشئها Epic.

استخرج فقط metadata موثوقة:

```text
Game name
Install location
App/catalog identity
Version if available
Size if available
Launcher ownership
```

لا تعتمد على folder-name guessing.

### Epic Uninstall

استخدم فقط mechanism يمكن إثباته من launcher metadata / registered protocol / official local behavior.

لا تخترع URI بدون تحقق.

إذا uninstall غير قابلة للتنفيذ بأمان:

```text
Managed by Epic Games
Open in Launcher
```

## Xbox / Microsoft Store Games

استخدم:

```text
PackageManager
Package identity
AppListEntry
```

وWindows deployment APIs الرسمية.

لا تحذف WindowsApps folders يدويًا.

إذا package removable:

```text
CanUninstall = true
```

إذا protected/framework/resource:

```text
Hide from removable list
```

## EA app / Ubisoft Connect / Battle.net

اكتشف الألعاب فقط عبر metadata/config/manifests الموثوقة الخاصة باللانشر.

لا تعمل filesystem guessing.

لكل launcher:

```text
Detect launcher
Detect configured libraries
Parse supported manifest format
Normalize game identity
Resolve icon
Resolve safe uninstall/launcher action
```

إذا format غير موثق أو غير ثابت:

```text
Parse conservatively
Unknown fields => ignore
No destructive action based on guesses
```

## Unified App/Game Model

وسّع الـnormalized model بدون كسر Phase 5.

أضف:

```text
ItemKind:
    Application
    Game

Source:
    Win32
    MSI
    MSIX
    Steam
    Epic
    Xbox
    EA
    Ubisoft
    BattleNet
```

## Deduplication

قد تظهر اللعبة في أكثر من مصدر.

مثال:

```text
Steam game + Registry uninstall entry
```

لا تعرضها مرتين إذا توجد هوية موثوقة تربطهما.

استخدم:

```text
Launcher AppId
Install path
Registered uninstall identity
Executable metadata
ProductCode/package identity
```

لا تدمج فقط بسبب تشابه الاسم.

## Game Icons

اعرض أيقونة اللعبة الحقيقية.

الأولوية:

1. Launcher manifest icon/logo إذا موثوق.
2. Registered application icon.
3. Main executable icon إذا executable موثوقة من manifest.
4. Launcher-specific fallback.
5. Generic F1 game fallback.

لا تنفذ اللعبة للحصول على icon.

## Game Size

إذا launcher يوفر installed size موثوقة:

اعرضها.

إذا لا:

```text
Unknown
```

لا تعمل recursive folder-size scan لكل الألعاب تلقائيًا إذا يبطئ F1.

## UI

استخدم unified list.

أضف Filter صغير:

```text
الكل
البرامج
الألعاب
```

وعند الألعاب يمكن عرض badge صغير:

```text
Steam
Epic
Xbox
EA
Ubisoft
Battle.net
```

بدون ازدحام.

## Game Discovery Tests

أضف fixtures لـ:

```text
Steam default library
Steam secondary library on D:
Steam multiple libraries
Steam valid appmanifest
Steam missing/corrupt appmanifest
Steam duplicate registry entry
Epic valid manifest
Epic missing manifest
Epic stale manifest
Xbox removable package
Xbox protected/framework package
EA valid synthetic manifest
Ubisoft valid synthetic manifest
Battle.net valid synthetic metadata
Launcher not installed
Launcher installed but no games
Duplicate game identities
Malformed manifest
Very large manifest
Unicode game names
```

لا تستخدم ألعاب المستخدم الحقيقية كـdestructive test target.

Real device discovery:

```text
Read-only only
```

---



---

# IMPORTANT — DO NOT CREATE A SEPARATE GAMES SECTION

ممنوع إنشاء صفحة أو قسم مستقل باسم:

```text
Games
الألعاب
Game Library
```

ولا تضف عنصر جديد للـSidebar خاص بالألعاب.

الألعاب يجب أن تظهر فقط داخل:

```text
إزالة البرامج
```

ضمن نفس القائمة الموحدة مع البرامج.

استخدم فلتر بسيط داخل صفحة إزالة البرامج:

```text
الكل
البرامج
الألعاب
```

ويكون مصدر اللعبة ظاهرًا بشكل صغير مثل:

```text
Steam
Epic
Xbox
EA
Ubisoft
Battle.net
```

بدون إنشاء Navigation جديد أو View/Page مستقلة.

الهدف:

```text
One Uninstaller page
One unified list
Apps + Games together
```

مع بقاء إمكانية البحث والفرز والتصفية داخل نفس الصفحة.


# FIX 6 — AUTOMATIC LEFTOVER SCAN AFTER UNINSTALL

بعد نجاح إزالة أي برنامج أو لعبة:

لا أريد المستخدم يضغط:

```text
Scan for leftovers
```

يدويًا.

F1 يبدأ فحص البقايا تلقائيًا بعد أن يتأكد أن التطبيق/اللعبة تمت إزالتها.

Flow الجديد:

```text
User clicks Uninstall
↓
Native / Launcher uninstall runs
↓
F1 verifies item is no longer installed
↓
F1 automatically starts Safe Leftover Scan
↓
Results appear automatically
↓
User chooses:
    Delete selected leftovers
    or
    Keep leftovers
```

## Important

الفحص تلقائي.

**الحذف ليس تلقائيًا.**

لا تحذف أي Leftover بدون موافقة المستخدم.

## Leftover Result UI

إذا لم توجد بقايا:

```text
لا توجد بقايا.
```

إذا توجد:

```text
تم العثور على بقايا
3 عناصر • 128 MB

[ حذف المحدد ]
[ إبقاء ]
```

يمكن عرض `Safe` / `Review` داخل details فقط.

لا تضع paragraph طويل.

## Auto Leftover Scan Safety

احتفظ بكل قواعد Phase 5:

- strong identity evidence
- no name-only deletion
- protected paths
- shared vendor safety
- reparse protection
- registry backup
- no shared runtime deletion
- no launcher shared data deletion
- no arbitrary path
- no arbitrary registry key

## Games Leftovers

لا تعتبر:

```text
Steam
Epic
EA
Ubisoft
Battle.net
launcher root
shared shader cache
shared anti-cheat
shared redistributable
shared mods directory
shared workshop content
```

بقايا خاصة باللعبة بدون ownership evidence قوي.

## Save Games

لا تحذف Save Games تلقائيًا.

أي candidate قد يحتوي:

```text
Save files
Screenshots
Mods
User-generated content
Profiles
Config
```

يكون:

```text
Review
Not selected by default
```

ويظهر label مختصر:

```text
قد يحتوي بيانات شخصية
```

## Launcher Managed Game Removal

بعد uninstall عبر launcher:

تحقق أن manifest/package identity اختفت أو state أصبحت Uninstalled.

لا تعتمد فقط على launcher process exit.

بعدها فقط ابدأ leftover scan.

---

# FIX 7 — AUTO-LOAD / REAL-TIME DATA FROM APP START

لا أريد المستخدم يدخل أي قسم ثم يضغط:

```text
Refresh
Scan
Analyze
Load
```

حتى يرى البيانات الأساسية.

F1 يجب أن يكون:

```text
Open → Data is already there
```

## Core UX Principle

من لحظة تشغيل F1:

```text
Show cached data immediately
+
Refresh real data automatically in background
+
Live telemetry starts automatically
```

الأزرار:

```text
Refresh
Scan
Analyze
```

يمكن أن تبقى كـmanual override فقط، وليست prerequisite.

## Startup Data Orchestrator

أنشئ خدمة مركزية مثل:

```text
IApplicationDataOrchestrator
```

أو اسم مناسب.

مسؤوليتها:

```text
Warm caches
Start live telemetry
Refresh inventories
Schedule low-priority scans
Avoid duplicate work
Cancel on shutdown
```

## Startup Priority

### Tier 1 — Immediate

```text
Theme
Language
Navigation
Dashboard cached snapshot
Performance telemetry
Memory telemetry
```

### Tier 2 — Fast Background

```text
System Information snapshot
Uninstaller inventory
Game launcher inventory
Startup inventory
Tools availability
Memory analysis
```

### Tier 3 — Expensive Background

```text
Cleaner scan
Deep icon hydration
Slow launcher manifests
Non-critical diagnostics
```

لا تنتظر Tier 2/3 قبل إظهار النافذة.

## Cached Snapshots

احفظ آخر نتائج Read-only المناسبة محليًا:

```text
Dashboard summary
System Information static snapshot
Installed apps/games normalized inventory summary
Startup summary
Cleaner last complete scan summary
```

لكن:

- cache versioned
- expiry policy
- corruption-safe
- privacy-safe
- no stale destructive sessions reused

## Startup Experience

اعرض آخر snapshot فورًا مع:

```text
جارٍ التحديث...
```

ثم استبدلها بالبيانات الحديثة.

إذا لا توجد cache لأول تشغيل:

اعرض loading state تلقائيًا، بدون زر مطلوب.

## Dashboard

يظهر تلقائيًا:

```text
CPU
RAM
Disk
Network
System status
Cleaner summary
Installed apps count
Startup count
```

Live metrics تبدأ تلقائيًا.

## System Information

البيانات static تأتي من background warm-up.

إذا provider ما زال يعمل:

اعرض الموجود ثم حدّث section عند اكتماله.

## Performance

من Startup:

```text
PerformanceMonitorService.Start()
```

بـlifecycle صحيح.

عند فتح Performance:

history الحالية تظهر فورًا.

## Memory

Memory metrics وPressure state تعمل تلقائيًا.

Analyze يصبح manual re-check فقط.

## Uninstaller

Inventory يبدأ تلقائيًا ويشمل:

```text
Win32
MSI
MSIX
Games/Launchers
```

## Startup Manager

Startup inventory يبدأ تلقائيًا.

Manageable items تظهر مباشرة.

## Cleaner

استخدم:

```text
last completed scan immediately
+
automatic background rescan
```

بعد startup.

### Cleaner Background Scan

لا تبدأ full scan على UI thread أو قبل ظهور Shell.

ابدأها بعد initial UI is responsive:

```text
Low-priority async
Cancellable
Bounded concurrency
```

إذا المستخدم فتح Cleaner أثناء scan:

اعرض progress الحالي تلقائيًا.

## Auto Rescan Policy

لا تعمل full scan كل دقيقة.

نفذ مثلًا:

```text
On app startup if last complete scan is stale
After successful clean
When user manually refreshes
Optional after significant session time if justified
```

استخدم stale threshold منطقيًا مثل 30-60 دقيقة أو policy مبنية على benchmark.

## Event-Driven Refresh

```text
Uninstall completed -> refresh apps/games automatically
Leftover delete -> refresh relevant summary
Startup enable/disable -> refresh startup automatically
Cleaner clean -> automatic rescan
Settings change -> apply instantly
```

## Window Activation Refresh

عند عودة المستخدم إلى F1:

نفذ cheap freshness check.

إذا البيانات قديمة:

refresh background تلقائيًا.

## No Duplicate Work

لا تسمح لنفس provider يعمل مرتين بسبب startup/page/manual refresh.

استخدم:

```text
single-flight / deduplicated requests
```

## Live vs Background Rules

### Real-time

```text
CPU
RAM
Disk activity
Network
Performance
Memory pressure
```

### Event/Background Refresh

```text
Installed apps
Games
Startup
System Information
Tools
Cleaner
```

لا تعمل 1-second polling لهذه الـinventories.

## Loading UX

ممنوع صفحات تبدأ بـ:

```text
اضغط تحديث
اضغط تحليل
اضغط فحص
```

استخدم تلقائيًا:

```text
جارٍ التحميل...
جارٍ التحديث...
جارٍ الفحص...
```

## Manual Buttons

يمكن إبقاء:

```text
Refresh
Scan now
Analyze now
```

لكن secondary/manual فقط.

## Startup Performance Measurement

قِس:

```text
Time to window visible
Time to first dashboard metrics
Time to usable navigation
Time to apps inventory ready
Time to games inventory ready
Time to startup inventory ready
Time to system info ready
Time to cleaner scan start
Time to cleaner scan complete
```

## Goal

```text
Double-click F1
→ Window appears immediately
→ Dashboard already alive
→ Open any section
→ Data is there
→ Background refresh happens automatically
```

## Resource Budget

راجع:

```text
CPU peak
Disk I/O
Memory
Thread count
Provider concurrency
```

استخدم bounded concurrency، مثل 2-4 heavy providers حسب benchmark.

## Failures

إذا Provider فشل:

اعرض آخر cache + status صغير:

```text
تعذر التحديث
```

مع Refresh manual كخيار.

## First Launch

إذا لا توجد cache:

كل شيء يبدأ تلقائيًا.

لا Wizard.

لا Press Refresh.

## Tests — Auto Load

اختبر:

```text
Cold first launch
Warm launch with cache
Corrupt cache
Stale cache
Provider failure
Slow provider
Navigate before provider completes
Navigate after provider completes
App minimize/restore
App close during background work
App reopen
```

## Auto Refresh Tests

اختبر:

```text
Uninstall -> apps refresh automatically
Game uninstall -> launcher inventory refresh automatically
Leftover scan starts automatically
Leftover deletion remains user-confirmed
Startup disable -> startup list refresh automatically
Cleaner clean -> rescan automatically
Settings -> apply immediately
```

## No Destructive Automation

ممنوع:

```text
Auto clean
Auto delete leftovers
Auto uninstall
Auto disable startup
Auto optimize RAM destructively
```

المسموح:

```text
Auto read
Auto scan
Auto analyze
Auto refresh
```

---

# FIX 8 — CONTINUE AUTONOMOUSLY UNTIL PRE-RELEASE FIXES ARE COMPLETE

أنا لن أكون حاضرًا لمراجعة كل sub-step.

**لا تتوقف بعد كل إصلاح وتنتظر موافقتي.**

نفذ جميع البنود الموجودة في هذا الملف بالتتابع:

```text
Uninstaller fixes
Sidebar F1 Premium removal
Startup Manager filtering/icons
Cleaner scan performance
UI text reduction
Game launcher discovery
Automatic leftover scan
Application auto-load / real-time orchestration
All tests
Builds
Runtime verification
Screenshots
Final report
```

## Do Not Ask for Confirmation Between Subtasks

استمر تلقائيًا من بند إلى التالي.

## Safety Exception

إذا واجهت شيئًا:

```text
unsafe
ambiguous
cannot be verified
would require deleting real user data
would require undocumented destructive behavior
```

نفذ:

```text
SKIP / DEFER
```

وسجل السبب، ثم أكمل باقي البنود.

## Automated Test Policy While Unattended

ممنوع حذف:

```text
real user apps
real games
real saves
real startup entries
real personal files
```

استخدم:

```text
synthetic fixtures
test manifests
test registry
test packages
temporary test files
fake launcher providers
```

Real device:

```text
read-only discovery/scan only
```

## Final Verification

بعد إكمال كل البنود شغّل:

```text
Locked Restore
Debug Build
Release Build
x64 Publish
Targeted Uninstaller Tests
Game Library Tests
Startup Tests
Cleaner Safety
Cleaner Performance Benchmark
Auto-load Tests
Cache Tests
UI Regression
Localization
Targeted Full Regression
Production-like Runtime
```

## Final Runtime Verification

تحقق من:

```text
Dashboard auto-load
System Info auto-load
Performance live
Memory live
Cleaner auto-scan/background refresh
Uninstaller apps + games auto-load
Startup manageable items auto-load
No F1 Premium card
Reduced UI text
Icons present
Automatic post-uninstall leftover scan using synthetic target
```

## Screenshots Required

أخرج screenshots فعلية:

```text
Dashboard
Uninstaller with apps + detected games
Startup Manager with manageable items + icons
Cleaner
System Information
Performance
Memory
Sidebar
```

## Final Report Additions

### Game Discovery

```text
Steam games detected:
Epic games detected:
Xbox games detected:
EA games detected:
Ubisoft games detected:
Battle.net games detected:
Duplicates merged:
Unsupported/unverified:
```

### Automatic Leftover Flow

```text
Synthetic uninstall:
Removal verified:
Auto leftover scan started:
Leftovers found:
User-choice UI shown:
No auto deletion:
```

### Auto-Load

| Data | Startup behavior | Cache | Refresh strategy | Ready time |
|---|---|---|---|---|
| Dashboard | | | | |
| Performance | | | | |
| System Info | | | | |
| Uninstaller | | | | |
| Games | | | | |
| Startup | | | | |
| Cleaner | | | | |
| Memory | | | | |

### Startup Performance

```text
Window visible:
Dashboard live:
Navigation usable:
Apps ready:
Games ready:
Startup ready:
System Info ready:
Cleaner scan started:
Cleaner scan completed:
```

### Resource Usage

```text
Startup CPU peak:
Memory peak:
Background worker count:
No duplicate provider work:
```

## Final Status

لا تبدأ Phase 10.

أكمل كل الإصلاحات والاختبارات والتقرير أولًا.

في النهاية فقط توقف عند:

```text
PRE-RELEASE FIXES COMPLETE — WAITING FOR USER REVIEW
```

ولا تبدأ:

```text
Phase 10
Installer finalization
Signing
1.0.0 Production
```

حتى أراجع النتيجة.
