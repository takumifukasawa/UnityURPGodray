using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// -------------------------------------------------------------------------
// ref:
// https://www.cyanilux.com/tutorials/custom-renderer-features/
// -------------------------------------------------------------------------

/// <summary>
/// 
/// </summary>
public class GodrayRenderPassFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class GodrayPassSettings
    {
        public bool ShowInSceneView;
        public RenderPassEvent RenderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        public bool ClearTarget;

        [Header("Draw Renders Settings")]
        public LayerMask LayerMask = 1;

        public Material OverrideMaterial;
        public int OverrideMaterialPass;
        public string ColorTargetDestinationID = "";

        [Header("Blit Settings")]
        public Material BlitMaterial;

        [Range(0.1f, 1)]
        public float DownScalingRate = 1f;

        [Header("Parameters")]
        [Range(0, 1)]
        public float BlendRate = 1;

        [Range(0, 1)]
        public float GlobalAlpha = 1;

        public Color FogColor = Color.white;

        [Range(0, 128)]
        public float AttenuationBase = 64;

        [Range(0, 64)]
        public float AttenuationPower = 1;

        [Header("Ray")]
        // [Range(1, 128)]
        // public int MaxIterationNum = 64;
        
        [Range(0, 5)]
        public float RayStep = 0.1f;

        [Range(0, 5)]
        public float RayNearOffset = 0.01f;

        [Range(0, 0.01f)]
        public float RayJitterSizeX = 0.005f;

        [Range(0, 0.01f)]
        public float RayJitterSizeY = 0.005f;
    }

    [SerializeField]
    private GodrayPassSettings _godrayPassSettings;

    private GodrayRenderPass _scriptablePass;

    private Material _material;

    private bool _showInSceneView;

    /// <summary>
    /// 
    /// </summary>
    public override void Create()
    {
        _scriptablePass = new GodrayRenderPass(_godrayPassSettings, name);
        _scriptablePass.renderPassEvent = _godrayPassSettings.RenderPassEvent;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="renderer"></param>
    /// <param name="renderingData"></param>
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var cameraType = renderingData.cameraData.cameraType;
        if (cameraType == CameraType.Preview)
        {
            return;
        }

        if (!_godrayPassSettings.ShowInSceneView && cameraType == CameraType.SceneView)
        {
            return;
        }

        renderer.EnqueuePass(_scriptablePass);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="disposing"></param>
    protected override void Dispose(bool disposing)
    {
        _scriptablePass.Dispose();
    }
}

/// <summary>
/// 
/// </summary>
class GodrayRenderPass : ScriptableRenderPass
{
    // private const string PROFILER_TAG = nameof(GodrayRenderPass);

    private Material _material;
    private RTHandle _cameraColorTarget;
    private RTHandle _cameraDepthTarget;
    private GodrayRenderPassFeature.GodrayPassSettings _settings;
    private ProfilingSampler _profilingSampler;
    private RTHandle _rtCustomColor;
    private RTHandle _rtTempColor1;
    private RTHandle _rtTempColor2;
    private FilteringSettings _filteringSettings;
    private List<ShaderTagId> _shaderTagsList = new List<ShaderTagId>();

