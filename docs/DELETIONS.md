# سجل الحذف — فرع `pre-release-fixes`

وثيقة دائمة: كل ما حُذف من المستودع أثناء عمل هذا الفرع، وطريقة إرجاعه بالضبط.
تُحدَّث في الالتزام الذي يرافق كل حذف جديد.

- الفرع: `pre-release-fixes` — الالتزام الأحدث: `1baeb78`
- **لا يوجد remote مُهيّأ**؛ العمل محلي بالكامل، فلا يوجد شيء «مُرفوع» يمكن المطالبة به من مكان آخر.
- المرجعية الأساسية `main` = `c86a24cca58f05ad0b965e2366558b26488da46f`

---

## 1. القاعدة العامة

لم يُحذف أي شيء نهائيًا. كل حذف مسجَّل داخل التاريخ (history)، وكل التزام حذف شيء محفوظ كما هو.
طالما الفرع موجود، فالاسترجاع مضمون.

الشيء الوحيد الذي يمكن أن يضيع فعلًا هو **المرجعية** (branch أو tag) إذا حُذفت ثم شُطبَت كائناتها بـ`git gc`.
لقطة المرجعيات في القسم 5 تسجّل كل SHA-1؛ تستطيع إرجاع أي مرجع بنفس الـSHA طالما لم تُشطب الكائنات.

```powershell
git gc --prune=now   # لا تشغّل هذا
git prune             # ولا هذا
git branch -D <name>  # سجّل الـSHA في القسم 5 أولًا
```

---

## 2. ما حُذف فعلًا — التزامان فقط

| الالتزام | التاريخ | عدد الملفات | المحتوى |
|---|---|---:|---|
| `486236a` | 2026-10-15 | 1 | ملف مصدر إنتاجي واحد: `src/SystemCore.App/CompatibilityService.cs` |
| `b39ecdb` | 2026-10-15 | 4268 | أدلة المراحل التاريخية + سكربتات مؤقتة + وثائق مرحلية قديمة |
| **المجموع** | | **4269** | |

لا يحذف أي التزام آخر أي ملفًا في هذا الفرع.

**`b39ecdb` لا يحذف أي ملف مصدر إنتاجي.** الأسماء الخمسة من نوع `.cs` التي حذفها كانت كلها
نسخًا داخل `docs/archive/evidence/**` — لا شيء منها مصدر تشغيل:

| الملف المحذوف |
|---|
| `docs/archive/evidence/pre-release-03/baseline/WindowsCleanerScanner.cs` |
| `docs/archive/evidence/debug/before-CleanerView.generated.cs` |
| `docs/archive/evidence/debug/HeapGraph/Program.cs` |
| `docs/archive/evidence/debug/NativeHeapTrace/Program.cs` |
| `docs/archive/evidence/debug/NativeHeapTrace/Comparison.cs` |

### تفكيك `b39ecdb` (4268 ملفًا)

| المجموعة | العدد | الوصف |
|---|---:|---|
| `docs/archive/evidence/**` | 4108 | لقطات شاشة، سجلات، JSON، ZIP لمرحل 8/9 وpre-release |
| `docs/archive/phases/**` | 20 | تقارير `PHASE-0` … `PHASE-9` |
| `scripts/*.ps1` | 117 | سكربتات مرحلية أحادية الاستعمال (`Invoke-Phase*`, `Test-F1*`, `Write-Phase*`) |
| `docs/*` (خارج الأرشيف) | 18 | `CURRENT-STATUS`, `GIT_WORKFLOW`, `SBOM`, خرائط التبعيات… |
| `docs/archive/README.md` | 1 | |
| `.serena/**` | 2 | إعدادات أداة سابقة |
| جذر المستودع | 2 | `TEST-RESULTS.md`, `01_F1_Setup_Git.ps1` |

---

## 3. أوامر الاسترجاع

### 3.1 ملف واحد

```powershell
cd G:\F1
git checkout 486236a^ -- src/SystemCore.App/CompatibilityService.cs
```

الرمز `^` ضروري: التزام `486236a` هو من حذف الملف، فمحتواه موجود في **والده**.

حذف هذا الملف جرى مع إزالة سطر تسجيل واحد في `App.xaml.cs`:
`registrations.AddSingleton<CompatibilityService>();` — أُزيل من بعد
`registrations.AddSingleton<LocalizationService>();`.

لإرجاعه يعمل القسم:
```powershell
git checkout 486236a^ -- src/SystemCore.App/CompatibilityService.cs
# ثم أعِد السطر التالي بعد سطر LocalizationService في src/SystemCore.App/App.xaml.cs:
#     registrations.AddSingleton<CompatibilityService>();
```

