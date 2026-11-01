using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MaaBlock.BatchDoctor.Lite.Editor
{
    public static class PipelineReadinessAuditor
    {
        public static List<PipelineHealthItem> AuditActivePipeline(out bool isSrpReady, out bool isResidentReady)
        {
            var findings = new List<PipelineHealthItem>();
            RenderPipelineAsset currentAsset = GraphicsSettings.currentRenderPipeline;

            if (currentAsset == null)
            {
                AddMissingPipelineFinding(findings);
                isSrpReady = false;
                isResidentReady = false;
                return findings;
            }

            if (currentAsset is not UniversalRenderPipelineAsset urpAsset)
            {
                AddNonUrpFinding(findings, currentAsset);
                isSrpReady = false;
                isResidentReady = false;
                return findings;
            }

            CheckSrpBatcherToggle(urpAsset, findings);
            CheckResidentDrawerMode(urpAsset, findings);
            CheckClusterLightLoopRenderers(urpAsset, findings);
            CheckBrgShaderStrippingMode(findings);
            CheckRawBufferPlatformSupport(findings);

            ComputeReadinessFlags(findings, out isSrpReady, out isResidentReady);
            return findings;
        }

        private static void AddMissingPipelineFinding(List<PipelineHealthItem> findings)
        {
            findings.Add(new PipelineHealthItem
            {
                Code = "PIPELINE_BUILTIN",
                Title = "Built-in Render Pipeline Active",
                Detail = "No Scriptable Render Pipeline asset is assigned in Graphics Settings. Neither SRP Batcher nor GPU Resident Drawer can run.",
                Severity = PipelineIssueSeverity.Error,
                BlocksSrpBatcher = true,
                BlocksResidentDrawer = true
            });
        }

        private static void AddNonUrpFinding(List<PipelineHealthItem> findings, RenderPipelineAsset asset)
        {
            findings.Add(new PipelineHealthItem
            {
                Code = "PIPELINE_NOT_URP",
                Title = "Active Pipeline Is Not URP",
                Detail = $"Current pipeline '{asset.name}' ({asset.GetType().Name}) is not a UniversalRenderPipelineAsset.",
                Severity = PipelineIssueSeverity.Warning,
                BlocksSrpBatcher = false,
                BlocksResidentDrawer = true
            });
        }

        private static void CheckSrpBatcherToggle(UniversalRenderPipelineAsset urpAsset, List<PipelineHealthItem> findings)
        {
            if (urpAsset.useSRPBatcher)
                return;

            findings.Add(new PipelineHealthItem
            {
                Code = "URP_SRP_BATCHER_OFF",
                Title = "SRP Batcher Is Disabled on URP Asset",
                Detail = $"'{urpAsset.name}' has useSRPBatcher = false. Both SRP Batcher and GPU Resident Drawer require SRP Batcher enabled.",
                Severity = PipelineIssueSeverity.Error,
                BlocksSrpBatcher = true,
                BlocksResidentDrawer = true
            });
        }

        private static void CheckResidentDrawerMode(UniversalRenderPipelineAsset urpAsset, List<PipelineHealthItem> findings)
        {
            if (urpAsset.gpuResidentDrawerMode == GPUResidentDrawerMode.InstancedDrawing)
                return;

            findings.Add(new PipelineHealthItem
            {
                Code = "URP_GRD_DISABLED",
                Title = "GPU Resident Drawer Is Disabled",
                Detail = $"'{urpAsset.name}' has GPU Resident Drawer set to Disabled instead of InstancedDrawing.",
                Severity = PipelineIssueSeverity.Warning,
                BlocksSrpBatcher = false,
                BlocksResidentDrawer = true
            });
        }

        private static void CheckClusterLightLoopRenderers(UniversalRenderPipelineAsset urpAsset, List<PipelineHealthItem> findings)
        {
            // Note: Unity 6 中 GPUResidentDrawer 强制要求所有 UniversalRendererData 启用簇光照循环（Forward+ 或 Deferred+），普通 Forward/Deferred 会导致 GRD 静默停用
            var serializedAsset = new SerializedObject(urpAsset);
            SerializedProperty rendererListProp = serializedAsset.FindProperty("m_RendererDataList");
            if (rendererListProp == null || !rendererListProp.isArray)
                return;

            for (int i = 0; i < rendererListProp.arraySize; i++)
            {
                Object element = rendererListProp.GetArrayElementAtIndex(i).objectReferenceValue;
                if (element == null)
                    continue;

                if (element is not UniversalRendererData rendererData)
                {
                    findings.Add(new PipelineHealthItem
                    {
                        Code = "URP_NON_UNIVERSAL_RENDERER",
                        Title = $"Renderer Slot #{i} Is Not UniversalRendererData",
                        Detail = $"Renderer '{element.name}' ({element.GetType().Name}) (2D/Custom Renderer) disables GPU Resident Drawer across this URP Asset.",
                        Severity = PipelineIssueSeverity.Warning,
                        BlocksSrpBatcher = false,
                        BlocksResidentDrawer = true
                    });
                    continue;
                }

                if (!rendererData.usesClusterLightLoop)
                {
                    findings.Add(new PipelineHealthItem
                    {
                        Code = "URP_RENDERING_MODE_NO_CLUSTER",
                        Title = $"Renderer '{rendererData.name}' Uses '{rendererData.renderingMode}' Instead of Forward+ / Deferred+",
                        Detail = "Unity 6 GPU Resident Drawer requires cluster light loops (Forward+ or Deferred+). Classic Forward/Deferred silently disables GPU Resident Drawer.",
                        Severity = PipelineIssueSeverity.Error,
                        BlocksSrpBatcher = false,
                        BlocksResidentDrawer = true
                    });
                }
            }
        }

        private static void CheckBrgShaderStrippingMode(List<PipelineHealthItem> findings)
        {
            if (EditorGraphicsSettings.batchRendererGroupShaderStrippingMode == BatchRendererGroupStrippingMode.KeepAll)
                return;

            findings.Add(new PipelineHealthItem
            {
                Code = "BRG_STRIPPING_INVALID",
                Title = "BatchRendererGroup Variants Will Be Stripped in Player Builds",
                Detail = $"Graphics Settings -> 'BatchRendererGroup Variants' is '{EditorGraphicsSettings.batchRendererGroupShaderStrippingMode}'. Must be 'KeepAll' or all DOTS instancing shaders fail in builds.",
                Severity = PipelineIssueSeverity.Error,
                BlocksSrpBatcher = false,
                BlocksResidentDrawer = true
            });
        }

        private static void CheckRawBufferPlatformSupport(List<PipelineHealthItem> findings)
        {
            if (BatchRendererGroup.BufferTarget == BatchBufferTarget.RawBuffer)
                return;

            findings.Add(new PipelineHealthItem
            {
                Code = "BRG_RAW_BUFFER_UNSUPPORTED",
                Title = "Active Graphics API Does Not Use RawBuffer Target",
                Detail = $"Current BatchRendererGroup.BufferTarget is '{BatchRendererGroup.BufferTarget}'. GPU Resident Drawer requires SSBO / RawBuffer support.",
                Severity = PipelineIssueSeverity.Warning,
                BlocksSrpBatcher = false,
                BlocksResidentDrawer = true
            });
        }

        private static void ComputeReadinessFlags(List<PipelineHealthItem> findings, out bool isSrpReady, out bool isResidentReady)
        {
            isSrpReady = true;
            isResidentReady = true;

            for (int i = 0; i < findings.Count; i++)
            {
                if (findings[i].BlocksSrpBatcher)
                    isSrpReady = false;
                if (findings[i].BlocksResidentDrawer)
                    isResidentReady = false;
            }
        }
    }
}
