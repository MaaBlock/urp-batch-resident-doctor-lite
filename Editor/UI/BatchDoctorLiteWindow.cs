using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MaaBlock.BatchDoctor.Lite.Editor
{
    public class BatchDoctorLiteWindow : EditorWindow
    {
        public const string ProUpgradeUrl = "https://github.com/MaaBlock/urp-batch-resident-doctor-lite#pro-edition-vs-lite";

        private readonly SceneBatchAuditor m_Auditor = new SceneBatchAuditor();
        private SceneBatchAuditReport m_Report;
        private bool m_IncludeInactive;
        private int m_SelectedTierFilter = -1;
        private string m_SearchQuery = string.Empty;
        private Vector2 m_MainScroll;

        [MenuItem("Tools/URP Batch & Resident Doctor/Open Batch Doctor (Lite)", priority = 100)]
        public static void OpenWindow()
        {
            BatchDoctorLiteWindow window = GetWindow<BatchDoctorLiteWindow>("Batch Doctor Lite");
            window.minSize = new Vector2(680f, 520f);
            window.RunAudit();
            window.Show();
        }

        private void OnEnable()
        {
            if (m_Report == null)
                RunAudit();
        }

        public void RunAudit()
        {
            m_Report = m_Auditor.AuditActiveScenes(m_IncludeInactive);
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbarHeader();

            if (m_Report == null)
            {
                EditorGUILayout.HelpBox("Click 'Scan Active Scene' to run SRP Batcher & GPU Resident Drawer diagnostics.", MessageType.Info);
                return;
            }

            m_MainScroll = EditorGUILayout.BeginScrollView(m_MainScroll);
            DrawSummaryCards();
            EditorGUILayout.Space(6f);
            DrawPipelineSection();
            EditorGUILayout.Space(6f);
            DrawIssueGroupsSection();
            EditorGUILayout.Space(6f);
            DrawRendererListSection();
            EditorGUILayout.Space(10f);
            DrawProComparisonFooter();
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbarHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Scan Active Scene", EditorStyles.toolbarButton, GUILayout.Width(130f)))
                RunAudit();

            bool nextIncludeInactive = GUILayout.Toggle(m_IncludeInactive, "Include Inactive", EditorStyles.toolbarButton, GUILayout.Width(120f));
            if (nextIncludeInactive != m_IncludeInactive)
            {
                m_IncludeInactive = nextIncludeInactive;
                RunAudit();
            }

            GUILayout.FlexibleSpace();

            if (m_Report != null)
            {
                GUILayout.Label(
                    $"Scanned {m_Report.TotalRenderers} renderers in {m_Report.ScanDurationMs:F2} ms",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSummaryCards()
        {
            EditorGUILayout.BeginHorizontal();
            DrawMetricCard(
                "GPU Resident Ready",
                $"{m_Report.GpuResidentReadyCount} ({m_Report.GpuResidentCoveragePercent:F0}%)",
                new Color(0.18f, 0.72f, 0.36f),
                0);
            DrawMetricCard(
                "SRP Batched Only",
                m_Report.SrpBatchedOnlyCount.ToString(),
                new Color(0.85f, 0.70f, 0.18f),
                1);
            DrawMetricCard(
                "Static Conflicts",
                m_Report.StaticConflictCount.ToString(),
                new Color(0.92f, 0.50f, 0.16f),
                2);
            DrawMetricCard(
                "Batch Breakers",
                m_Report.BatchBreakerCount.ToString(),
                new Color(0.88f, 0.24f, 0.24f),
                3);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawMetricCard(string label, string valueText, Color accentColor, int tierFilterIndex)
        {
            Color prevBg = GUI.backgroundColor;
            bool isSelected = m_SelectedTierFilter == tierFilterIndex;
            GUI.backgroundColor = isSelected ? Color.Lerp(accentColor, Color.white, 0.25f) : Color.Lerp(accentColor, Color.black, 0.45f);

            EditorGUILayout.BeginVertical("box", GUILayout.Height(54f), GUILayout.ExpandWidth(true));
            GUI.backgroundColor = prevBg;

            GUILayout.Label(label, EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(valueText, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(isSelected ? "Clear" : "Filter", EditorStyles.miniButton, GUILayout.Width(48f)))
                m_SelectedTierFilter = isSelected ? -1 : tierFilterIndex;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawPipelineSection()
        {
            EditorGUILayout.BeginVertical("box");
            string statusBadge = m_Report.IsPipelineResidentReady
                ? "READY (SRP Batcher + GPU Resident Drawer)"
                : (m_Report.IsPipelineSrpReady ? "PARTIAL (SRP Batcher Ready / GPU Resident Blocked)" : "BLOCKED");
            EditorGUILayout.LabelField($"1. Pipeline & Build Settings Readiness — {statusBadge}", EditorStyles.boldLabel);

            if (m_Report.PipelineFindings.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Active URP Asset, Forward+/Deferred+ Cluster Light Loop, SRP Batcher, and BatchRendererGroup KeepAll stripping settings are properly configured.",
                    MessageType.Info);
            }
            else
            {
                for (int i = 0; i < m_Report.PipelineFindings.Count; i++)
                {
                    PipelineHealthItem item = m_Report.PipelineFindings[i];
                    MessageType msgType = item.Severity switch
                    {
                        PipelineIssueSeverity.Error => MessageType.Error,
                        PipelineIssueSeverity.Warning => MessageType.Warning,
                        _ => MessageType.Info
                    };
                    EditorGUILayout.HelpBox($"{item.Title}\n{item.Detail}", msgType);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawIssueGroupsSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"2. Grouped Batch & Resident Culprits ({m_Report.IssueGroups.Count} categories)", EditorStyles.boldLabel);

            if (m_Report.IssueGroups.Count == 0)
            {
                EditorGUILayout.LabelField("Zero batch breakers or conflicts found in active scene.", EditorStyles.miniLabel);
            }
            else
            {
                for (int i = 0; i < m_Report.IssueGroups.Count; i++)
                    DrawSingleIssueGroup(m_Report.IssueGroups[i]);
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawSingleIssueGroup(IssueGroupSummary group)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"[{group.AffectedCount} objects] {group.Title}", EditorStyles.boldLabel);
            if (GUILayout.Button("Select All in Hierarchy", EditorStyles.miniButton, GUILayout.Width(145f)))
                SelectEntriesInHierarchy(group.Entries);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(group.Recommendation, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawRendererListSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("3. Per-Renderer Breakdown", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Search:", EditorStyles.miniLabel, GUILayout.Width(45f));
            m_SearchQuery = EditorGUILayout.TextField(m_SearchQuery, GUILayout.Width(180f));
            if (m_SelectedTierFilter != -1 && GUILayout.Button("Show All Tiers", EditorStyles.miniButton, GUILayout.Width(95f)))
                m_SelectedTierFilter = -1;
            EditorGUILayout.EndHorizontal();

            int shown = 0;
            const int maxRows = 150;

            for (int i = 0; i < m_Report.Entries.Count; i++)
            {
                RendererAuditEntry entry = m_Report.Entries[i];
                if (!MatchesFilter(entry))
                    continue;

                if (shown < maxRows)
                    DrawRendererRow(entry);

                shown++;
            }

            if (shown > maxRows)
                EditorGUILayout.LabelField($"Showing first {maxRows} of {shown} matching renderers. Refine search filter to narrow down.", EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
        }

        private bool MatchesFilter(RendererAuditEntry entry)
        {
            if (m_SelectedTierFilter != -1 && (int)entry.Tier != m_SelectedTierFilter)
                return false;

            if (string.IsNullOrWhiteSpace(m_SearchQuery))
                return true;

            return (entry.HierarchyPath != null && entry.HierarchyPath.IndexOf(m_SearchQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                   || (entry.PrimaryShaderName != null && entry.PrimaryShaderName.IndexOf(m_SearchQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                   || (entry.IssueDetail != null && entry.IssueDetail.IndexOf(m_SearchQuery, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void DrawRendererRow(RendererAuditEntry entry)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            string badge = entry.Tier switch
            {
                RendererBatchTier.GpuResidentReady => "[GRD READY]",
                RendererBatchTier.SrpBatchedOnly => "[SRP ONLY]",
                RendererBatchTier.StaticBatchConflict => "[STATIC CONFLICT]",
                _ => "[BREAKER]"
            };

            GUILayout.Label(badge, EditorStyles.miniBoldLabel, GUILayout.Width(115f));
            EditorGUILayout.BeginVertical();
            GUILayout.Label($"{entry.HierarchyPath}   ({entry.PrimaryShaderName})", EditorStyles.miniBoldLabel);
            GUILayout.Label(entry.IssueDetail, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            if (entry.GameObject != null && GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(48f)))
            {
                Selection.activeGameObject = entry.GameObject;
                EditorGUIUtility.PingObject(entry.GameObject);
            }

            EditorGUILayout.EndHorizontal();
        }

        private static void DrawProComparisonFooter()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Need 3D Scene View Heatmap, 1-Click Auto-Fixers & Project-Wide Prefab Scanning?", EditorStyles.boldLabel);
            if (GUILayout.Button("Get Pro Edition", GUILayout.Width(130f)))
                Application.OpenURL(ProUpgradeUrl);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "Pro unlocks: Live 3D Color Heatmap Overlay in Scene View • 1-Click Undoable Auto-Fix (Pipeline Forward+/KeepAll, Strip BatchingStatic, Clear MPBs) • Offline All-Prefabs & Multi-Scene Audit • Standalone HTML/JSON CI Reports.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private static void SelectEntriesInHierarchy(List<RendererAuditEntry> entries)
        {
            var objects = new List<UnityEngine.Object>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].GameObject != null)
                    objects.Add(entries[i].GameObject);
            }

            if (objects.Count > 0)
            {
                Selection.objects = objects.ToArray();
                EditorGUIUtility.PingObject(objects[0]);
            }
        }
    }
}
