# Publish to GitHub — Fika Weapon Pickup Fix

**Статус:** `ready`  
**GitHub:** Release + zip  
**Версия:** `1.5.2`  
**Deployment:** `(client_only)`

## 1. Подготовка (уже сделано этим скриптом)

Папка: `github-repos/FikaWeaponPickupFix/`

## 2. Создать репозиторий и запушить

```powershell
cd github-repos/FikaWeaponPickupFix
git init
git add .
git commit -m "Source backup Fika Weapon Pickup Fix v1.5.2"
git branch -M main
git remote add origin https://github.com/kabzon93region/FikaWeaponPickupFix.git
git push -u origin main
```

Или автоматически:

```powershell
python CURSORAIMODING/tools/publish/publish_github_release.py FikaWeaponPickupFix --create-repo
```

## 3. GitHub Release

Прикрепить zip (только игровые файлы, без INSTALL.md):

`\\Servant\data\Games\EscapeFromTarkov4\CURSORAIMODING\releases\FikaWeaponPickupFix_v1.5.2_2026-07-12.zip`

```powershell
gh release create v1.5.2 "\\Servant\data\Games\EscapeFromTarkov4\CURSORAIMODING\releases\FikaWeaponPickupFix_v1.5.2_2026-07-12.zip" ^
  --title "Fika Weapon Pickup Fix v1.5.2" ^
  --notes-file CHANGELOG.md
```

## Описание репозитория (suggested)

Фикс сломавшихся рук при подборе оружия с мёртвых ботов в Fika coop.

SPT 4.0 + Fika 2.3 headless stack. Deployment: `(client_only)`.
