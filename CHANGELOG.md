# Changelog

All notable changes to **URP Batch & Resident Doctor Lite** will be documented in this file.

## [1.0.0] - 2026-11-01

### Added
- Native Unity 6 (`6000.0+`) **GPU Resident Drawer** & **SRP Batcher** pipeline configuration readiness audit (`Forward+` / `Deferred+` cluster light loop validation, `BatchRendererGroup Variants = Keep All` player stripping guard, and `useSRPBatcher` checks).
- Direct `ShaderUtil` native C++ compiler compatibility bridge extracting exact `UnityPerMaterial` CBuffer incompatibility reasons and `DOTS_INSTANCING_ON` keyword support.
- Per-Renderer scene classification (`GPU Resident Ready`, `Static Batch Conflict`, `SRP Batched Only`, `Batch Breaker`) with grouped culprit summary, `MaterialPropertyBlock` detection, negative-scale winding flip detection, and one-click Hierarchy selection.
- EditMode automated test suite.
