using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;


[System.Serializable]
public class BackgroundFromCameraFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Tooltip("用于全屏绘制的材质，shader需要采样_SourceTex。")]
        public Material fullscreenMaterial;

        [Tooltip("材质的采样贴图属性名。")]
        public string sourceTexturePropertyName = "_SourceTex";

        [Tooltip("插入的事件点。默认为天空盒之前。")]
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingSkybox;

        [Tooltip("如果需要，替代目标相机的清除标志（通常不用）。")]
        public bool overrideClearFlags = false;
        public ClearFlag clearFlags = ClearFlag.None;
        public Color clearColor = Color.black;
    }

    public Settings settings = new Settings();

    BackgroundFromCameraPass _pass;

    public override void Create()
    {
        _pass = new BackgroundFromCameraPass(settings);
        _pass.renderPassEvent = settings.passEvent;
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        var currentCam = renderingData.cameraData.camera;
        var targetCamera = CameraToRenderTexture.TargetCamera; // 从CameraToRenderTexture获取目标相机
        if (targetCamera == null || settings.fullscreenMaterial == null)
            return;
        if (currentCam == targetCamera)
        {
            _pass.ConfigureInput(ScriptableRenderPassInput.None);
            _pass.Setup(CameraToRenderTexture.SourceRT); // 给Pass源纹理
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var targetCamera = CameraToRenderTexture.TargetCamera; // 从CameraToRenderTexture获取目标相机
        if (targetCamera == null || settings.fullscreenMaterial == null)
            return;

        if (renderingData.cameraData.camera == targetCamera)
        {            
            _pass.ConfigureInput(ScriptableRenderPassInput.None);
            _pass.Setup(CameraToRenderTexture.SourceRT); // 给Pass源纹理
            renderer.EnqueuePass(_pass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        // 无持有需释放的资源
    }

    class BackgroundFromCameraPass : ScriptableRenderPass
    {
        readonly Settings _settings;
        static readonly ProfilingSampler s_Sampler = new ProfilingSampler("BackgroundFromCameraPass");

        RenderTexture _sourceRT; // 相机A的输出
        int _sourceTexId;        // 材质属性ID

        public BackgroundFromCameraPass(Settings settings)
        {
            _settings = settings;
            _sourceTexId = Shader.PropertyToID(string.IsNullOrEmpty(_settings.sourceTexturePropertyName)
                                               ? "_SourceTex"
                                               : _settings.sourceTexturePropertyName);
        }

        public void Setup(RenderTexture src)
        {
            _sourceRT = src;
        }

#if UNITY_6000_0_OR_NEWER
        // RenderGraph路径（URP 17+）
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_sourceRT == null || _settings.fullscreenMaterial == null)
                return;

            var res = frameData.Get<UniversalResourceData>();

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("BackgroundFromCameraPass.RecordRG", out var passData, s_Sampler))
            {
                passData.material = _settings.fullscreenMaterial;
                passData.sourceTex = _sourceRT;
                passData.color = res.activeColorTexture;
                passData.depth = res.activeDepthTexture;
                passData.clearFlags = _settings.overrideClearFlags ? _settings.clearFlags : ClearFlag.None;
                passData.clearColor = _settings.clearColor;
                builder.SetRenderAttachment(passData.color, 0);
                if (passData.depth.IsValid())
                    builder.SetRenderAttachmentDepth(passData.depth, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    // Clear render target if needed
                    if (data.clearFlags != ClearFlag.None)
                    {
                        bool clearDepth = (data.clearFlags & ClearFlag.Depth) != 0;
                        bool clearColor = (data.clearFlags & ClearFlag.Color) != 0;
                        ctx.cmd.ClearRenderTarget(clearDepth, clearColor, data.clearColor);
                    }

                    Debug.Log(data.sourceTex);
                    data.material.SetTexture(_sourceTexId, data.sourceTex);
                    CoreUtils.DrawFullScreen(ctx.cmd, data.material, shaderPassId: 0);
                });
            }
        }

        class PassData
        {
            public Material material;
            public Texture sourceTex;
            public TextureHandle color;
            public TextureHandle depth;
            public ClearFlag clearFlags;
            public Color clearColor;
        }
#endif
    }
}
