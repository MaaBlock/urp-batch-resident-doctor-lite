using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MaaBlock.BatchDoctor.Lite.Editor
{
    public class ShaderCompatibilityBridge
    {
        private const string DotsInstancingKeyword = "DOTS_INSTANCING_ON";
        private const string ErrorShaderName = "Hidden/InternalErrorShader";

        private static readonly Func<Shader, int, int> s_GetCompatibilityCode;
        private static readonly Func<Shader, int, int, string> s_GetIssueReason;
        private static readonly Func<Shader, string[]> s_GetGlobalKeywords;
        private static readonly Func<Shader, string[]> s_GetLocalKeywords;

        private readonly Dictionary<int, ShaderAuditResult> m_Cache = new Dictionary<int, ShaderAuditResult>();

        static ShaderCompatibilityBridge()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type shaderUtilType = typeof(ShaderUtil);

            s_GetCompatibilityCode = BindStaticDelegate<Func<Shader, int, int>>(
                shaderUtilType, "GetSRPBatcherCompatibilityCode", flags);
            s_GetIssueReason = BindStaticDelegate<Func<Shader, int, int, string>>(
                shaderUtilType, "GetSRPBatcherCompatibilityIssueReason", flags);
            s_GetGlobalKeywords = BindStaticDelegate<Func<Shader, string[]>>(
                shaderUtilType, "GetShaderGlobalKeywords", flags);
            s_GetLocalKeywords = BindStaticDelegate<Func<Shader, string[]>>(
                shaderUtilType, "GetShaderLocalKeywords", flags);
        }

        private static TDelegate BindStaticDelegate<TDelegate>(Type type, string methodName, BindingFlags flags)
            where TDelegate : Delegate
        {
            MethodInfo method = type.GetMethod(methodName, flags);
            if (method == null)
                return null;
            return Delegate.CreateDelegate(typeof(TDelegate), method, false) as TDelegate;
        }

        public void ClearCache()
        {
            m_Cache.Clear();
        }

        public ShaderAuditResult InspectShader(Shader shader)
        {
            if (shader == null)
                return BuildMissingShaderResult();

            int instanceId = shader.GetInstanceID();
            if (m_Cache.TryGetValue(instanceId, out ShaderAuditResult cached))
                return cached;

            ShaderAuditResult result = EvaluateShaderUncached(shader);
            m_Cache[instanceId] = result;
            return result;
        }

        private static ShaderAuditResult BuildMissingShaderResult()
        {
            return new ShaderAuditResult
            {
                Shader = null,
                ShaderName = "<Missing Shader>",
                IsErrorOrHiddenShader = true,
                IsSrpBatcherCompatible = false,
                IncompatibilityReason = "Material has no assigned Shader.",
                SupportsDotsInstancing = false
            };
        }

        private static ShaderAuditResult EvaluateShaderUncached(Shader shader)
        {
            string shaderName = shader.name ?? "<Unnamed Shader>";
            bool isNullDevice = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
            bool isError = string.Equals(shaderName, ErrorShaderName, StringComparison.Ordinal)
                           || ShaderUtil.ShaderHasError(shader)
                           || (!isNullDevice && !shader.isSupported);
            if (isError)
            {
                return new ShaderAuditResult
                {
                    Shader = shader,
                    ShaderName = shaderName,
                    IsErrorOrHiddenShader = true,
                    IsSrpBatcherCompatible = false,
                    IncompatibilityReason = "Shader failed compilation or is unsupported on the active graphics API.",
                    SupportsDotsInstancing = false
                };
            }

            // Note: 调用 Unity C++ 底层 ShaderUtil 原生接口检测活动 SubShader 的 UnityPerMaterial CBuffer 兼容性代码与失败原因
            int activeSubShader = 0;
            QueryNativeSrpCompatibility(shader, activeSubShader, out int compatCode, out string reason);

            // Note: Unity 6 默认开启异步着色器编译，首次加载工程尚未渲染过的 Shader 会返回 "Not initialized"，需强制同步编译各 Pass 以初始化底层 CBuffer 反射表
            if (compatCode != 0 && reason.IndexOf("Not initialized", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                WarmUpShaderPassesSynchronously(shader);
                QueryNativeSrpCompatibility(shader, activeSubShader, out compatCode, out reason);
            }

            bool isSrpCompatible = compatCode == 0
                                   || reason.IndexOf("Not initialized", StringComparison.OrdinalIgnoreCase) >= 0;

            return new ShaderAuditResult
            {
                Shader = shader,
                ShaderName = shaderName,
                IsErrorOrHiddenShader = false,
                IsSrpBatcherCompatible = isSrpCompatible,
                IncompatibilityReason = isSrpCompatible ? string.Empty : reason.Trim(),
                SupportsDotsInstancing = CheckDotsInstancingKeyword(shader)
            };
        }

        private static void QueryNativeSrpCompatibility(Shader shader, int subShader, out int code, out string reason)
        {
            code = s_GetCompatibilityCode != null ? s_GetCompatibilityCode(shader, subShader) : 0;
            reason = string.Empty;
            if (code != 0 && s_GetIssueReason != null)
                reason = s_GetIssueReason(shader, subShader, code) ?? "Incompatible UnityPerMaterial CBuffer layout.";
        }

        private static void WarmUpShaderPassesSynchronously(Shader shader)
        {
            var tempMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                int passCount = tempMat.passCount;
                for (int p = 0; p < passCount; p++)
                {
                    if (!ShaderUtil.IsPassCompiled(tempMat, p))
                        ShaderUtil.CompilePass(tempMat, p, true);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tempMat);
            }
        }

        private static bool CheckDotsInstancingKeyword(Shader shader)
        {
            if (shader.keywordSpace.FindKeyword(DotsInstancingKeyword).isValid)
                return true;

            if (ContainsKeyword(s_GetGlobalKeywords?.Invoke(shader), DotsInstancingKeyword))
                return true;

            if (ContainsKeyword(s_GetLocalKeywords?.Invoke(shader), DotsInstancingKeyword))
                return true;

            return false;
        }

        private static bool ContainsKeyword(string[] keywords, string target)
        {
            if (keywords == null)
                return false;

            for (int i = 0; i < keywords.Length; i++)
            {
                if (string.Equals(keywords[i], target, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
