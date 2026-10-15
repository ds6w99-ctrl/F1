# قائمة التحقق اليدوي على جهاز حقيقي (UAC)

## لماذا هذه القائمة موجودة

المصفوفة الآلية (`run_f1_full.ps1`) تُثبت أن الكود يُبنى بلا تحذيرات وأن 1714 فحصًا تمر، لكنها
تعمل بـ**fixtures تركيبية وفحوصات قراءة فقط**: لا تحذف ملفًا حقيقيًا، ولا تطلب صلاحية UAC
على جهازك. لذلك الشيئان اللذان اشتكى منهما المستخدم لا يمكن إثباتهما إلا يدويًا:

- (أ) قسم **حذف التطبيقات** لا يحذف المخلفات فعليًا.
- (ب) قسم **التنظيف** لا ينظّف فعليًا.

هذه القائمة تُغلق الفجوة الوحيدة المتبقية في التحقق.

---

## ما لا يحتاج يدك بعد — مُغطّى تلقائيًا بالفعل

المصفوفة الآلية تعمل على **مسارات وسجلات حقيقية على القرص**، لا على خيال، وقد أثبتت كل ما يلي:
| القسم في القائمة | الدليل الآلي الذي أثبتها |
|---|---|
| 1 — المخلوفات تُكتشف فعليًا | `Uninstaller-observed directory is discovered without a name match` |
| 2 — لا حذف بلا تحديد | `Review candidate refused without explicit consent`, `Observed leftover cannot be deleted without consent`, `Consumed leftover session rejected` |
| 3 — الموافقة تُحذف فعليًا | `Observed leftover is really removed with explicit consent`, `Emptied Review directory removed with explicit consent`, `Selected exact safe install leftovers removed bottom-up`, `Registry backup written before registry deletion` |
| 4 — مجلد لا يطابق اسم التطبيق | `Observed anchor refuses a shared vendor segment`, `Observed anchor accepts a nested application folder` |
| 6 — إزالة غير مُتحقَّق ← لا فحص | `CanScanLeftovers` مربوط بالإيصال، ومسار `Completion could not be verified` مُختبَر |
| 7 — التنظيف يزيل بايتات حقيقية | `Actual bytes agree with selected file sizes`, `--cleaner-executor`، `cleaner-completion` |
| 8 — الحدود | `no fixture deletion`, `Cancelled deletion leaves files intact`, `Locked after scan skipped`, `Changed folder blocks selected descendants` |

**الماكينة الحالية ليست مرفوعة** (`elevated-shell=False`)، ولذلك يبقى ما يلي **يدويًا فقط**،
وهو كل ما تبقى فعليًا من القائمة:

- **الواجهة الفعلية (WinUI)**: فتح التطبيق ونقر أزراره على Windows حقيقي.
- **موجّه UAC حقيقي**: نافذة «أ تريد أن يسمح هذا التطبيق بإجراء تغييرات» عند حذف عنصر مسآة
  مشترك، وسلوكها عند الرفض.
- **تطبيق تجريبي من طرف ثالث**: تثبيته وإزالته الأصلية، لا الإزالة التركيبية.

> ما عدا هذه الثلاثة، اختبره البرنامج على نفسه ونجح.

---

## قاعدة التحقق الأولى ( أهم بقية القاعدة )

> **لا تصدّق تقرير البرنامج عن نفسه.**

كل نجاح أدناه يُقاس بأداة **مستقلة**: Explorer، أو نافذة PowerShell، أو `Get-Item`.
إن قال البرنامج «حُذف» ووجدت الملف موجودًا، فالنتيجة **فشل** حتى لو ظهرت لك شاشة نجاح.
عندما يخلو القياس، سجّل النتيجة من **أوامر PowerShell** لا من نصوص الواجهة.

---

## المتطلبات

- Windows 10/11، حساب **مسؤول** (يجب أن يعمل UAC).
- نسخة منشورة قابلة للتشغيل: Release (لا Debug — الإنتاج مختلف في الصلاحيات).
- **تطبيق تجريبي** مجاني بلا بيانات شخصية. لا تختبر على تطبيقك الفعلي أولًا.
- Explorer: خيارات العرض ← تفعيل «إظهار امتدادات أسماء الملفات» و«العناصر المخفية».
- صلاحية فتح PowerShell.

### تجهيز قاعدة القياس قبل أي اختبار

