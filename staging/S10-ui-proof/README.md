# S10 UI visual/input acceptance fixture

This is staging-only. Do not copy it into `Assets` until the Unity source-window owner releases the lease.

## Integration call

1. Copy `S10UiVisualInputAcceptanceFixture.cs.txt` to `Assets/Tests/Editor/S10UiVisualInputAcceptanceFixture.cs`.
2. In Unity Test Runner, run EditMode tests by exact class:

```text
S10UiVisualInputAcceptanceFixture
```

The fixture opens `MapSelectionScene` and `NarrowDistrict` additively only to clone the production
`MapSelectionPanel` and typed `SessionResultPanel` Canvas ancestors into an unsaved `PreviewScene`.
`GameScene` is never opened. It never saves a scene, invokes `GameManager.Awake`, applies the map
preview, calls career/load/save APIs, selects a language through the preference path, or edits an
asset. The isolated manager receives authored maps, modifiers and a `ContentRegistry` through test
reflection while remaining inactive.

All pointer, submit, and move actions are synthetic `ExecuteEvents`; they are not physical mouse,
keyboard, or gamepad evidence. PNGs are emitted at 1920x1080 to:

```text
memory-bank/traffic-police/evidence/s10-ui-proof/
```

## Current limits

- This turn did not invoke Unity, import the staged file, run Test Runner, or generate PNGs.
- `PoliceStatusView`/HUD capture, real pause-time freeze, and physical hardware input are not part
  of this fixture and remain Not Run. No fake active-HUD screenshot is emitted.
- `TryApplyPreviewSelection` is intentionally not called: its production contract invokes
  `SelectMap` and the save path. Duration and modifier assertions therefore remain explicitly
  preview-only.
