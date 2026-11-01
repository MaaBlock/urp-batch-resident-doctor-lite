# URP Batch & Resident Doctor Lite — User Manual

## Overview

**URP Batch & Resident Doctor Lite** audits active Unity 6 (`6000.0+`) scenes for **SRP Batcher** and **GPU Resident Drawer (`BatchRendererGroup`)** compatibility issues in milliseconds—without clicking through thousands of nodes in Unity's Frame Debugger.

## Opening the Tool

In the Unity Editor menu bar, go to:

`Tools > URP Batch & Resident Doctor > Open Batch Doctor (Lite)`

## What It Audits

1. **Pipeline & Graphics Settings Readiness**
   - Verifies that an active `UniversalRenderPipelineAsset` has `SRP Batcher` enabled.
   - Verifies that `GPU Resident Drawer` is set to `InstancedDrawing`.
   - Verifies that **every** `UniversalRendererData` in the pipeline uses a cluster light loop (`Forward+` or `Deferred+`). Classic `Forward` or legacy `Deferred` silently disables GPU Resident Drawer in Unity 6!
   - Verifies `Edit > Project Settings > Graphics > BatchRendererGroup Variants` is set to `Keep All` so DOTS Instancing variants are not stripped in standalone Player builds.

2. **Native C++ `ShaderUtil` Compatibility Inspection**
   - Queries Unity's internal shader compiler compatibility codes to detect mismatched `UnityPerMaterial` CBuffer layouts across passes.
   - Verifies `DOTS_INSTANCING_ON` keyword availability required by `GPUResidentDrawer`.

3. **Per-Renderer Scene Classification**
   - **GPU Resident Ready**: Eligible for `BatchRendererGroup` GPU-driven instancing & SRP Batcher.
   - **Static Conflicts**: `BatchingStatic` enabled on `MeshRenderer`, blocking `GPUResidentDrawer` per-instance frustum/occlusion culling.
   - **SRP Batched Only**: Compatible with SRP Batcher, but excluded from `GPUResidentDrawer` (e.g., `SkinnedMeshRenderer`, missing `DOTS_INSTANCING_ON`, odd negative scale winding flip, oversubscribed material slots).
   - **Batch Breakers**: Breaks both `GPUResidentDrawer` and `SRP Batcher` (`MaterialPropertyBlock` active, incompatible `UnityPerMaterial` CBuffer, null material, or missing mesh).
