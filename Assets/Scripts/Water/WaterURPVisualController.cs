using UnityEngine;

[ExecuteAlways]
public sealed class WaterURPVisualController : MonoBehaviour
{
    [Header("Scale-independent waves")]
    [Min(0.01f)] public float tilesPerMeter = 0.32f;
    [Range(0.02f, 0.15f)] public float waveScale = 0.145f;
    public Vector4 waterProWaveSpeed = new Vector4(8.5f, 4.2f, -7.4f, -3.1f);

    [Header("Water colors")]
    public Color shallowColor = new Color(0.18f, 0.72f, 0.86f, 0.72f);
    public Color deepColor = new Color(0.025f, 0.20f, 0.34f, 0.88f);
    public Color horizonColor = new Color(0.42f, 0.88f, 0.95f, 1f);
    [Min(0.01f)] public float depthDistance = 4f;

    [Header("Surface")]
    [Range(0f, 0.10f)] public float refractionStrength = 0.042f;
    public Color refractionColor = new Color(0.72f, 0.94f, 1f, 1f);
    [Range(0.5f, 8f)] public float fresnelPower = 3f;
    [Range(0f, 2f)] public float foamWidth = 0.28f;
    [Range(0f, 2f)] public float foamIntensity = 0.65f;
    [Range(0f, 1f)] public float waveHighlight = 0.02f;

    private static readonly int WaveTilingId = Shader.PropertyToID("_WaveTiling");
    private static readonly int TilesPerMeterId = Shader.PropertyToID("_TilesPerMeter");
    private static readonly int WaveScaleId = Shader.PropertyToID("_WaveScale");
    private static readonly int LegacyWaveSpeedId = Shader.PropertyToID("_LegacyWaveSpeed");
    private static readonly int ShallowColorId = Shader.PropertyToID("_ShallowColor");
    private static readonly int DeepColorId = Shader.PropertyToID("_DeepColor");
    private static readonly int HorizonColorId = Shader.PropertyToID("_HorizonColor");
    private static readonly int DepthDistanceId = Shader.PropertyToID("_DepthDistance");
    private static readonly int RefractionStrengthId = Shader.PropertyToID("_RefractionStrength");
    private static readonly int RefractionColorId = Shader.PropertyToID("_RefractionColor");
    private static readonly int FresnelPowerId = Shader.PropertyToID("_FresnelPower");
    private static readonly int FoamWidthId = Shader.PropertyToID("_FoamWidth");
    private static readonly int FoamIntensityId = Shader.PropertyToID("_FoamIntensity");
    private static readonly int WaveHighlightId = Shader.PropertyToID("_WaveHighlight");
    private static readonly int UseRefractionId = Shader.PropertyToID("_UseRefraction");

    private MaterialPropertyBlock propertyBlock;

    private void OnEnable() => Apply();
    private void OnValidate() => Apply();
    private void Update()
    {
        if (!Application.isPlaying)
            Apply();
    }

    [ContextMenu("Apply Water Visual Settings")]
    public void Apply()
    {
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>(true);
        foreach (MeshRenderer renderer in renderers)
        {
            Transform face = renderer.transform;
            float worldUnitsPerLocalX = face.TransformVector(Vector3.right).magnitude;
            float worldUnitsPerLocalZ = face.TransformVector(Vector3.forward).magnitude;
            Vector4 waveTiling = new Vector4(
                Mathf.Max(0.0001f, worldUnitsPerLocalX * tilesPerMeter),
                Mathf.Max(0.0001f, worldUnitsPerLocalZ * tilesPerMeter), 0f, 0f);

            bool refractive = face.name == "Up" || face.name == "Front";
            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetVector(WaveTilingId, waveTiling);
            propertyBlock.SetFloat(TilesPerMeterId, tilesPerMeter);
            propertyBlock.SetFloat(WaveScaleId, waveScale);
            propertyBlock.SetVector(LegacyWaveSpeedId, waterProWaveSpeed);
            propertyBlock.SetColor(ShallowColorId, shallowColor);
            propertyBlock.SetColor(DeepColorId, deepColor);
            propertyBlock.SetColor(HorizonColorId, horizonColor);
            propertyBlock.SetFloat(DepthDistanceId, depthDistance);
            propertyBlock.SetFloat(RefractionStrengthId, refractionStrength);
            propertyBlock.SetColor(RefractionColorId, refractionColor);
            propertyBlock.SetFloat(FresnelPowerId, fresnelPower);
            propertyBlock.SetFloat(FoamWidthId, foamWidth);
            propertyBlock.SetFloat(FoamIntensityId, foamIntensity);
            propertyBlock.SetFloat(WaveHighlightId, waveHighlight);
            propertyBlock.SetFloat(UseRefractionId, refractive ? 1f : 0f);
            renderer.SetPropertyBlock(propertyBlock);
        }
    }
}
