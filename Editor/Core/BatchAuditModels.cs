using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaaBlock.BatchDoctor.Lite.Editor
{
    public enum RendererBatchTier
    {
        GpuResidentReady = 0,
        SrpBatchedOnly = 1,
        StaticBatchConflict = 2,
        BatchBreaker = 3
    }

    public enum BatchIssueCode
    {
        None = 0,
        NullOrMissingMaterial = 1,
        MissingMesh = 2,
        MaterialPropertyBlockActive = 3,
        ShaderNotSrpCompatible = 4,
        StaticBatchingBlocksResidentDrawer = 5,
        ShaderMissingDotsInstancing = 6,
        NonMeshRendererFallback = 7,
        OddNegativeScaleWindingFlip = 8,
        OversubscribedMaterialSlots = 9
    }

    public enum PipelineIssueSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    public class PipelineHealthItem
    {
        public string Code;
        public string Title;
        public string Detail;
        public PipelineIssueSeverity Severity;
        public bool BlocksSrpBatcher;
        public bool BlocksResidentDrawer;
    }

    public class ShaderAuditResult
    {
        public Shader Shader;
        public string ShaderName;
        public bool IsErrorOrHiddenShader;
        public bool IsSrpBatcherCompatible;
        public string IncompatibilityReason;
        public bool SupportsDotsInstancing;
    }

    public class RendererAuditEntry
    {
        public Renderer Renderer;
        public GameObject GameObject;
        public string HierarchyPath;
        public RendererBatchTier Tier;
        public BatchIssueCode PrimaryIssue;
        public string IssueDetail;
        public string PrimaryShaderName;
        public int SharedMaterialCount;
        public int SubMeshCount;
        public bool HasPropertyBlock;
        public bool IsStaticBatched;
        public bool HasOddNegativeScale;
    }

    public class IssueGroupSummary
    {
        public BatchIssueCode IssueCode;
        public RendererBatchTier WorstTier;
        public string Title;
        public string Recommendation;
        public List<RendererAuditEntry> Entries = new List<RendererAuditEntry>();

        public int AffectedCount => Entries.Count;
    }

    public class SceneBatchAuditReport
    {
        public DateTime TimestampUtc;
        public double ScanDurationMs;
        public int TotalRenderers;
        public int GpuResidentReadyCount;
        public int SrpBatchedOnlyCount;
        public int StaticConflictCount;
        public int BatchBreakerCount;
        public bool IsPipelineSrpReady;
        public bool IsPipelineResidentReady;
        public List<PipelineHealthItem> PipelineFindings = new List<PipelineHealthItem>();
        public List<IssueGroupSummary> IssueGroups = new List<IssueGroupSummary>();
        public List<RendererAuditEntry> Entries = new List<RendererAuditEntry>();

        public float GpuResidentCoveragePercent =>
            TotalRenderers > 0 ? (GpuResidentReadyCount * 100f) / TotalRenderers : 0f;

        public float SrpCompatibleCoveragePercent =>
            TotalRenderers > 0 ? ((TotalRenderers - BatchBreakerCount) * 100f) / TotalRenderers : 0f;
    }
}
