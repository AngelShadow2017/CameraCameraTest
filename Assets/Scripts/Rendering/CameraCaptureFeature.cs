using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering; // for GraphicsFormat

namespace Rendering
{
    /// <summary>
    /// 相机输出捕获Feature
    /// 在相机渲染完成后（包括后处理），捕获其输出供下一个相机使用
    /// </summary>
    public class CameraCaptureFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Tooltip("是否启用调试日志")]
            public bool enableDebugLog = false;
        }

        public Settings settings = new Settings();
        private CameraCapturePass capturePass;

        // 存储每个相机的输出纹理 (使用TextureHandle而非RTHandle)
        private static Dictionary<Camera, RTHandle> cameraOutputs = new Dictionary<Camera, RTHandle>();
        
        // 每个相机的纹理描述符缓存
        private static Dictionary<Camera, RenderTextureDescriptor> cameraDescriptors = new Dictionary<Camera, RenderTextureDescriptor>();

        public override void Create()
        {
            capturePass = new CameraCapturePass(settings);
            // 在所有渲染完成后捕获（包括后处理）
            capturePass.renderPassEvent = RenderPassEvent.AfterRendering;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game && 
                renderingData.cameraData.cameraType != CameraType.SceneView)
                return;

            Camera currentCamera = renderingData.cameraData.camera;
            
            // 检查是否在相机链中
            CameraChainManager manager = CameraChainManager.GetManagerForCamera(currentCamera);
            if (manager == null)
                return; // 不在任何链中，不需要捕获

            // 如果不是最后一个相机，需要捕获输出
            int index = manager.GetCameraIndex(currentCamera);
            var renderOrder = manager.GetRenderOrder();
            
            if (index >= 0 && index < renderOrder.Count - 1)
            {
                // 为当前相机分配或更新持久化的RTHandle
                EnsureCameraOutputRTHandle(currentCamera, renderingData.cameraData.cameraTargetDescriptor);
                
                // 不是最后一个相机，需要捕获
                capturePass.Setup(currentCamera);
                renderer.EnqueuePass(capturePass);
                
                if (settings.enableDebugLog)
                {
                    Debug.Log($"[CameraCapture] Enqueued capture pass for camera '{currentCamera.name}'");
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            capturePass?.Dispose();
            
            // 清理所有捕获的纹理
            foreach (var kvp in cameraOutputs)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.Release();
                }
            }
            cameraOutputs.Clear();
            cameraDescriptors.Clear();
        }

        /// <summary>
        /// 获取指定相机的输出纹理
        /// </summary>
        public static RTHandle GetCameraOutput(Camera camera)
        {
            if (camera == null)
                return null;
                
            cameraOutputs.TryGetValue(camera, out var output);
            return output;
        }

        /// <summary>
        /// 设置指定相机的输出纹理
        /// </summary>
        private static void SetCameraOutput(Camera camera, RTHandle output)
        {
            if (camera == null)
                return;

            // 如果已存在，先释放
            if (cameraOutputs.TryGetValue(camera, out var existing) && existing != null)
            {
                existing.Release();
            }

            cameraOutputs[camera] = output;
        }
        
        /// <summary>
        /// 确保相机有对应的RTHandle，如果尺寸改变则重新分配
        /// </summary>
        private static void EnsureCameraOutputRTHandle(Camera camera, RenderTextureDescriptor cameraDesc)
        {
            if (camera == null)
                return;

            bool needsAlloc = false;
            
            // 检查是否已存在
            if (!cameraOutputs.TryGetValue(camera, out var existingRT) || existingRT == null)
            {
                needsAlloc = true;
            }
            else if (cameraDescriptors.TryGetValue(camera, out var cachedDesc))
            {
                // 检查尺寸或格式是否改变
                if (cachedDesc.width != cameraDesc.width || 
                    cachedDesc.height != cameraDesc.height || 
                    cachedDesc.graphicsFormat != cameraDesc.graphicsFormat)
                {
                    existingRT.Release();
                    needsAlloc = true;
                }
            }
            else
            {
                needsAlloc = true;
            }

            if (needsAlloc)
            {
                // 创建新的RTHandle
                RenderTextureDescriptor desc = cameraDesc;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;
                desc.useMipMap = false;
                
                // 使用简化的RTHandles.Alloc重载
                RTHandle newRT = RTHandles.Alloc(
                    desc.width,
                    desc.height,
                    TextureXR.slices,
                    DepthBits.None,
                    desc.graphicsFormat,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    TextureDimension.Tex2D,
                    enableRandomWrite: false,
                    useMipMap: false,
                    autoGenerateMips: false,
                    isShadowMap: false,
                    anisoLevel: 1,
                    mipMapBias: 0f,
                    msaaSamples: MSAASamples.None,
                    bindTextureMS: false,
                    useDynamicScale: false,
                    memoryless: RenderTextureMemoryless.None,
                    name: $"CameraOutput_{camera.name}"
                );
                
                cameraOutputs[camera] = newRT;
                cameraDescriptors[camera] = desc;
            }
        }

        private class CameraCapturePass : ScriptableRenderPass
        {
            private Settings settings;
            private Camera targetCamera;
            private const string profilerTag = "Camera Output Capture";

            public CameraCapturePass(Settings settings)
            {
                this.settings = settings;
            }

            public void Setup(Camera camera)
            {
                this.targetCamera = camera;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                // 获取预分配的RTHandle
                RTHandle captureRT = GetCameraOutput(targetCamera);
                if (captureRT == null)
                {
                    Debug.LogError($"[CameraCapture] No RTHandle allocated for camera '{targetCamera.name}'");
                    return;
                }

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(profilerTag, out var passData))
                {
                    // 获取相机的最终输出（包括后处理）
                    TextureHandle cameraColor = resourceData.activeColorTexture;
                    passData.sourceTexture = cameraColor;
                    passData.targetCamera = targetCamera;

                    // 将预分配的RTHandle导入到RenderGraph
                    TextureHandle captureTexture = renderGraph.ImportTexture(captureRT);
                    passData.destinationTexture = captureTexture;

                    // 设置渲染目标（将拷贝结果写入捕获纹理）
                    builder.SetRenderAttachment(captureTexture, 0);

                    // 读取源纹理
                    builder.UseTexture(cameraColor, AccessFlags.Read);

                    // 不允许剔除此Pass
                    builder.AllowPassCulling(false);
                    
                    // 设置渲染函数 - 使用static lambda避免分配
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        ExecutePass(data, context);
                    });

                    if (settings.enableDebugLog)
                    {
                        Debug.Log($"[CameraCapture] RenderGraph pass recorded for '{targetCamera.name}'");
                    }
                }
            }

            private static void ExecutePass(PassData data, RasterGraphContext context)
            {
                // 从TextureHandle获取实际的RTHandle
                RTHandle sourceRT = (data.sourceTexture);
                
                // 将当前相机的输出 Blit 到捕获纹理（已设置为渲染目标）
                Blitter.BlitTexture(
                    context.cmd,
                    sourceRT,
                    new Vector4(1, 1, 0, 0), // scale and bias
                    0, // mipLevel
                    false // bilinear
                );
            }

            public void Dispose()
            {
                // 清理资源在Feature的Dispose中处理
            }

            private class PassData
            {
                public TextureHandle sourceTexture;
                public TextureHandle destinationTexture;
                public Camera targetCamera;
            }
        }
    }
}
