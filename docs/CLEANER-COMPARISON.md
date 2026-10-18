# مقارنة: قسم «التنظيف» في F1 مقابل BleachBit

> المرجع: [bleachbit/bleachbit](https://github.com/bleachbit/bleachbit) — الدفاتر في `cleaners/*.xml`.
> المصدر الفعلي المقروء: `windows_explorer.xml`، `internet_explorer.xml`، `deepscan.xml`،
> وقائمة `cleaners/` الكاملة عبر GitHub API.
> كود F1: `src/SystemCore.Infrastructure/Cleaner/*`، `CleanerViewModel.cs`.

## ما تنظّفه BleachBit على Windows (مِمَّا قرأناه فعليًا)

| المكوّن | الخيارات |
|---|---|
| **Windows Explorer** | MRU (UserAssist + ComDlg32 + RunMRU…)، المستندات الأخيرة + Jump Lists، Run، سجل البحث، Shellbags، **Thumbnails** |
| **Internet Explorer** | Cookies، نماذج، History (+ TypedURLs)، Cache، سجل التنزيلات، Logs |
| **Deep scan** | `.angular`، Backup (`*.bak`/`*~`)، `.DS_Store`، `node_modules`، `venv`، `__pycache__`، `Thumbs.db`، Temp (`~wr*.tmp`، `ppt*.tmp`)، ملفات VIM swap |
| تطبيقات | Brave، Chromium/Chrome، Firefox، Thunderbird، VLC، VSCode، Discord، Zoom، WinRAR… |

## ما تنظّفه F1 الآن

`UserTemp`، `WindowsTemp`، `ShaderCache (D3DSCache)`، `CrashDumps`، `Thumbnails`.
(كاش المتصفحات وWER **معطّلة بقرار V1**، F1-002.)

## الفجوات الحقيقية

| الفجوة | الحكم |
|---|---|
| **رواسب Explorer** (MRU، Recent، Run، Search، Shellbags، Jump Lists) | **الأهم**: بيانات خصوصية حقيقية تُنظَّف أمنيًا في HKCU فقط |
| **Deep scan** (bak، Thumbs.db، pycache، node_modules…) | قيمة عالية لكن **بطيء** ويكتشف ملفات المستخدم؛ يحتاج وضعًا صريحًا ومراجعة |
| **كاش المتصفحات** | F1 يعطّلها بقرار V1 — BleachBit ينظّفها. |
| **تطبيقات الطرف الثالث** (VLC، VSCode…) | خارج نطاق F1 بقرار معماري (لا حذف بيانات تطبيقات غير معروفة). |

## قرار التصميم — «ما لا يمكن تنظيفه لا يظهر»

طلبك: **الخيارات غير القابلة للتنظيف لا تظهر للمستخدم**. لذلك:

1. كل هدف (target) جديد يُفحص قبل الدخول إلى الصفوف.
2. الصف الذي لا يوجد له أي مسار/مفتاح موجود على الجهاز **يُخفى تمامًا** (لا يظهر معطّلًا ولا فارغًا).
3. الصف الذي يظهر يجب أن يكون قابلاً للتنظيف فعلاً: مسار موجود + داخل جذر مسموح + يمر ببوابة `IsAllowed`.
4. `Unavailable` لا يُستعمل بعد الآن لعرض «غير مدعوم» — الصف يختفي.

## التنفيذ (هذه الدفعة)

- إضافة فئة `ExplorerHistory` (سجل Explorer) — **HKCU فقط**، حذف مفاتيح محددة بالاسم، لا حذف مفاتيح أب.
- `Thumbs.db` ضمن فحص المستخدم (ملفات `Thumbs.db` بعد حد عمر).
- إخفاء أي صف بلا هدف موجود (`Available == false`) بدل عرضه.
- v1 لا يشمل Deep scan (بطيء ومخاطره أعلى) — موثّق كخيار مستقبلي.