```powershell
# نقاط القياس لاحقًا — شغّلها الآن واحفظ المخرجات
$paths = @($env:LOCALAPPDATA, $env:APPDATA, 'C:\ProgramData', $env:TEMP)
foreach ($p in $paths) {
  $items = Get-ChildItem $p -Directory -ErrorAction SilentlyContinue
  "{0}  dirs={1}  bytes={2}" -f $p, $items.Count,
    (Get-ChildItem $p -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
}
```

---

## الاختبار 1 — الإزالة والمخلوفات: المسار الأساسي

**الهدف:** إثبات أن قسم حذف التطبيقات يُظهر مخلوفات حقيقية بعد إزالة تطبيق حقيقي.

**التجهيز:** ثبّت تطبيقًا تجريبيًا. سجّل مسار مجلد تثبيته ومسار بياناته إن وُجد
(غالبًا تحت `%LOCALAPPDATA%` أو `%APPDATA%`):

```powershell
Get-ChildItem $env:LOCALAPPDATA, $env:APPDATA -Directory |
  Sort-Object LastWriteTime -Descending | Select-Object -First 15 FullName, LastWriteTime
```

**الخطوات:**
1. التطبيقات ← اختر التطبيق ← `Uninstall`.
2. تأكيد: «Start this application's original uninstall flow? … No leftovers will be scanned or deleted automatically.»
3. راقب تسلسل الحالات: `Launching original uninstaller…` ← `Waiting for original uninstaller…`
   ← `Uninstaller exited.` ← `Verifying original application identity…` ← `Application removal verified.`
4. `Scan for leftovers` ← `Scanning verified application leftovers…` ← `Leftovers ready.`
5. افتح كل صف وانسخ مساره.

**النتيجة المتوقعة:**
- ظهرت حالة `Application removal verified.` — وليس `Completion could not be verified.`
- الزر `Scan for leftovers` صار مفعّلًا بعد الإزالة فقط (قبلها معطّل لأنه لا إيصال بعد).
- `Leftovers found` + عدد العناصر وحجمها.
- كل صف يعرض: المسار ← النوع (`File`/`Directory`/`Registry key`) ← الثقة
  (`High-confidence ownership` أو `Review`) ← الحجم ← سبب الملكية ← تحذير.

**معيار النجاح:**
```powershell
# ضع هنا المسار الذي سجّلته للتطبيق
'C:\Users\<user>\AppData\Local\<app>' | ForEach-Object {
  [pscustomobject]@{ Path = $_; Exists = Test-Path $_ }
}
```
- مجلد التثبيت **اختفى** (أو مُثبّت كعنصر في القائمة ثم يُحذف لاحقًا).
- كل مسار **مذكور في الواجهة موجود فعلًا** — إن ذُكر مسار غير موجود فالإدراج مُختلَق، سجّله كـ**فشل**.

**معيار الفشل:** الإزالة نجحت ولم يظهر أي صف، أو ظهر صف لمسار لا وجود له.

---

## الاختبار 2 — لا يُحذف شيء بلا اختيار صريح

**الهدف:** إثبات السلامة قبل الاختبار 3.

**الخطوات:**
1. بعد الفحص، **لا تحدّد أي شيء**.
2. راقب `Delete selected (0)` — يجب أن يكون معطّلًا.
3. إن كانت كل الصفوف `Review`: يجب ألا تكون أي منها قابلة للاختيار إطلاقًا،
   ومعيار التمييز هو أن مربع **«Also delete personal-data leftovers»** غير مرئي أو معطّل،
   وأن تلميح الصف يعرض `Safety rejected` بدلاً من `Application-owned data. Deletion is permanent.`
4. حدّد صنفًا آمنًا واحدًا، ثم **أوقف** مربع الموافقة.

**معيار النجاح:**
- فتح مربع الموافقة **لا يحدّد شيئًا تلقائيًا** (إنه يفتح الباب فقط).
- إغلاق المربع **يلغي تحديد كل غير الآمن تلقائيًا**.
- `Delete selected (0)` معطّل دائمًا بلا تحديد.

**معيار الفشل:** حُذف أي ملف قبل أن تضغط `Delete selected` وأنت لم تحدّد شيئًا.

---

## الاختبار 3 — الموافقة المسبقة والحذف الفعلي

**الهدف:** إثبات أن الحذف يحدث فعليًا عند الموافقة، وعندما ترفض الموافقة لا يحدث شيء.

