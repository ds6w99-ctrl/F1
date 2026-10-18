# مقارنة: أدوات الذاكرة في F1 مقابل Windows Memory Cleaner (WMC)

> المرجع: [IgorMundstein/WinMemoryCleaner](https://github.com/IgorMundstein/WinMemoryCleaner) — README الكامل مقروء.
> كود F1: `Core/MemoryTools/*`، `Infrastructure/MemoryTools/*`، `App/MemoryTools/*`.

## ما تفعله WMC فعليًا (8 عمليات عبر واجهات Windows الأصلية)

| العملية | API | Windows |
|---|---|---|
| Combined Page List | `NtSetSystemInformation` | 8+ |
| Modified File Cache | `SetSystemFileCacheSize` + `FlushFileBuffers` | XP+ |
| Modified Page List | `NtSetSystemInformation` | Vista+ |
| Registry Cache | `NtSetSystemInformation` (RegistryReorganize) | 8.1+ |
| Standby List | `NtSetSystemInformation` | Vista+ |
| Standby List (low priority) | `NtSetSystemInformation` | Vista+ |
| System File Cache | `SetSystemFileCacheSize` | XP+ |
| Working Set | `EmptyWorkingSet` على كل العمليات (مع قائمة استثناء) | XP+ |

## ما يفعله F1 حاليًا

F1 **لا يستدعي أي API تحرير** — قرار تصميمي موثّق: «لا cache إنتاجية ثبتت أهليتها للتحرير المستقل»، فتظهر النتيجة `NoAction` دائمًا. الآلية موجودة (`ISystemCoreMemoryCacheOwner`) لكن **لم يُسجَّل أي مالك في الإنتاج**.

## الفجوات

| # | الفجوة | الأثر |
|---|---|---|
| 1 | **لا تحرير فعلي للذاكرة** | الفئة كلها عرض فقط — أكبر فرق مع WMC |
| 2 | قائمة استثناء عمليات | لا توجد؛ WMC يحمي العمليات الحرجة |
| 3 | جدولة تلقائية (كل X ساعة / عند انخفاض RAM) | F1 يرفض الخدمات الخلفية عمدًا |
| 4 | إشعارات + منطقة النظام | F1 بلا tray عمدًا |
| 5 | سطر أوامر للتحسين | لا يوجد |
| 6 | مقارنة قبل/بعد عبر Resource Monitor | F1 يعرض المقاييس لكن لا يحرّر |

## قرار التنفيذ (هذه الدفعة)

**نفعّل تحريرًا حقيقيًا محدودًا وآمنًا**، مع الحفاظ على مبدأ F1: لا إجراء بلا أساس.

- **Standby List (low priority)** أولًا: أقل خطرًا وموثّقة (Vista+).
- **Standby List (كامل)** كخيار منفصل مع وصف صريح للثمن (إبطاء إقلاع التطبيقات مرة أخرى).
- **Working Set**: عبر `EmptyWorkingSet` على العمليات المكتبية فقط، **مع استثناء إلزامي** لعمليات لا يجوز لمسها (النظام، F1 نفسه، وكل عملية يختارها المستخدم).
- **Modified Page List / Combined Page List / Registry Cache / File Cache**: تُعرض في الكود بخريطة توثيق الـAPI، لكن **لا تُنفَّذ في هذه الدفعة** إلا إذا كان الجهاز يدعمها وكان المستخدم اختارها صراحةً — تنفيذها يحتاج امتياز `SeProfileSingleProcessPrivilege` الذي يملكه F1 عبر مساعد مرتفع الصلاحيات فقط.
- **قائمة الاستثناءات** تُطبَّق على Working Set إلزاميًا.
- **سطر أوامر للتحسين** يُضاف للـCLI: `/standby`, `/standby-low`, `/workingset` مع رفض أي شيء غيرها.
- **الإشعار/الجدولة/الـtray**: تبقى خارج النطاق بقرار سابق (F1 بلا خلفية مستمرة).
