# F1 — Git Workflow for OpenCode

## الهدف
من الآن فصاعدًا Git هو شبكة الأمان الأساسية للمشروع.

آخر baseline قبل Phase 10 يجب أن يكون عليه tag:

```text
pre-phase10-baseline
```

## قبل أي تعديل كبير

نفذ:

```powershell
git status
git log -5 --oneline
```

لا تبدأ تعديل كبير إذا هناك تغييرات مجهولة غير محفوظة.

## لكل Feature / Fix

استخدم branch واضحة:

```powershell
git switch -c fix/startup-manager
```

أو:

```powershell
git switch -c feature/phase10
```

بعد التعديل:

```powershell
git status
git diff
```

ثم Build/Tests.

إذا نجحت:

```powershell
git add .
git commit -m "fix: improve startup manager filtering and icons"
```

## لا تعمل

```text
git reset --hard
git clean -fd
git checkout -- .
```

إلا بموافقة صريحة من المستخدم، لأنها قد تمسح العمل.

## قبل Phase 10

تأكد من:

```powershell
git status
git tag --list
git log --oneline --decorate -10
```

ويجب أن يكون baseline موجودًا:

```text
pre-phase10-baseline
```

## إذا خرب تعديل جديد

أسهل مقارنة:

```powershell
git diff pre-phase10-baseline..HEAD
```

ولا ترجع تلقائيًا بدون مراجعة الفرق.

## OpenCode

في بداية كل محادثة جديدة:

1. اقرأ `docs/NEW_CHAT_START_HERE.md`
2. نفذ `git status`
3. نفذ `git log -5 --oneline`
4. اقرأ آخر commit / branch
5. أكمل من الحالة الفعلية للمستودع
6. لا تعيد العمل المنجز

## Remote Backup

إذا تم ربط GitHub:

```powershell
git push
```

بعد كل milestone مهم.

لا ترفع:
- build outputs
- logs
- dumps
- secrets
- signing private keys
