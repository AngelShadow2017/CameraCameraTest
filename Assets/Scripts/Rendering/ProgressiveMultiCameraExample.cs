using UnityEngine;

namespace Rendering
{
    /// <summary>
    /// Progressive Multi-Camera示例脚本
    /// 用于演示如何设置和使用渐进式多相机渲染系统
    /// </summary>
    public class ProgressiveMultiCameraExample : MonoBehaviour
    {
        [Header("Example Settings")]
        [SerializeField]
        [Tooltip("是否在Start时自动设置示例")]
        private bool autoSetupOnStart = false;

        [Header("Camera References")]
        [SerializeField]
        private Camera camera1;
        
        [SerializeField]
        private Camera camera2;
        
        [SerializeField]
        private Camera camera3;

        private CameraChainManager chainManager;

        private void Start()
        {
            if (autoSetupOnStart)
            {
                SetupExample();
            }
        }

        /// <summary>
        /// 设置示例配置
        /// </summary>
        [ContextMenu("Setup Example")]
        public void SetupExample()
        {
            // 创建或获取CameraChainManager
            chainManager = GetComponent<CameraChainManager>();
            if (chainManager == null)
            {
                chainManager = gameObject.AddComponent<CameraChainManager>();
                Debug.Log("Created CameraChainManager component");
            }

            // 查找或创建相机
            if (camera1 == null)
                camera1 = FindOrCreateCamera("Camera1_Background", 0);
            if (camera2 == null)
                camera2 = FindOrCreateCamera("Camera2_Main", 1);
            if (camera3 == null)
                camera3 = FindOrCreateCamera("Camera3_Overlay", 2);

            // 配置第一个相机（背景）
            ConfigureBackgroundCamera(camera1);
            
            // 配置第二个相机（主场景）
            ConfigureMainCamera(camera2);
            
            // 配置第三个相机（覆盖层）
            ConfigureOverlayCamera(camera3);

            // 使用CameraChainManager建立渲染链
            chainManager.ClearChain();
            chainManager.AddOrUpdateCamera(camera1, 0, true);
            chainManager.AddOrUpdateCamera(camera2, 100, true);
            chainManager.AddOrUpdateCamera(camera3, 200, true);

            Debug.Log("Progressive Multi-Camera example setup complete!");
            Debug.Log("Next steps:");
            Debug.Log("1. Add 'Progressive Multi-Camera Feature' to your URP Renderer");
            Debug.Log("2. Add 'Camera Capture Feature' to your URP Renderer");
            Debug.Log("3. Assign objects to layers: 'Background', 'MainScene', 'Overlay'");
        }

        private Camera FindOrCreateCamera(string cameraName, int depth)
        {
            // 先尝试在子对象中查找
            Transform existing = transform.Find(cameraName);
            if (existing != null)
            {
                Camera cam = existing.GetComponent<Camera>();
                if (cam != null)
                {
                    Debug.Log($"Found existing camera: {cameraName}");
                    return cam;
                }
            }

            // 创建新相机
            GameObject camObj = new GameObject(cameraName);
            camObj.transform.SetParent(transform);
            camObj.transform.localPosition = Vector3.zero;
            camObj.transform.localRotation = Quaternion.identity;

            Camera camera = camObj.AddComponent<Camera>();
            camera.depth = depth;
            
            Debug.Log($"Created new camera: {cameraName}");
            return camera;
        }

        private void ConfigureBackgroundCamera(Camera cam)
        {
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.depth = 0;
            cam.cullingMask = LayerMask.GetMask("Background");
            
            Debug.Log($"Configured '{cam.name}' as Background camera");
        }

        private void ConfigureMainCamera(Camera cam)
        {
            // 关键：不清除，在前一个相机的基础上渲染
            cam.clearFlags = CameraClearFlags.Nothing;
            cam.depth = 1;
            cam.cullingMask = LayerMask.GetMask("MainScene");
            
            Debug.Log($"Configured '{cam.name}' as Main camera");
        }

        private void ConfigureOverlayCamera(Camera cam)
        {
            // 关键：不清除，在前一个相机的基础上渲染
            cam.clearFlags = CameraClearFlags.Nothing;
            cam.depth = 2;
            cam.cullingMask = LayerMask.GetMask("Overlay");
            
            Debug.Log($"Configured '{cam.name}' as Overlay camera");
        }

        #region Runtime Control