    private string _name;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="godrayPassSettings"></param>
    /// <param name="name"></param>
    public GodrayRenderPass(GodrayRenderPassFeature.GodrayPassSettings godrayPassSettings, string name)
    {
        _settings = godrayPassSettings;
        _name = name;

        _filteringSettings = new FilteringSettings(RenderQueueRange.opaque, _settings.LayerMask);

        _shaderTagsList.Add(new ShaderTagId("SRPDefaultUnlit"));
        _shaderTagsList.Add(new ShaderTagId("UniversalForward"));
        _shaderTagsList.Add(new ShaderTagId("UniversalForwardOnly"));

        _profilingSampler = new ProfilingSampler(name);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="cmd"></param>
    /// <param name="renderingData"></param>
    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        var colorDesc = renderingData.cameraData.cameraTargetDescriptor;
        colorDesc.depthBufferBits = 0;

        var rtTempDesc1 = renderingData.cameraData.cameraTargetDescriptor;
        rtTempDesc1.depthBufferBits = 0;
        rtTempDesc1.colorFormat = RenderTextureFormat.R8;

        var rtTempDesc2 = renderingData.cameraData.cameraTargetDescriptor;
        rtTempDesc2.depthBufferBits = 0;

        // RenderingUtils.ReAllocateIfNeeded(ref _rtTempColor1, colorDesc, name: "_TemporaryColorTexture1");
        // RenderingUtils.ReAllocateIfNeeded(ref _rtTempColor1, new Vector2(_settings.DownScalingRate, _settings.DownScalingRate), rtTempDesc1, name: "_TemporaryColorTexture1");
        RenderingUtils.ReAllocateIfNeeded(ref _rtTempColor1, new Vector2(0.5f, 0.5f), rtTempDesc1, name: "_TemporaryColorTexture1");
        RenderingUtils.ReAllocateIfNeeded(ref _rtTempColor2, rtTempDesc2, name: "_TemporaryColorTexture2");

        // Debug.Log("---------------------------");
        // Debug.Log(renderingData.cameraData.renderer.cameraColorTargetHandle.rt.width);
        // Debug.Log(renderingData.cameraData.renderer.cameraColorTargetHandle.rt.height);
        // Debug.Log(_rtTempColor1.scaleFactor);
        // Debug.Log(_rtTempColor1.GetScaledSize());

        if (_settings.ColorTargetDestinationID != "")
        {
            RenderingUtils.ReAllocateIfNeeded(ref _rtCustomColor, colorDesc, name: _settings.ColorTargetDestinationID);
        }
        else
        {
            _rtCustomColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
        }

        var rtCameraDepth = renderingData.cameraData.renderer.cameraDepthTargetHandle;

        if (_rtCustomColor == null || rtCameraDepth == null)
        {
            return;
        }

        if (_settings.ClearTarget)
        {
            ConfigureTarget(_rtCustomColor, rtCameraDepth);
            ConfigureClear(ClearFlag.Color, new Color(0, 0, 0, 0));
        }
        else
        {
            ConfigureTarget(_rtCustomColor, rtCameraDepth);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="context"></param>
    /// <param name="renderingData"></param>
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        // fallback
        if (_settings.RenderPassEvent == RenderPassEvent.AfterRendering)
        {
            return;
        }

        // NOTE: mac だと空にしないと endSample でエラーになる
        var commandBuffer = CommandBufferPool.Get();
        // var commandBuffer = CommandBufferPool.Get(_name);

        using (new ProfilingScope(commandBuffer, _profilingSampler))
        {
            context.ExecuteCommandBuffer(commandBuffer);
            commandBuffer.Clear();

            var soringCriteria = renderingData.cameraData.defaultOpaqueSortFlags;
            var drawingSettings = CreateDrawingSettings(_shaderTagsList, ref renderingData, soringCriteria);
            if (_settings.OverrideMaterial != null)
            {
                drawingSettings.overrideMaterialPassIndex = _settings.OverrideMaterialPass;
                drawingSettings.overrideMaterial = _settings.OverrideMaterial;
            }

            //
            // setup material
            //

            var projectionMatrix = renderingData.cameraData.camera.projectionMatrix;
            var viewMatrix = renderingData.cameraData.camera.worldToCameraMatrix;
            var viewProjectionMatrix = projectionMatrix * viewMatrix;
            var inverseViewMatrix = viewMatrix.inverse;
            var inverseViewProjectionMatrix = viewProjectionMatrix.inverse;
            var inverseProjectionMatrix = projectionMatrix.inverse;

            _settings.BlitMaterial.SetFloat("_BlendRate", _settings.BlendRate);
            _settings.BlitMaterial.SetFloat("_GlobalAlpha", _settings.GlobalAlpha);
            _settings.BlitMaterial.SetColor("_FogColor", _settings.FogColor);
            _settings.BlitMaterial.SetMatrix("_InverseViewMatrix", inverseViewMatrix);
            _settings.BlitMaterial.SetMatrix("_InverseProjectionMatrix", inverseProjectionMatrix);
            _settings.BlitMaterial.SetMatrix("_InverseViewProjectionMatrix", inverseViewProjectionMatrix);
            _settings.BlitMaterial.SetFloat("_RayStep", _settings.RayStep);
            _settings.BlitMaterial.SetFloat("_RayNearOffset", _settings.RayNearOffset);
            _settings.BlitMaterial.SetFloat("_RayJitterSizeX", _settings.RayJitterSizeX);
            _settings.BlitMaterial.SetFloat("_RayJitterSizeY", _settings.RayJitterSizeY);
            _settings.BlitMaterial.SetFloat("_AttenuationBase", _settings.AttenuationBase);
            _settings.BlitMaterial.SetFloat("_AttenuationPower", _settings.AttenuationPower);
            // _settings.BlitMaterial.SetInt("_MaxIterationNum", _settings.MaxIterationNum);

            //
            // end setup material
            //

            // post process の場合はいらない
            // context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref _filteringSettings);

            if (_settings.ColorTargetDestinationID != "")
            {
                commandBuffer.SetGlobalTexture(_settings.ColorTargetDestinationID, _rtCustomColor);
            }

            // blit
            if (_settings.BlitMaterial != null)
            {
                var cameraTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
                if (cameraTarget == null || _rtTempColor1 == null || _rtTempColor2 == null)
                {
                    return;
                }

                // if (cameraTarget != null && _rtTempColor != null)
                // {
                Blitter.BlitCameraTexture(commandBuffer, cameraTarget, _rtTempColor1, _settings.BlitMaterial, 0);
                commandBuffer.SetGlobalTexture("_GodrayTexture", _rtTempColor1);
                // Blitter.BlitCameraTexture(commandBuffer, _rtTempColor1, _rtTempColor2, _settings.BlitMaterial, 1);
                // Blitter.BlitCameraTexture(commandBuffer, _rtTempColor2, cameraTarget);
                Blitter.BlitCameraTexture(commandBuffer, cameraTarget, cameraTarget, _settings.BlitMaterial, 1);
                // }

                // tmp
                // Blitter.BlitCameraTexture(commandBuffer, cameraTarget, _rtTempColor, _settings.BlitMaterial, 0);
                // Blitter.BlitCameraTexture(commandBuffer, _rtTempColor, cameraTarget);
            }
        }

        context.ExecuteCommandBuffer(commandBuffer);
        commandBuffer.Clear();
        CommandBufferPool.Release(commandBuffer);

        // Blitter.BlitCameraTexture(commandBuffer, _cameraColorTarget, _cameraColorTarget, _material, 0);
        // context.ExecuteCommandBuffer(commandBuffer);
        // CommandBufferPool.Release(commandBuffer);

        // var cameraData = renderingData.cameraData;
        // var w = cameraData.camera.scaledPixelWidth;
        // var h = cameraData.camera.scaledPixelHeight;
        // 
        // commandBuffer.GetTemporaryRT(RENDER_TEXTURE_ID, w, h, 0, FilterMode.Point, RenderTextureFormat.Default);
        // commandBuffer.Blit(_currentRenderTargetIdentifier, RENDER_TEXTURE_ID);
        // commandBuffer.Blit(RENDER_TEXTURE_ID, _currentRenderTargetIdentifier);

        // context.ExecuteCommandBuffer(commandBuffer);
        // context.Submit();
        // 
        // CommandBufferPool.Release(commandBuffer);
    }

    /// <summary>
    /// 
    /// </summary>
    public void Dispose()
    {
        if (_settings.ColorTargetDestinationID != "")
        {
            _rtCustomColor?.Release();
        }

        _rtTempColor1?.Release();
        _rtTempColor2?.Release();
    }
}
