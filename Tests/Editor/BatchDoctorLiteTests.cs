using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MaaBlock.BatchDoctor.Lite.Editor.Tests
{
    public class BatchDoctorLiteTests
    {
        private GameObject m_TempObject;
        private Material m_TempUrpMaterial;

        [SetUp]
        public void SetUp()
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            Assert.IsNotNull(urpLit, "URP Lit shader must be available in URP verification project.");
            m_TempUrpMaterial = new Material(urpLit);
            m_TempObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_TempObject.GetComponent<MeshRenderer>().sharedMaterial = m_TempUrpMaterial;
        }

        [TearDown]
        public void TearDown()
        {
            if (m_TempObject != null)
                Object.DestroyImmediate(m_TempObject);
            if (m_TempUrpMaterial != null)
                Object.DestroyImmediate(m_TempUrpMaterial);
        }

        [Test]
        public void UrpLitMeshRenderer_IsClassifiedAsGpuResidentReady()
        {
            var auditor = new SceneBatchAuditor();
            RendererAuditEntry entry = auditor.AuditSingleRenderer(m_TempObject.GetComponent<MeshRenderer>());

            Assert.AreEqual(RendererBatchTier.GpuResidentReady, entry.Tier);
            Assert.AreEqual(BatchIssueCode.None, entry.PrimaryIssue);
        }

        [Test]
        public void MaterialPropertyBlock_IsClassifiedAsBatchBreaker()
        {
            MeshRenderer mr = m_TempObject.GetComponent<MeshRenderer>();
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", Color.red);
            mr.SetPropertyBlock(mpb);

            var auditor = new SceneBatchAuditor();
            RendererAuditEntry entry = auditor.AuditSingleRenderer(mr);

            Assert.AreEqual(RendererBatchTier.BatchBreaker, entry.Tier);
            Assert.AreEqual(BatchIssueCode.MaterialPropertyBlockActive, entry.PrimaryIssue);
        }

        [Test]
        public void BatchingStaticFlag_IsClassifiedAsStaticBatchConflict()
        {
            GameObjectUtility.SetStaticEditorFlags(m_TempObject, StaticEditorFlags.BatchingStatic);

            var auditor = new SceneBatchAuditor();
            RendererAuditEntry entry = auditor.AuditSingleRenderer(m_TempObject.GetComponent<MeshRenderer>());

            Assert.AreEqual(RendererBatchTier.StaticBatchConflict, entry.Tier);
            Assert.AreEqual(BatchIssueCode.StaticBatchingBlocksResidentDrawer, entry.PrimaryIssue);
        }

        [Test]
        public void OddNegativeScale_IsClassifiedAsWindingFlipSplit()
        {
            m_TempObject.transform.localScale = new Vector3(-1f, 1f, 1f);

            var auditor = new SceneBatchAuditor();
            RendererAuditEntry entry = auditor.AuditSingleRenderer(m_TempObject.GetComponent<MeshRenderer>());

            Assert.AreEqual(RendererBatchTier.SrpBatchedOnly, entry.Tier);
            Assert.AreEqual(BatchIssueCode.OddNegativeScaleWindingFlip, entry.PrimaryIssue);
        }
    }
}