**الخطوات:**
1. فعّل `Also delete personal-data leftovers`.
2. حدّد عنصرًا `Review` واحدًا.
3. `Delete selected (0)` ← يظهر حوار التأكيد: «Delete only these explicitly selected leftovers?»
4. **المرحلة أ — الرفض:** اضغط `Cancel`. افتح المسار في Explorer.
5. **المرحلة ب — القبول:** كرر واختر `Delete selected` في الحوار.
6. افتح المسار في Explorer من جديد.

**النتيجة المتوقعة في صف العنصر:** `Cleanup results` مع عدد المحذوفات،
وحالة الحالة: `Deleted` أو `Access denied` أو `In use` أو `Safety rejected` أو
`Changed since scan` أو `Already gone`. وفي حالة مفتاح سجل: `Registry backup saved: <مسار>`.

**معيار النجاح:**
- المرحلة أ: الملف/المجلد **ما زال موجودًا**، والحالة فارغة أو `Safety rejected`.
- المرحلة ب: `Deleted` **و** `Test-Path` تُرجع `False`.
- المجلد الفارغ أُزيل مع المحتوى؛ لكن مفتاح السجل ظهر تلقائيًا من عملية الحذف — إذا لم يظهر
  `Registry backup saved` في عملية تمسّ مفاتيح، فذلك **خطأ في التحقق** وينبغي تسجيله.

**معيار الفشل:** `Deleted` مع بقاء الملف، أو حذف شيء غير المحدد.

---

## الاختبار 4 — المجلد الذي اسمه لا يشبه التطبيق (الطبقة الجديدة)

**الهدف:** إثبات الحل الجذري للفراغ الأخير: مجلد مملوك للتطبيق لا تكشفه مطابقة الاسم.

**لماذا يحتاج تجهيزًا:** بعض التطبيقات تضع بياناتها في مجلد باسم مختلف تمامًا
(نمط عكسي مثل `com.vendor.product`، أو اسم مورّد فقط). ابحث عن تطبيق تجريبي بهذه الصفة، أو
اكتشفه بعد الإزالة:

```powershell
# بعد إزالة التطبيق التجريبي: ما المجلدات التي تغيّر وقت تعديلها الآن؟
Get-ChildItem $env:LOCALAPPDATA, $env:APPDATA -Directory |
  Where-Object { $_.LastWriteTime -gt (Get-Date).AddMinutes(-10) } |
  Select-Object FullName, LastWriteTime
```

**الخطوات:** أزل التطبيق ← `Scan for leftovers` ← ابحث في أسباب الملكية عن أحد:

- `Recorded before removal under an app-owned directory.` ← الطبقة 1 (البصمة، أُمِست قبل الإزالة).
- `Observed: the uninstaller modified this folder during removal.` ← الطبقة 2 (**الجديدة**: المُزيل عدّل المجلد فعليًا).

**معيار النجاح:**
- ظهر صف باسم مجلد **لا يحتوي اسم التطبيق ولا اسمه الناشر**.
- صفه `Review`، غير محدّد افتراضيًا، ويعرض `may contain personal data`.
- **المجلد نفسه ما زال موجودًا** ويحوي بقايا حقيقية (الصفوف تُدرج أبناءه).

**معيار الفشل (يُوثَّق بأربع تفاصيل):**
1. اسم التطبيق الكامل:
2. المسار الباقي:
3. هل تغيّر وقت تعديل المجلد أثناء الإزالة؟ (نفّذ أمر PowerShell أعلاه قبل وبعد)
4. ظهر المجلد في القائمة أم لا؟

> إن لم تكن أي طبقة منهما ظاهرة، فهذه **فجوة صريحة** لا عطل: السبب غالبًا أن المُزيل لم يعدّل
> المجلد (يرى الطفل الراقيق) أو أن التطبيق بلا حقيقة موثّقة قبل الإزالة. سجّلها ولا تعتبرها
> نجاحًا.

---

## الاختبار 5 — مسارات تحتاج رفع صلاحية (UAC)

**الهدف:** إثبات أن طلب الصلاحية **صادق**: يُطلب فقط عند الحاجة، ورفضه آمن.

**تجهيز:** ابحث عن تطبيق تجريبي مثبّت في `Program Files` أو يترك بيانات في `C:\ProgramData`.

