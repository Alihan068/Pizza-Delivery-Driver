# Pizza Delivery Driver Release Checklist

## Before a distributable build

- Confirm the six enabled scenes in Build Settings are ordered as `MainMenu`, `GarageScene`, `MapSelectionScene`, `GameScene`, `NarrowDistrict`, `Expressway`.
- Run `Tools/PizzaGame/Validate Localization` and confirm every referenced key exists in every authored language with no unused keys.
- Run scene validation for all enabled scenes and confirm there are no missing scripts or broken prefab references.
- Run the EditMode suite and confirm all tests pass.
- Clear the Unity Console, refresh/import all assets, wait for compilation to finish, then build. Do not build while Unity reports uncompiled changes.
- Verify the player build manually through `MainMenu → Garage → MapSelection → GameScene → result → Garage`.

## MCP package policy

`com.coplaydev.unity-mcp` is a development dependency. It may remain in the working project for editor automation, but it must not be part of a public release source snapshot unless the owner intentionally chooses that policy.

1. Make a copy of `Packages/manifest.json` and `Packages/packages-lock.json` outside the release branch.
2. Remove only `com.coplaydev.unity-mcp` through Unity Package Manager so Unity recalculates the lock file.
3. Reopen or refresh Unity, resolve packages, compile, run tests, validate scenes, and build the release player.
4. Check that no runtime script references MCP-only types.
5. Keep the development branch's MCP manifest unchanged in its backup; restore it only in the development checkout, never inside the release artifact.

Do not remove `com.unity.modules.ai` without also removing or replacing the Wingman editor helper that uses `NavMeshAgent` and `NavMeshObstacle`. Do not remove `com.unity.modules.physics` or `com.unity.modules.terrain` while MCP/URP still declare them as dependencies.

## Current verification

- Windows Mono Development build: passed on 2026-09-07 with 0 errors and 3 known Unity/URP/third-party warnings.
- EditMode suite: passed on 2026-09-07 with 15/15 tests passed.
- Windows IL2CPP release build: blocked by the local toolchain; install Windows SDK 10.0.19041+ and the Visual Studio C++ workload before retrying.
- Manual gameplay and visual checks remain owner-only and are tracked in `memory-bank/owner_checklist.md`.