**ملاحظة صريحة:** `CompatibilityService` كانت طبقة اكتشاف قدرات النظام (إصدار Windows،
المعمارية، توفر Mica) ولم تكن مستخدمة فعليًا وقت الحذف، ونماذجها الأساسية باقية اليوم في
`src/SystemCore.Core/Compatibility/FeatureCapabilities.cs` (`CompatibilityRules`،
`FeatureCapabilities`). لذلك إرجاع الملف وحده **يكفي للترجمة** ولا يحتاج إرجاع أي ملف آخر.

إن أردت إرجاع السلوك كاملًا (بما في ذلك التغييرات الأخرى في ذلك الالتزام):
```powershell
git revert --no-edit 486236a
```
قد يتعارض مع لاحق من الالتزامات ويحتاج حلًّا يدويًا.

### 3.2 كل ما حذفه `b39ecdb`

```powershell
cd G:\F1
git revert --no-edit b39ecdb
```
يستعيد الـ4268 ملفًا في التزام جديد. إن ظهر تعارض، فالمحتوى كاملًا في `b39ecdb^`.

استرجاع مجموعة محددة فقط:
```powershell
git checkout b39ecdb^ -- docs/archive/phases
git checkout b39ecdb^ -- scripts/Test-UninstallerPage.ps1
git checkout b39ecdb^ -- docs/PRE-PHASE10-VERIFICATION.md
```

### 3.3 إرجاع التزامين معًا

```powershell
git revert --no-edit b39ecdb
git revert --no-edit 486236a
```
الأحدث أولًا (هذا هو الترتيب الصحيح: `b39ecdb` أحدث من `486236a`).

---

## 4. إعادة توليد القائمة كاملة

كل ما سبق مشتق من أمر واحد، وهذه الوثيقة ليست بديلاً عنه:
```powershell
git log main..pre-release-fixes --diff-filter=D --name-status --pretty=format:'@@%h|%s'
```
يُعيد سطرًا لكل ملف محذوف متبوعًا بالتزامه. للاسترجاع المباشر من هذا المخرج:
```powershell
git diff-tree --no-commit-id --name-only -r b39ecdb --diff-filter=D | ForEach-Object { git checkout b39ecdb^ -- $_ }
```

---

## 5. لقطة المرجعيات (الجزء الوحيد القابل للضياع)

التقطت عند `1baeb78`:

| المرجع | SHA-1 |
|---|---|
| `main` | `c86a24cca58f05ad0b965e2366558b26488da46f` |
| `pre-release-fixes` | `1baeb7802d917bae21d23f5434564df5ba147380` |
| `pre-phase10-baseline` (وسم) | `833fb38b7ec186d52936cb4de45abf7fcebe92c8` |
| `oce-backup/fix2-20260926` | `62a0af76871d7c94c8c4c69eaee5788d593f6ef1` |
| `oce-backup/fix3-20260926` | `1e952f49d8e5eea154c818acbbd41b9c355d6885` |
| `oce-backup/fix4-copy-polish` | `30e4b841ff5767ddd30682f3eb28ee9e91fe4a2a` |
| `oce-backup/fix5-20260926` | `a19bcf79a74dc81d32b1b71fe4fe3cf8929a6294` |
| `oce-backup/fix6-20260926` | `a1adb63dcd6d4d482919a73fe3787cd6bd465d55` |
| `oce-backup/fix7-20260927` | `5e210631db6b6d2b873ebd436b56c5cab7d5b5f1` |
| `oce-backup/fix7-task2-wip` | `53a4d6ed59fdf00066454f41e5955beb7ec7c3f9` |

إرجاع مرجع بعد حذفه:
```powershell
git update-ref refs/tags/pre-phase10-baseline 833fb38b7ec186d52936cb4de45abf7fcebe92c8
git update-ref refs/heads/oce-backup/fix2-20260926 62a0af76871d7c94c8c4c69eaee5788d593f6ef1
```

---

## 6. ما لم يُحذف بعد — وبَقِي عمداً

| البند | الحالة |
|---|---|
| فروع `oce-backup/*` (سبعة) | موجودة |
| الوسم `pre-phase10-baseline` | موجود |
| `main` | لم يُمسّ |
| `bin` / `obj` / `artifacts` | مخرجات بناء غير مسجّلة في Git؛ تنظيفها آمن في أي وقت |
| الملفات الأربعة خارج النطاق بقرار صريح | حذف مجلدات الكاش (الفجوة 5)، مخلفات الألعاب (الفجوة 7) — لم تُنفَّذ ولم يُحذف بسببها شيء |