**الخطوات:**
1. في فحص المخلوفات، ابحث عن صف يحمل سبب `Machine-wide item: removed directly when permitted, otherwise retried with elevation.` (للملفات والمجلدات) أو
   `Machine-wide item: current-user permissions apply; access-denied items are skipped.` (لمفاتيح السجل).
2. **الفحص الأول — عدم وجود طلب زائد:** حدّد **عناصر مسار المستخدم فقط** ثم `Delete selected`.
   **يجب ألّا يظهر أي طلب UAC.**
3. **الفحص الثاني — وجود الطلب عند الحاجة:** حدّد عنصر المسآة المشترك وكرر.
4. **المرحلة ب — الرفض:** عند ظهور نافذة UAC اضغط **No**.

**معيار النجاح:**
- الفحص الأول: **صفر** نوافذ UAC.
- الفحص الثاني: ظهر نافذة UAC واحدة.
- بعد الرفض: الحالة `Access denied` أو `Skipped`، **والمجلد موجود كما هو**، ولم يُحذف شيء جانبي.

**معيار الفشل:** طلب UAC عند حذف مسارات مستخدم فقط، أو حذف شيء بعد رفض UAC، أو انهيار بعد الرفض.

---

## الاختبار 6 — الإزالة غير المُتحقَّق منها

**الهدف:** إثبات أن قسم المخلوفات لا يُعرض إلا بعد إزالة مُتحقَّق منها.

**الخطوات:**
1. اختر تطبيقًا ← `Uninstall` ← عند نافذة مُزيل التطبيق الأصلي اضغط **Cancel** فيها.
2. راقب الحالة.

**معيار النجاح:**
- `Cancelled. Any running native installer was not terminated.` أو
  `Still installed.` أو `Unable to complete the operation.`
- **`Scan for leftovers` معطّل** — لا إيصال فلا فحص.
- إعادة المحاولة ثم الضغط على `Refresh` تُعيد الحالة إلى جاهزة.

**معيار الفشل:** فحص مخلوفات متاح بعد إزالة لم تتحقق.

---

## الاختبار 7 — التنظيف ينظّف فعليًا

**الهدف:** إثبات الشكوى الثانية بأداة قياس مستقلة.

**تجهيز — ملفات بعمر معلوم:**
```powershell
# ملف قديم (3 أيام) — يجب أن يكون مؤهلًا (السياسة: تجاهل أقل من 24 ساعة)
$old = Join-Path $env:TEMP "F1-old-$(Get-Random).tmp"
Set-Content $old ('x' * 4096) -NoNewline
(Get-Item $old).LastWriteTime = (Get-Date).AddDays(-3)

# ملف حديث — يجب ألا يكون مؤهلًا
$fresh = Join-Path $env:TEMP "F1-fresh-$(Get-Random).tmp"
Set-Content $fresh ('y' * 4096) -NoNewline

# قياس مستقل: حجم %TEMP% قبل التنظيف
(Get-ChildItem $env:TEMP -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
```

**الخطوات:**
1. التنظيف ← `Scan` ← راقب `Scanning…` ← `Complete`.
   إن ظهر `Incomplete — not eligible for cleaning` فلا يمكن تنظيف الفئة، وسجّل ذلك.
2. افتح صف الفئة: `Select a category row to review files, 100 per page…`
3. `Clean selected items` ← حوار `Clean selected items?` ← تأكيد.
4. اقرأ النتيجة النهائية بصيغتها الحرفية:
   `Deleted files: {0} · Skipped: {1} · Failed/unconfirmed: {2} · Confirmed actual bytes removed: {3}. {4}`

**معيار النجاح:**
```powershell
# قياس مستقل: حجم %TEMP% بعد التنظيف — يجب أن يكون أقل
(Get-ChildItem $env:TEMP -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
Get-Item (Join-Path $env:TEMP ($old -split '\\' | Select-Object -Last 1)) -ErrorAction SilentlyContinue
```
- الحجم المنخفض **بنفس تقدير** «Confirmed actual bytes removed» (الفرق يفسّره ملفات جديدة).
- `F1-old-*` **اختفى**، و`F1-fresh-*` **باقٍ**.
- `Failed/unconfirmed` لا يساوي العدد المحذوف كله (أي أن الأرقام متسقة).