        /// <summary>
        /// 运行时启用/禁用指定相机
        /// </summary>
        public void ToggleCamera(int cameraIndex, bool enabled)
        {
            if (chainManager == null)
            {
                Debug.LogError("CameraChainManager not found!");
                return;
            }

            // 这里需要扩展CameraChainManager来支持运行时修改
            // 当前版本可以直接enable/disable Camera组件
            Camera cam = null;
            switch (cameraIndex)
            {
                case 0: cam = camera1; break;
                case 1: cam = camera2; break;
                case 2: cam = camera3; break;
            }

            if (cam != null)
            {
                cam.enabled = enabled;
                Debug.Log($"Camera '{cam.name}' {(enabled ? "enabled" : "disabled")}");
            }
        }

        /// <summary>
        /// 运行时更新相机顺序
        /// </summary>
        public void UpdateCameraOrder()
        {
            if (chainManager != null)
            {
                chainManager.UpdateRenderOrder();
                Debug.Log("Camera render order updated");
            }
        }

        #endregion

        #region Debug Visualization

        private void OnGUI()
        {
            if (!Application.isPlaying)
                return;

            // 显示调试信息
            GUILayout.BeginArea(new Rect(10, 10, 300, 200));
            GUILayout.Label("Progressive Multi-Camera System", GUI.skin.box);
            
            if (chainManager != null)
            {
                var renderOrder = chainManager.GetRenderOrder();
                GUILayout.Label($"Active Cameras: {renderOrder.Count}");
                
                for (int i = 0; i < renderOrder.Count; i++)
                {
                    if (renderOrder[i] != null)
                    {
                        GUILayout.Label($"  {i}: {renderOrder[i].name}");
                    }
                }
            }
            else
            {
                GUILayout.Label("CameraChainManager not found");
                if (GUILayout.Button("Setup Example"))
                {
                    SetupExample();
                }
            }
            
            GUILayout.EndArea();
        }

        #endregion

        #region Editor Helpers

#if UNITY_EDITOR
        [ContextMenu("Print Camera Info")]
        private void PrintCameraInfo()
        {
            if (chainManager == null)
            {
                Debug.Log("No CameraChainManager found");
                return;
            }

            var renderOrder = chainManager.GetRenderOrder();
            Debug.Log($"=== Camera Chain Info ===");
            Debug.Log($"Total Cameras: {renderOrder.Count}");
            
            for (int i = 0; i < renderOrder.Count; i++)
            {
                Camera cam = renderOrder[i];
                if (cam != null)
                {
                    Debug.Log($"Camera {i}: {cam.name}");
                    Debug.Log($"  - Clear Flags: {cam.clearFlags}");
                    Debug.Log($"  - Depth: {cam.depth}");
                    Debug.Log($"  - Culling Mask: {LayerMaskToString(cam.cullingMask)}");
                    Debug.Log($"  - Enabled: {cam.enabled}");
                }
            }
        }

        private string LayerMaskToString(LayerMask mask)
        {
            string result = "";
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    string layerName = LayerMask.LayerToName(i);
                    if (!string.IsNullOrEmpty(layerName))
                    {
                        if (result.Length > 0)
                            result += ", ";
                        result += layerName;
                    }
                }
            }
            return string.IsNullOrEmpty(result) ? "Nothing" : result;
        }

        [ContextMenu("Validate Setup")]
        private void ValidateSetup()
        {
            Debug.Log("=== Validating Setup ===");
            
            bool hasErrors = false;

            // 检查CameraChainManager
            if (chainManager == null)
            {
                Debug.LogError("✗ CameraChainManager component missing!");
                hasErrors = true;
            }
            else
            {
                Debug.Log("✓ CameraChainManager found");
            }

            // 检查相机
            if (camera1 == null || camera2 == null || camera3 == null)
            {
                Debug.LogError("✗ Some cameras are not assigned!");
                hasErrors = true;
            }
            else
            {
                Debug.Log("✓ All cameras assigned");
                
                // 检查清除标志
                if (camera1.clearFlags == CameraClearFlags.Nothing)
                {
                    Debug.LogWarning("⚠ Camera1 should clear (Skybox or SolidColor)");
                }
                
                if (camera2.clearFlags != CameraClearFlags.Nothing)
                {
                    Debug.LogWarning("⚠ Camera2 should NOT clear (Don't Clear)");
                }
                
                if (camera3.clearFlags != CameraClearFlags.Nothing)
                {
                    Debug.LogWarning("⚠ Camera3 should NOT clear (Don't Clear)");
                }
            }

            // 检查Renderer Features
            Debug.Log("⚠ Remember to add Renderer Features to your URP Renderer Asset:");
            Debug.Log("  1. Progressive Multi-Camera Feature");
            Debug.Log("  2. Camera Capture Feature");

            if (!hasErrors)
            {
                Debug.Log("=== Validation Complete - No Critical Errors ===");
            }
        }
#endif

        #endregion
    }
}
