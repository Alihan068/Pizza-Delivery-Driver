# Pizza-Delivery-Driver — current agent rules

This is the concise project entrypoint. Full pre-cleanup text: [AGENTS snapshot](memory-bank/history/AGENTS.md.snapshot-2026-09-21.md).

## Authority and scope

- Before planning or delegating, read [the shared model-routing policy](C:/Users/PcVIP/.agents/model-routing.md) once per session (again if changed), then [the project routing supplement](memory-bank/model-routing.md). Explicitly select supported delegation models; never claim an unverified switch or change billing/global model settings.
- New explicit user instructions take precedence, then this file, then the current handoff and verification records. Older chronology is context only.
- Current authority: [traffic-police/handoff.md](memory-bank/traffic-police/handoff.md), supported by [traffic-police/verification.md](memory-bank/traffic-police/verification.md) and [drift_verification.md](memory-bank/drift_verification.md).
- Before Unity work, read [UNITY_AI_GUIDELINES.md](UNITY_AI_GUIDELINES.md), [memory-bank/README.md](memory-bank/README.md), the current handoff, and relevant verification. Follow [the 14-skill mapping](memory-bank/README.md#genel-unity-skilleri--2026-09-21); installed skills live at `C:/Users/PcVIP/.agents/skills/`.
- Preserve unrelated dirty changes and other agent ownership. Work only within explicitly authorized scope.
- Use the project’s plan/authority gates and update existing tracking documents after meaningful implementation steps.
- The owner checklist is owner-only. Do not claim owner acceptance as agent verification.
- Report evidence truthfully: use `Not Run`/`Not Measured`; source completion without live evidence remains `[~]`.

## Permanent boundaries

- Never run `git commit`, even after a request without first reminding the user and obtaining confirmation. Never push or alter the index.
- Map editing/generation/rebuilding must not touch `GameScene`. New maps use separate scenes/blueprints; change `GameScene` only after a separate explicit request.
- No code, Unity, scene, prefab, asset, or build-setting changes outside the explicitly authorized task scope. The user reauthorized full project implementation and testing on 2026-09-21; the GameScene protection, one-operator rule, and no-index rule still apply.
- Code/comments are English; use K&R braces and camelCase. Do not add explicit `private` fields; use `[SerializeField]` for inspector data and `public` only for external API. Add XML docs to every new/changed public member (quality target at least 8/10).
- Preserve Unity serialization and GUID stability; do not casually rename serialized fields/assets or rewrite YAML. Verify APIs against Unity `6000.0.62f1`.
- Type-based `Find` is allowed only for genuine singletons. Multi-instance types use serialized references; name-based `Find` is prohibited.
- Use one Unity operator. Do not edit/recompile while another worker tests or changes Unity/Assets.
- Play Mode approval is task-specific: stale approval from an earlier session does not apply to a new task. Ask before Play Mode unless the current user explicitly authorizes it.

## Verification

Use the project Verification Report style: environment/version, exact checks/results, console state, serialization integrity, Play Mode/Test Runner state, performance/GC state, and commit/index state. Never infer live, visual, input, performance, or owner evidence from source or Edit Mode results.

## Active execution scope

The former documentation-only maintenance scope is archived at [memory-bank/history/markdown-only-maintenance-2026-09-21.md](memory-bank/history/markdown-only-maintenance-2026-09-21.md). The active scope is the authorized S00–S12 implementation and verification plan, subject to the authority records and permanent project boundaries above.

Personal AI-process documents must stay out of public commits. This file was already tracked; ignore rules do not untrack it. Do not alter the index to fix that without authorization.
