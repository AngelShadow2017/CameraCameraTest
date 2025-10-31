using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class ProgressiveMultiCameraFeature : ScriptableRendererFeature
{
    class ProgressiveBackgroundPass : ScriptableRenderPass
    {
        private readonly ProfilingSampler m_Profiler = new ProfilingSampler("ProgressiveBackground");
        private Material m_BlitMaterial;

        private class PassData
        {
            public TextureHandle source; // 上一相机的最终颜色（通过 ImportTexture）
            public TextureHandle destination; // 当前相机的 activeColorTexture
            public Material blitMat;
        }

        public ProgressiveBackgroundPass()
        {
            // 使用引擎自带Blit材质（或自定义纯拷贝材质）
            m_BlitMaterial = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/BlitCopy"));
            // 固定在 BeforeRendering 时机，这样在相机开始渲染之前就先绘制上一相机的结果作为背景
            renderPassEvent = RenderPassEvent.BeforeRendering;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();

            var cam = cameraData.camera;
            if (cam == null) return;
            if (!ProgressiveCameraTag.IsProgressive(cam)) return;

            // 第一台参与链的相机没有“上一输出”，直接跳过
            if (!CameraChainManager.HasLastOutputThisFrame()) return;

            // 导入上一相机结果作为 RG 的输入纹理
            var lastRT = CameraChainManager.GetLastOutputRT();
            if (lastRT == null) return;

            using (var builder =
                   renderGraph.AddRasterRenderPass<PassData>("ProgressiveBackground", out var passData, m_Profiler))
            {
                passData.source = renderGraph.ImportTexture(lastRT);
                passData.destination = resourceData.activeColorTexture;
                passData.blitMat = m_BlitMaterial;

                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.SetRenderAttachment(passData.destination, 0, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    // 直接进行全屏复制（默认不翻转）
                    Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.blitMat, 0);
                });
            }
        }

        protected void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_BlitMaterial);
        }
    }

    ProgressiveBackgroundPass m_Pass;

    public override void Create()
    {
        m_Pass = new ProgressiveBackgroundPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var cam = renderingData.cameraData.camera;
        if (cam != null && ProgressiveCameraTag.IsProgressive(cam))
        {
            renderer.EnqueuePass(m_Pass);
        }
    }
}