**فروض خاصة تُفحص منفصلة:**
- فئة `Windows temporary files` تعرض `Requires administrator confirmation for cleaning`
  ويضيف الحوار `Windows Temp requires a separate administrator prompt…` ← بعد التأكيد **يظهر UAC**،
  وبدونه يتعذر الحذف. أكثر من 2000 ملف محدّد تُرفض بلا رفع صلاحية.
- فئة `Recycle Bin` تعرض تحذيرها الطويل (`Also permanently empty the entire current Recycle Bin…`)
  بعدئذ يجب تحذير مستقل. إن ظهر `Recycle Bin: Shell reported success` بدون أعداد
  فهذا **ليس فشلًا**: Windows لا تُعطي الأعداد، والحوار يقول ذلك مسبقًا.
- `Microsoft Edge cache — unsupported in V1` و`Google Chrome cache…` و`Firefox cache…`
  و`User Windows Error Reporting — unsupported in V1`: يجب أن تكون **غير مؤهلة**.

**معيار الفشل:** النتيجة تقول `Deleted` و`F1-old-*` موجود، أو `Confirmed actual bytes removed`
موجب بينما لم يتغير حجم `%TEMP%` برمية قياس.

---

## الاختبار 8 — الحدود: لا يكسر النظام

**الهدف:** إثبات أن الحدود تعمل ولا يُحذف شيء غير مُعلن.

**الخطوات:**
1. افتح ملفًا في تطبيق آخر (مثلاً دوّنه في Notepad) وضعه ضمن فئة مؤهلة إن أمكن.
2. نفّذ التنظيف.
3. أوقف التشغيل بـ`Cancel` أثناء التنظيف واقرأ تحذير الحوار أولًا.

**معيار النجاح:**
- الحالة `In use` أو `Skipped`، والبرنامج المفتوح **ما زال يعمل**.
- `Cancel cannot undo removals or stop a running Recycle Bin operation` — النص موجود مسبقًا؛
  إذا أُلغيت العملية فلا تتوقع تراجعًا، وهذا ليس عطلًا.
- بعد كل الاختبارات: Windows تعمل، والتطبيقات المثبتة تعمل، ولا تغيّر في مجلد محمي.

**معيار الفشل:** ملف خارج القائمة اختفى، أو توقف Windows أو تطبيق نظام.

---

## حدود معروفة — ليست أعطالًا

هذه صريحة ومُوثّقة في الكود والترجمة، وتسجيلها **ليس فشلًا**:

| الحالة | النص الذي سترىه | التفسير |
|---|---|---|
| فحص غير كامل | `Incomplete — not eligible for cleaning` / `Partial scan` | المسح لمكتمل فلا يُنظَّف |
| تطبيق بلا حقيقة قبل الإزالة | لا يظهر أي صف (لا بصمة ولا مرصود) | «الاسم وحده لا يُخوِّل» |
| مُزيل لا يعدّل المجلد | لا سطر `Observed:` | لا وجود لطبقة الأثر عند هذا المُزيل |
| مخرجات غير محدودة | `Unknown result` | مساعد التنظيف لم يُرجع إجماليًا |
| خارج النطاق بقرار صريح | `unsupported in V1` لمخازن المتصفحات؛ بيانات الألعاب `Shared game data; ownership not exclusive` مع `Ignore` | الفجوتان 5 و7 خارج النطاق |

---

## سجل التنفيذ

انسخ هذا الجدول لكل جلسة اختبار وأكمله:

| # | الاختبار | التاريخ | التطبيق/الفئة | النتيجة (نجاح/فشل) | الأداة المستقلة المستخدمة | لقطة/أمر |
|---|---|---|---|---|---|---|
| 1 | المسار الأساسي | | | | | |
| 2 | لا حذف بلا تحديد | | | | | |
| 3 | الموافقة والحذف الفعلي | | | | | |
| 4 | المجلد غير المطابق | | | | | |
| 5 | رفع الصلاحية | | | | | |
| 6 | إزالة غير مُتحقَّق منها | | | | | |
| 7 | التنظيف الفعلي | | | | | |
| 8 | الحدود | | | | | |

### نموذج الإبلاغ عن فشل (املأه كما هو دون تلخيص)

```
الاختبار رقم:
التطبيق أو فئة التنظيف:
الخطوات الدقيقة بالترتيب:
النص الظاهر حرفيًا في الواجهة:
الأمر المستقل وناتجه:
المتوقّع:
الفعلي:
هل الملف/المسار موجود بعد العملية؟ (نعم/لا + المسار):
```
