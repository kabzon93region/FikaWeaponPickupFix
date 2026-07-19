## 1.6.0 (2026-07-19)

- **Fixed: patch target corrected.** v1.5.2 patched `Player.Proceed` (base class) which is NEVER called because `FikaPlayer` overrides it without calling `base.Proceed()`. Now patches `FikaPlayer.Proceed` directly — this is the actual method that fires for local player weapon equip.

## 1.5.2 (2026-07-13)

- Added prefix + postfix logging for diagnosis of "broken hands" root cause.

## 1.5.1 (2026-07-12)

- **Proper hands controller disposal on weapon pickup:** Previously, orphaned bot `FirearmController` instances were only null'd via reflection, leaving internal state (firing loops, weapon references, animation subscriptions) alive. This caused "broken hands" when equipping a weapon that was still referenced by a dead bot's controller. Now follows the canonical `Player.OnDead()` cleanup sequence: `OnPlayerDead()` → `FastForwardCurrentState()` loop → `Destroy()` → Unity `Object.Destroy()` → null. All steps are individually wrapped in try/catch since a dead bot's controller may already be partially cleaned up.

## 1.4.0 (2026-07-10)

- **Removed duplicate `FikaBot.OnDead` hook:** `BotDeadCleanupPatch` deleted — it duplicated the same hook that `FikaBotDeathReconcile` already handles. Dead bots are now detected via direct `HealthController.IsAlive` check at weapon pickup time, eliminating redundant postfix execution on every bot death.
- **Simplified architecture:** `FikaWeaponPickupFix` now has a single responsibility — cleaning orphan `FirearmController` instances when the player picks up a weapon. All bot death processing (corpse creation, ragdoll, audio, reconciliation) is handled exclusively by `FikaBotDeathReconcile`.

## 1.3.0 (2026-07-09)

- **Enhanced diagnostic logging:** детальная информация о мёртвых ботах - ник, фракция, тип HandsController, оружие. При подборе оружия - статистика сканирования (сколько игроков, сколько пропущено мёртвых, сколько очищено).
- **Memory leak fix:** автоматическая очистка устаревших записей в DeadBotProfileIds (>5 минут) предотвращает неограниченный рост памяти.
- **Stack traces in error logs:** при исключениях в логи теперь пишется полный stack trace для диагностики.

# Changelog — Fika Weapon Pickup Fix

## 1.2.0 (2026-07-09)

- **Fix game freeze on bot death:** `BotDeadCleanupPatch` больше не null'ит `_handsController` сразу при смерти бота. Вместо этого ставится флаг «мёртв» (`DeadBotProfileIds`), а очистка контроллера происходит лениво — только при подборе оружия игроком (`PlayerWeaponPickupPatch`). Это устраняет конфликт с `FikaBotDeathReconcile`, который также обрабатывает смерть ботов и нуждается в `HandsController` для создания трупа и ragdoll.

## 1.1.0

- Initial release: BotDeadCleanupPatch + PlayerWeaponPickupPatch + FirearmControllerDestroyPatch
