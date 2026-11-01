using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MaaBlock.BatchDoctor.Lite.Editor
{
    public class SceneBatchAuditor
    {
        private readonly ShaderCompatibilityBridge m_ShaderBridge = new ShaderCompatibilityBridge();

        public ShaderCompatibilityBridge ShaderBridge => m_ShaderBridge;

        public SceneBatchAuditReport AuditActiveScenes(bool includeInactive = false)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            m_ShaderBridge.ClearCache();

            var report = new SceneBatchAuditReport
            {
                TimestampUtc = DateTime.UtcNow
            };

            report.PipelineFindings = PipelineReadinessAuditor.AuditActivePipeline(
                out bool isSrpReady,
                out bool isResidentReady);
            report.IsPipelineSrpReady = isSrpReady;
            report.IsPipelineResidentReady = isResidentReady;

            FindObjectsInactive inactiveMode = includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(inactiveMode, FindObjectsSortMode.None);

            AuditRendererCollection(renderers, report);
            report.IssueGroups = BuildIssueGroups(report.Entries);

            stopwatch.Stop();
            report.ScanDurationMs = stopwatch.Elapsed.TotalMilliseconds;
            return report;
        }

        public RendererAuditEntry AuditSingleRenderer(Renderer renderer)
        {
            var entry = new RendererAuditEntry
            {
                Renderer = renderer,
                GameObject = renderer.gameObject,
                HierarchyPath = BuildHierarchyPath(renderer.transform),
                HasPropertyBlock = renderer.HasPropertyBlock(),
                IsStaticBatched = GameObjectUtility.AreStaticEditorFlagsSet(renderer.gameObject, StaticEditorFlags.BatchingStatic),
                HasOddNegativeScale = HasOddNegativeScale(renderer.transform)
            };

            Material[] materials = renderer.sharedMaterials;
            entry.SharedMaterialCount = materials != null ? materials.Length : 0;
            entry.SubMeshCount = ExtractSubMeshCount(renderer);

            if (!ValidateMeshPresence(renderer, entry))
                return entry;

            if (!ValidateMaterialsAndShaders(materials, out ShaderAuditResult worstShader, out string primaryShaderName, entry))
            {
                entry.PrimaryShaderName = primaryShaderName;
                return entry;
            }

            entry.PrimaryShaderName = primaryShaderName;
            ClassifyCompatibleShaderRenderer(renderer, worstShader, entry);
            return entry;
        }

        private void AuditRendererCollection(Renderer[] renderers, SceneBatchAuditReport report)
        {
            report.TotalRenderers = renderers.Length;
            report.Entries.Capacity = renderers.Length;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                RendererAuditEntry entry = AuditSingleRenderer(renderer);
                report.Entries.Add(entry);
                AccumulateTierCounts(entry.Tier, report);
            }
        }

        private static void AccumulateTierCounts(RendererBatchTier tier, SceneBatchAuditReport report)
        {
            switch (tier)
            {
                case RendererBatchTier.GpuResidentReady:
                    report.GpuResidentReadyCount++;
                    break;
                case RendererBatchTier.SrpBatchedOnly:
                    report.SrpBatchedOnlyCount++;
                    break;
                case RendererBatchTier.StaticBatchConflict:
                    report.StaticConflictCount++;
                    break;
                case RendererBatchTier.BatchBreaker:
                    report.BatchBreakerCount++;
                    break;
            }
        }

        private static bool ValidateMeshPresence(Renderer renderer, RendererAuditEntry entry)
        {
            if (renderer is MeshRenderer)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    entry.Tier = RendererBatchTier.BatchBreaker;
                    entry.PrimaryIssue = BatchIssueCode.MissingMesh;
                    entry.PrimaryShaderName = "<No Mesh>";
                    entry.IssueDetail = "MeshRenderer has no valid MeshFilter.sharedMesh assigned.";
                    return false;
                }
            }
            else if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh == null)
            {
                entry.Tier = RendererBatchTier.BatchBreaker;
                entry.PrimaryIssue = BatchIssueCode.MissingMesh;
                entry.PrimaryShaderName = "<No Mesh>";
                entry.IssueDetail = "SkinnedMeshRenderer has no sharedMesh assigned.";
                return false;
            }

            return true;
        }

        private bool ValidateMaterialsAndShaders(
            Material[] materials,
            out ShaderAuditResult firstIncompatibleOrNonDots,
            out string primaryShaderName,
            RendererAuditEntry entry)
        {
            firstIncompatibleOrNonDots = null;
            primaryShaderName = "<None>";

            if (materials == null || materials.Length == 0)
            {
                entry.Tier = RendererBatchTier.BatchBreaker;
                entry.PrimaryIssue = BatchIssueCode.NullOrMissingMaterial;
                entry.IssueDetail = "Renderer has 0 assigned materials.";
                return false;
            }

            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                if (mat == null || mat.shader == null)
                {
                    entry.Tier = RendererBatchTier.BatchBreaker;
                    entry.PrimaryIssue = BatchIssueCode.NullOrMissingMaterial;
                    entry.IssueDetail = $"Material slot [{i}] is null or missing its shader.";
                    return false;
                }

                ShaderAuditResult shaderResult = m_ShaderBridge.InspectShader(mat.shader);
                if (i == 0)
                    primaryShaderName = shaderResult.ShaderName;

                if (!shaderResult.IsSrpBatcherCompatible)
                {
                    entry.Tier = RendererBatchTier.BatchBreaker;
                    entry.PrimaryIssue = BatchIssueCode.ShaderNotSrpCompatible;
                    entry.IssueDetail = string.IsNullOrEmpty(shaderResult.IncompatibilityReason)
                        ? $"Shader '{shaderResult.ShaderName}' is not SRP Batcher compatible."
                        : $"Shader '{shaderResult.ShaderName}': {shaderResult.IncompatibilityReason}";
                    return false;
                }

                if (!shaderResult.SupportsDotsInstancing && firstIncompatibleOrNonDots == null)
                    firstIncompatibleOrNonDots = shaderResult;
            }

            return true;
        }

        private static void ClassifyCompatibleShaderRenderer(
            Renderer renderer,
            ShaderAuditResult nonDotsShader,
            RendererAuditEntry entry)
        {
            if (entry.HasPropertyBlock)
            {
                entry.Tier = RendererBatchTier.BatchBreaker;
                entry.PrimaryIssue = BatchIssueCode.MaterialPropertyBlockActive;
                entry.IssueDetail = "Active MaterialPropertyBlock forces per-draw fallback and breaks both SRP Batcher and GPU Resident Drawer.";
                return;
            }

            if (renderer is not MeshRenderer)
            {
                entry.Tier = RendererBatchTier.SrpBatchedOnly;
                entry.PrimaryIssue = BatchIssueCode.NonMeshRendererFallback;
                entry.IssueDetail = $"{renderer.GetType().Name} is not supported by GPU Resident Drawer; falls back to SRP Batcher.";
                return;
            }

            if (entry.IsStaticBatched)
            {
                entry.Tier = RendererBatchTier.StaticBatchConflict;
                entry.PrimaryIssue = BatchIssueCode.StaticBatchingBlocksResidentDrawer;
                entry.IssueDetail = "BatchingStatic flag merges geometry into static VBOs, disabling GPU Resident Drawer per-instance culling.";
                return;
            }

            if (nonDotsShader != null)
            {
                entry.Tier = RendererBatchTier.SrpBatchedOnly;
                entry.PrimaryIssue = BatchIssueCode.ShaderMissingDotsInstancing;
                entry.IssueDetail = $"Shader '{nonDotsShader.ShaderName}' lacks '#pragma multi_compile _ DOTS_INSTANCING_ON'; falls back to SRP Batcher.";
                return;
            }

            if (entry.SubMeshCount > 0 && entry.SharedMaterialCount > entry.SubMeshCount)
            {
                entry.Tier = RendererBatchTier.SrpBatchedOnly;
                entry.PrimaryIssue = BatchIssueCode.OversubscribedMaterialSlots;
                entry.IssueDetail = $"Renderer has {entry.SharedMaterialCount} material slots for {entry.SubMeshCount} submesh(es), issuing redundant draw calls.";
                return;
            }

            if (entry.HasOddNegativeScale)
            {
                entry.Tier = RendererBatchTier.SrpBatchedOnly;
                entry.PrimaryIssue = BatchIssueCode.OddNegativeScaleWindingFlip;
                entry.IssueDetail = "Odd negative scale flips triangle winding culling state, splitting instanced batches from positive-scaled meshes.";
                return;
            }

            entry.Tier = RendererBatchTier.GpuResidentReady;
            entry.PrimaryIssue = BatchIssueCode.None;
            entry.IssueDetail = "Fully eligible for Unity 6 GPU Resident Drawer (BatchRendererGroup) & SRP Batcher.";
        }

        private static int ExtractSubMeshCount(Renderer renderer)
        {
            if (renderer is MeshRenderer)
            {
                MeshFilter mf = renderer.GetComponent<MeshFilter>();
                return mf != null && mf.sharedMesh != null ? mf.sharedMesh.subMeshCount : 0;
            }

            if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                return smr.sharedMesh.subMeshCount;

            return 0;
        }

        private static bool HasOddNegativeScale(Transform transform)
        {
            Vector3 s = transform.lossyScale;
            return (s.x * s.y * s.z) < 0f;
        }

        private static string BuildHierarchyPath(Transform current)
        {
            if (current == null)
                return string.Empty;

            var parts = new List<string>(8);
            Transform cursor = current;
            while (cursor != null)
            {
                parts.Add(cursor.name);
                cursor = cursor.parent;
            }

            var sb = new StringBuilder(64);
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                if (sb.Length > 0)
                    sb.Append('/');
                sb.Append(parts[i]);
            }

            return sb.ToString();
        }

        private static List<IssueGroupSummary> BuildIssueGroups(List<RendererAuditEntry> entries)
        {
            var map = new Dictionary<BatchIssueCode, IssueGroupSummary>();

            for (int i = 0; i < entries.Count; i++)
            {
                RendererAuditEntry entry = entries[i];
                if (entry.PrimaryIssue == BatchIssueCode.None)
                    continue;

                if (!map.TryGetValue(entry.PrimaryIssue, out IssueGroupSummary group))
                {
                    group = CreateIssueGroupMetadata(entry.PrimaryIssue, entry.Tier);
                    map[entry.PrimaryIssue] = group;
                }

                group.Entries.Add(entry);
            }

            var groups = new List<IssueGroupSummary>(map.Values);
            groups.Sort((a, b) =>
            {
                int tierCompare = b.WorstTier.CompareTo(a.WorstTier);
                return tierCompare != 0 ? tierCompare : b.AffectedCount.CompareTo(a.AffectedCount);
            });
            return groups;
        }

        private static IssueGroupSummary CreateIssueGroupMetadata(BatchIssueCode code, RendererBatchTier tier)
        {
            return code switch
            {
                BatchIssueCode.MaterialPropertyBlockActive => new IssueGroupSummary
                {
                    IssueCode = code,
                    WorstTier = tier,
                    Title = "Active MaterialPropertyBlock (Breaks SRP Batcher & GRD)",
                    Recommendation = "Clear MaterialPropertyBlock and migrate per-instance colors/floats to Material variants or DOTS Instanced properties."
                },
                BatchIssueCode.ShaderNotSrpCompatible => new IssueGroupSummary
                {
                    IssueCode = code,
                    WorstTier = tier,
                    Title = "Incompatible Shader CBuffer Layout (Breaks SRP Batcher & GRD)",
                    Recommendation = "Wrap all material properties consistently inside CBUFFER_START(UnityPerMaterial) across every pass, or switch to URP Lit/SimpleLit/ShaderGraph."
                },
                BatchIssueCode.NullOrMissingMaterial => new IssueGroupSummary
                {
                    IssueCode = code,
                    WorstTier = tier,
                    Title = "Null or Missing Material / Broken Shader",
                    Recommendation = "Remove empty material slots or assign a valid URP material."
                },
                BatchIssueCode.MissingMesh => new IssueGroupSummary
                {
                    IssueCode = code,
                    WorstTier = tier,
                    Title = "Renderer Missing Mesh Asset",
                    Recommendation = "Assign a valid sharedMesh or remove unused MeshRenderer components."
                },
                BatchIssueCode.StaticBatchingBlocksResidentDrawer => new IssueGroupSummary
                {
                    IssueCode = code,
                    WorstTier = tier,
                    Title = "BatchingStatic Flag Blocks Unity 6 GPU Resident Drawer",
                    Recommendation = "Uncheck 'Batching Static' on MeshRenderers (keep Contribute GI / Occluder Static) so GPU Resident Drawer can GPU-cull and instance them."
                },
                BatchIssueCode.ShaderMissingDotsInstancing => new IssueGroupSummary
                {
                    IssueCode = code,
                    WorstTier = tier,
                    Title = "Shader Missing DOTS_INSTANCING_ON Keyword",
                    Recommendation = "Add '#pragma multi_compile _ DOTS_INSTANCING_ON' and '#pragma target 4.5' (or enable DOTS Instancing in Shader Graph)."
                },
                BatchIssueCode.NonMeshRendererFallback => new IssueGroupSummary
                {
                    IssueCode = code,
                    WorstTier = tier,
                    Title = "Non-MeshRenderer (Skinned / Particle / Sprite) SRP Fallback",
                    Recommendation = "Expected behavior: GPU Resident Drawer only batches MeshRenderers; ensure these remain SRP Batcher compatible."
                },
                BatchIssueCode.OddNegativeScaleWindingFlip => new IssueGroupSummary
                {
                    IssueCode = code,
                    WorstTier = tier,
                    Title = "Odd Negative Transform Scale (Winding Flip Split)",
                    Recommendation = "Avoid negative mirrored scales (-1, 1, 1) on heavily instanced props; bake mirrored geometry if needed."
                },
                BatchIssueCode.OversubscribedMaterialSlots => new IssueGroupSummary
                {
                    IssueCode = code,
                    WorstTier = tier,
                    Title = "More Material Slots Than Mesh SubMeshes",
                    Recommendation = "Trim Renderer.sharedMaterials array length to match Mesh.subMeshCount."
                },
                _ => new IssueGroupSummary
                {
                    IssueCode = code,
                    WorstTier = tier,
                    Title = code.ToString(),
                    Recommendation = "Inspect renderer configuration."
                }
            };
        }
    }
}
