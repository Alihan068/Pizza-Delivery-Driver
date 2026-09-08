# Pizza Delivery Driver Release Checklist

## Driving-system change gate — planned, not implemented (2026-09-08)

The drift-driving revision requires fresh validation before release. Previous build and test results below describe the pre-change version and cannot close these gates.

- [ ] Revalidate keyboard/gamepad driving, pause/resume, device/focus loss, and UI input isolation.
- [ ] Validate all registered vehicle prefabs and test new vehicle creation without changing templates or existing vehicle IDs.
- [ ] Run collision, cargo loss, collection, customer lifecycle, settlement and save compatibility regressions.
- [ ] Verify steering, braking and controlled drift in all three gameplay maps with initial and upgraded vehicles.
- [ ] Measure frame-time spikes, steady-state allocations and feedback lifetime across five consecutive shifts.
- [x] Produce a fresh Windows Development build and record test artifacts and outstanding manual checks; retry the release backend after its toolchain requirements are satisfied. Windows Mono Development job `build-4eac2bcc57` succeeded on 2026-09-08 at `139.77 MB`, with `0` errors and `3` known Unity/URP/third-party warnings.

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

## Pre-driving-change verification (historical)

- Windows Mono Development build: passed on 2026-09-08 with `139.77 MB`, 0 errors and 3 known Unity/URP/third-party warnings (`build-4eac2bcc57`).
- EditMode suite: passed on 2026-09-08 with `24/24` tests passed (`3f6e9e0c6a3340ef8793f3f58d470fb2`).
- Windows IL2CPP release build: blocked by the local toolchain; install Windows SDK 10.0.19041+ and the Visual Studio C++ workload before retrying.
- Manual gameplay and visual checks remain owner-only and are tracked in `memory-bank/owner_checklist.md`.
