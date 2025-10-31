using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Rendering
{
    /// <summary>
    /// 管理多个相机的渲染顺序和层级关系
    /// 每个相机将在前一个相机的渲染结果上继续渲染（渐进式渲染）
    /// </summary>
    public class CameraChainManager : MonoBehaviour
    {
        [System.Serializable]
        public class CameraEntry
        {
            [Tooltip("要渲染的相机")]
            public Camera camera;
            
            [Tooltip("是否启用该相机")]
            public bool enabled = true;
            
            [Tooltip("该相机的渲染优先级（数字越小越先渲染）")]
            public int priority = 0;
        }

        [Header("Camera Chain Settings")]
        [SerializeField]
        [Tooltip("相机链列表，按priority排序渲染")]
        private List<CameraEntry> cameraChain = new List<CameraEntry>();

        [SerializeField]
        [Tooltip("是否强制将相机的Depth设置为渲染顺序（0,1,2,...) 以避免顺序错误")]
        private bool enforceCameraDepthOrder = true;

        [Header("Runtime Info")]
        [SerializeField]
        [Tooltip("当前渲染顺序（只读）")]
        private List<Camera> currentRenderOrder = new List<Camera>();

        private static Dictionary<Camera, CameraChainManager> cameraToChainManager = new Dictionary<Camera, CameraChainManager>();

        private void OnEnable()
        {
            UpdateRenderOrder();
            RegisterCameras();
        }

        private void OnDisable()
        {
            UnregisterCameras();
        }

        private void OnValidate()
        {
            UpdateRenderOrder();
        }

        /// <summary>
        /// 更新渲染顺序，并刷新全局注册表。
        /// </summary>
        public void UpdateRenderOrder()
        {
            // 记录之前注册的相机供注销
            var prevOrder = new List<Camera>(currentRenderOrder);

            currentRenderOrder.Clear();
            
            // 按priority排序，移除null和disabled的相机
            var sorted = cameraChain
                .Where(e => e != null && e.enabled && e.camera != null)
                .OrderBy(e => e.priority)
                .Select(e => e.camera)
                .ToList();

            currentRenderOrder.AddRange(sorted);

            // 可选：强制设置相机Depth与顺序一致，保证渲染顺序与链一致
            if (enforceCameraDepthOrder)
            {
                for (int i = 0; i < currentRenderOrder.Count; i++)
                {
                    var cam = currentRenderOrder[i];
                    if (cam != null)
                    {
                        cam.depth = i;
                    }
                }
            }

            // 刷新全局注册表
            foreach (var cam in prevOrder)
            {
                if (cam != null && cameraToChainManager.TryGetValue(cam, out var mgr) && mgr == this)
                {
                    cameraToChainManager.Remove(cam);
                }
            }
            foreach (var cam in currentRenderOrder)
            {
                if (cam != null)
                {
                    cameraToChainManager[cam] = this;
                }
            }
        }

        /// <summary>
        /// 注册所有相机到全局字典
        /// </summary>
        private void RegisterCameras()
        {
            foreach (var cam in currentRenderOrder)
            {
                if (cam != null)
                {
                    cameraToChainManager[cam] = this;
                }
            }
        }

        /// <summary>
        /// 取消注册所有相机
        /// </summary>
        private void UnregisterCameras()
        {
            foreach (var cam in currentRenderOrder)
            {
                if (cam != null && cameraToChainManager.ContainsKey(cam))
                {
                    cameraToChainManager.Remove(cam);
                }
            }
        }

        /// <summary>
        /// 获取当前相机的渲染顺序
        /// </summary>
        public List<Camera> GetRenderOrder()
        {
            return new List<Camera>(currentRenderOrder);
        }

        /// <summary>
        /// 获取指定相机在链中的索引
        /// </summary>
        /// <returns>索引，如果不在链中返回-1</returns>
        public int GetCameraIndex(Camera camera)
        {
            return currentRenderOrder.IndexOf(camera);
        }

        /// <summary>
        /// 获取指定相机的前一个相机
        /// </summary>
        /// <returns>前一个相机，如果是第一个或不在链中返回null</returns>
        public Camera GetPreviousCamera(Camera camera)
        {
            int index = GetCameraIndex(camera);
            if (index <= 0)
                return null;
            return currentRenderOrder[index - 1];
        }

        /// <summary>
        /// 判断是否是链中的第一个相机
        /// </summary>
        public bool IsFirstCamera(Camera camera)
        {
            return GetCameraIndex(camera) == 0;
        }

        /// <summary>
        /// 清空并刷新相机链。
        /// </summary>
        public void ClearChain()
        {
            cameraChain.Clear();
            UpdateRenderOrder();
        }

        /// <summary>
        /// 添加或更新相机项（可在运行时调用）。
        /// </summary>
        public void AddOrUpdateCamera(Camera camera, int priority, bool enabled = true)
        {
            if (camera == null)
                return;

            var entry = cameraChain.FirstOrDefault(e => e != null && e.camera == camera);
            if (entry == null)
            {
                cameraChain.Add(new CameraEntry
                {
                    camera = camera,
                    priority = priority,
                    enabled = enabled
                });
            }
            else
            {
                entry.priority = priority;
                entry.enabled = enabled;
            }

            UpdateRenderOrder();
        }

        /// <summary>
        /// 静态方法：获取相机所属的CameraChainManager
        /// </summary>
        public static CameraChainManager GetManagerForCamera(Camera camera)
        {
            if (camera == null)
                return null;
                
            cameraToChainManager.TryGetValue(camera, out var manager);
            return manager;
        }

        /// <summary>
        /// 静态方法：获取相机的前一个相机
        /// </summary>
        public static Camera GetPreviousCameraStatic(Camera camera)
        {
            var manager = GetManagerForCamera(camera);
            return manager?.GetPreviousCamera(camera);
        }

        /// <summary>
        /// 静态方法：判断是否是链中的第一个相机
        /// </summary>
        public static bool IsFirstCameraStatic(Camera camera)
        {
            var manager = GetManagerForCamera(camera);
            return manager?.IsFirstCamera(camera) ?? true; // 如果没有manager，视为独立相机
        }

        #region Editor Helpers

#if UNITY_EDITOR
        [ContextMenu("Auto Find Cameras in Children")]
        private void AutoFindCameras()
        {
            cameraChain.Clear();
            var cameras = GetComponentsInChildren<Camera>(true);
            
            for (int i = 0; i < cameras.Length; i++)
            {
                cameraChain.Add(new CameraEntry
                {
                    camera = cameras[i],
                    enabled = true,
                    priority = i * 100
                });
            }
            
            UpdateRenderOrder();
            Debug.Log($"Found {cameras.Length} cameras");
        }

        [ContextMenu("Print Render Order")]
        private void PrintRenderOrder()
        {
            UpdateRenderOrder();
            Debug.Log($"Camera Chain Render Order ({currentRenderOrder.Count} cameras):");
            for (int i = 0; i < currentRenderOrder.Count; i++)
            {
                Debug.Log($"  {i}: {currentRenderOrder[i].name}");
            }
        }
#endif

        #endregion
    }
}
