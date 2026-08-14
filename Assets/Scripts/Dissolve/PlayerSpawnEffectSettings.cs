using UnityEngine;

[CreateAssetMenu(menuName = "SpikeBall/Player Spawn Effect Settings", fileName = "PlayerSpawnEffectSettings")]
public sealed class PlayerSpawnEffectSettings : ScriptableObject
{
    [Header("Timing")]
    [Min(0f)] public float spawnDelay = 0.18f;
    [Min(0.1f)] public float duration = 2.35f;
    [Min(0.1f)] public float postFadeDuration = 0.45f;

    [Header("Dissolve Surface")]
    [Min(0.1f)] public float noiseScale = 4.8f;
    [Range(0.01f, 0.5f)] public float noiseStrength = 0.2f;
    [Range(0.005f, 0.3f)] public float edgeWidth = 0.03f;
    [ColorUsage(true, true)] public Color hotEdgeColor = new Color(0.2f, 8f, 12f, 1f);
    [ColorUsage(true, true)] public Color coolEdgeColor = new Color(0.1f, 1.8f, 10f, 1f);
    [Range(0f, 5f)] public float edgeIntensity = 1.45f;
    [ColorUsage(true, true)] public Color hologramTint = new Color(0.03f, 0.25f, 1.6f, 1f);
    [Range(0f, 2f)] public float fresnelStrength = 0.45f;

    [Header("Energy Accents")]
    [Min(0f)] public float particleRate = 105f;
    [Min(0f)] public float boundaryLightIntensity = 3.2f;
    [Min(0f)] public float bloomIntensity = 1.15f;
    [Range(0f, 1f)] public float bloomScatter = 0.72f;
    [Range(0f, 1f)] public float vignetteIntensity = 0.12f;

    [Header("Integration")]
    public string gameplayScenePrefix = "level";
    public bool lockPlayerDuringSpawn = true;
    [Min(1)] public int playerSearchFrames = 180;
}
