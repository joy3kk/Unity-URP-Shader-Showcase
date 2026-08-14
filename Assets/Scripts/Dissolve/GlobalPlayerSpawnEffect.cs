using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-1000)]
public sealed class GlobalPlayerSpawnEffect : MonoBehaviour
{
    private const string DissolveShaderName = "SpikeBall/Effects/Player Spawn Dissolve";
    private const string ParticleShaderName = "SpikeBall/Effects/Player Spawn Particles";
    private const string SettingsResourceName = "PlayerSpawnEffectSettings";

    private sealed class RendererState
    {
        public Renderer renderer;
        public Material[] originalMaterials;
        public Material[] effectMaterials;
    }

    private sealed class RigidbodyState
    {
        public Rigidbody body;
        public bool wasKinematic;
        public RigidbodyConstraints constraints;
    }

    private sealed class BehaviourState
    {
        public Behaviour behaviour;
        public bool enabled;
    }

    private sealed class CameraState
    {
        public UniversalAdditionalCameraData data;
        public bool postProcessing;
    }

    // ===== 传送门静态扩展（Portal 三组件共享契约）=====
    /// <summary>传送门：设为 true 时抑制下一帧 sceneLoaded 的出生 dissolve（读取后置 false 并返回，跳过整个 dissolve/LockPlayer）。</summary>
    public static bool SuppressSpawnEffect;

    /// <summary>传送门：非空时在播出生 dissolve 前把玩家锚定到该位置（溶解/粒子/后处理锚定目标出口洞），应用后清空为 null。</summary>
    public static Vector3? SpawnPositionOverride;

    /// <summary>传送门：出生 dissolve 正常结束（CleanupTransientFx 之后、协程结束处）触发，供外部施加出口弹射。</summary>
    public static event System.Action EffectCompleted;

    private static GlobalPlayerSpawnEffect instance;
    private PlayerSpawnEffectSettings settings;
    private Coroutine pendingRoutine;
    private Coroutine effectRoutine;
    private readonly List<RendererState> rendererStates = new List<RendererState>();
    private readonly List<RigidbodyState> rigidbodyStates = new List<RigidbodyState>();
    private readonly List<BehaviourState> behaviourStates = new List<BehaviourState>();
    private readonly List<CameraState> cameraStates = new List<CameraState>();
    private readonly List<Material> allocatedMaterials = new List<Material>();
    private GameObject activeFxRoot;
    private ParticleSystem boundaryParticles;
    private Material boundaryParticleMaterial;
    private Light boundaryLight;
    private Volume effectVolume;
    private VolumeProfile runtimeVolumeProfile;
    private bool previousGameplayInputBlocked;
    private bool controlsLocked;
    private bool restoring;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<GlobalPlayerSpawnEffect>() != null) return;
        var go = new GameObject("[Global] Player Spawn Effect");
        go.AddComponent<GlobalPlayerSpawnEffect>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        settings = Resources.Load<PlayerSpawnEffectSettings>(SettingsResourceName);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<PlayerSpawnEffectSettings>();
            settings.hideFlags = HideFlags.HideAndDontSave;
            Debug.LogWarning("[PlayerSpawnEffect] Resources/PlayerSpawnEffectSettings missing; using runtime defaults.");
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void Start()
    {
        // sceneLoaded normally schedules the effect before the first rendered frame.
        // This only covers unusual startup paths where that callback did not run.
        if (pendingRoutine == null && effectRoutine == null)
            ScheduleForScene(SceneManager.GetActiveScene());
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        RestoreImmediate(true);
        if (settings != null && (settings.hideFlags & HideFlags.DontSave) != 0) Destroy(settings);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 传送门：本帧要执行出生 dissolve 的入口处最先检查抑制标记。
        // 若被抑制则清空标记并返回，跳过整个 dissolve/LockPlayer 流程（传送场景不再播出生特效）。
        if (SuppressSpawnEffect)
        {
            SuppressSpawnEffect = false;
            return;
        }

        ScheduleForScene(scene);
    }

    private void ScheduleForScene(Scene scene)
    {
        if (!IsGameplayScene(scene)) return;
        if (pendingRoutine != null) StopCoroutine(pendingRoutine);
        if (effectRoutine != null) StopCoroutine(effectRoutine);
        RestoreImmediate(true);

        // sceneLoaded is invoked before the scene's first rendered frame. StartCoroutine
        // executes synchronously until its first yield, so applying the dissolve here keeps
        // the original player material from flashing for one frame.
        GameObject playerRoot = FindPlayerRoot(scene);
        if (playerRoot != null)
        {
            effectRoutine = StartCoroutine(PlaySpawnEffect(playerRoot));
            return;
        }

        pendingRoutine = StartCoroutine(WaitForPlayerAndPlay(scene));
    }

    private bool IsGameplayScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return false;
        string prefix = settings != null ? settings.gameplayScenePrefix : "level";
        if (string.IsNullOrWhiteSpace(prefix)) return true;
        // 支持逗号分隔多前缀：如 "level,the"（thecenter/theleft/theupper 三枢纽触发出生 dissolve）
        string[] prefixes = prefix.Split(',');
        for (int i = 0; i < prefixes.Length; i++)
        {
            string p = prefixes[i].Trim();
            if (string.IsNullOrWhiteSpace(p)) continue;
            if (scene.name.StartsWith(p, System.StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private IEnumerator WaitForPlayerAndPlay(Scene scene)
    {
        int frames = Mathf.Max(1, settings.playerSearchFrames);
        for (int i = 0; i < frames; i++)
        {
            if (!scene.IsValid() || !scene.isLoaded) yield break;
            GameObject playerRoot = FindPlayerRoot(scene);
            if (playerRoot != null)
            {
                pendingRoutine = null;
                effectRoutine = StartCoroutine(PlaySpawnEffect(playerRoot));
                yield break;
            }
            yield return null;
        }

        pendingRoutine = null;
        Debug.LogWarning($"[PlayerSpawnEffect] No player found in scene '{scene.name}' after {frames} frames.");
    }

    /// <summary>
    /// 三分支查找指定场景中的玩家根节点：优先 PlayerHealth → PlayerController → BeginnerRollController。
    /// public 供 PortalManager 复用于传送出口定位。
    /// </summary>
    public static GameObject FindPlayerRoot(Scene scene)
    {
        PlayerHealth ddol = FindObjectsOfType<PlayerHealth>(true)
            .FirstOrDefault(h => h != null && IsDontDestroyOnLoad(h.gameObject.scene));
        if (ddol != null) return ddol.transform.root.gameObject;

        PlayerHealth health = FindObjectsOfType<PlayerHealth>(true)
            .FirstOrDefault(h => h != null && h.gameObject.scene == scene);
        if (health != null) return health.transform.root.gameObject;

        PlayerController player = FindObjectsOfType<PlayerController>(true)
            .FirstOrDefault(p => p != null && p.gameObject.scene == scene);
        if (player != null) return player.transform.root.gameObject;

        BeginnerRollController beginner = FindObjectsOfType<BeginnerRollController>(true)
            .FirstOrDefault(p => p != null && p.gameObject.scene == scene);
        return beginner != null ? beginner.transform.root.gameObject : null;
    }

    static bool IsDontDestroyOnLoad(Scene s)
    {
        return !s.IsValid() || s.name == "DontDestroyOnLoad";
    }

    private IEnumerator PlaySpawnEffect(GameObject playerRoot)
    {
        Shader dissolveShader = Shader.Find(DissolveShaderName);
        if (dissolveShader == null)
        {
            Debug.LogError($"[PlayerSpawnEffect] Shader '{DissolveShaderName}' not found.");
            effectRoutine = null;
            yield break;
        }

        Renderer[] renderers = playerRoot.GetComponentsInChildren<Renderer>(true)
            .Where(r => r != null && r.enabled &&
                        !(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !(r is LineRenderer))
            .ToArray();
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"[PlayerSpawnEffect] Player '{playerRoot.name}' has no supported renderers.");
            effectRoutine = null;
            yield break;
        }

        // 首帧先隐藏玩家（替代"同步段换 dissolve 材质"的防闪帧手段）：sceneLoaded 派发
        // 尚未完成时，PortalManager 可能还没写 SpawnPositionOverride（两单例订阅顺序未定义），
        // 故先让出一帧等派发结束，再读 override 并定位玩家，保证 dissolve 锚定出口洞而非出生位。
        SetRenderersEnabled(renderers, false);
        yield return null;
        SetRenderersEnabled(renderers, true);

        // 传送门：读取玩家 renderer bounds 之前应用出生位置覆盖。
        // 若传送门已设置目标出口坐标，先把玩家根定位过去，使溶解/粒子/后处理锚定出口洞；应用后清空为 null。
        if (SpawnPositionOverride.HasValue)
        {
            playerRoot.transform.position = SpawnPositionOverride.Value;
            SpawnPositionOverride = null;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        float padding = Mathf.Max(0.04f, bounds.size.y * 0.035f);
        float minY = bounds.min.y - padding;
        float maxY = bounds.max.y + padding;

        PrepareRenderers(renderers, dissolveShader, minY, maxY, playerRoot.GetInstanceID());
        if (settings.lockPlayerDuringSpawn) LockPlayer(playerRoot);
        SetupPostProcessing();
        SetupBoundaryFx(bounds);
        SetDissolveProgress(0f);

        // Render one fully hidden frame before advancing the animation. This discards
        // the oversized delta time Unity can report immediately after a scene load,
        // while still preventing the original material from flashing.
        yield return null;

        float delay = Mathf.Max(0f, settings.spawnDelay);
        while (delay > 0f)
        {
            delay -= GetEffectDeltaTime();
            UpdateBoundaryFx(bounds, 0f, 0f);
            yield return null;
        }

        float elapsed = 0f;
        float duration = Mathf.Max(0.1f, settings.duration);
        while (elapsed < duration)
        {
            elapsed += GetEffectDeltaTime();
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);
            SetDissolveProgress(eased);
            UpdateBoundaryFx(bounds, eased, t);
            if (effectVolume != null) effectVolume.weight = Mathf.SmoothStep(0.2f, 1f, Mathf.Sin(t * Mathf.PI));
            yield return null;
        }

        SetDissolveProgress(1f);
        RestoreMaterialsAndControls();
        StopBoundaryEmission();

        float fadeDuration = Mathf.Max(0.1f, settings.postFadeDuration);
        float fade = 0f;
        while (fade < fadeDuration)
        {
            fade += GetEffectDeltaTime();
            float remaining = 1f - Mathf.Clamp01(fade / fadeDuration);
            if (effectVolume != null) effectVolume.weight = remaining;
            if (boundaryLight != null) boundaryLight.intensity = settings.boundaryLightIntensity * remaining;
            yield return null;
        }

        CleanupTransientFx(false);
        effectRoutine = null;
        Debug.Log($"[PlayerSpawnEffect] Spawn dissolve completed for '{playerRoot.name}' in scene '{playerRoot.scene.name}'.");

        // 传送门：dissolve 特效正常结束（CleanupTransientFx 之后、协程结束处）触发。
        // 弹射时序铁律：出口弹射必须在此事件之后施加，绕开 LockPlayer/RestoreMaterialsAndControls 等清零竞态。
        EffectCompleted?.Invoke();
    }

    private static float GetEffectDeltaTime()
    {
        // Scene activation, shader warm-up or a breakpoint must not skip the dissolve sweep.
        return Mathf.Min(Time.unscaledDeltaTime, 0.05f);
    }

    private void PrepareRenderers(Renderer[] renderers, Shader dissolveShader, float minY, float maxY, int seed)
    {
        rendererStates.Clear();
        allocatedMaterials.Clear();
        var random = new System.Random(seed);

        foreach (Renderer renderer in renderers)
        {
            Material[] originals = renderer.sharedMaterials;
            Material[] effects = new Material[originals.Length];
            for (int i = 0; i < originals.Length; i++)
            {
                Material effect = new Material(dissolveShader)
                {
                    name = $"{renderer.name}_SpawnDissolve_{i}",
                    hideFlags = HideFlags.HideAndDontSave
                };
                CopySurface(originals[i], effect);
                effect.SetFloat("_DissolveMinY", minY);
                effect.SetFloat("_DissolveMaxY", maxY);
                effect.SetFloat("_DissolveNoiseScale", settings.noiseScale);
                effect.SetFloat("_DissolveNoiseStrength", settings.noiseStrength);
                effect.SetFloat("_DissolveEdgeWidth", settings.edgeWidth);
                effect.SetColor("_EdgeColor", settings.hotEdgeColor);
                effect.SetColor("_EdgeColor2", settings.coolEdgeColor);
                effect.SetFloat("_EdgeIntensity", settings.edgeIntensity);
                effect.SetColor("_HologramTint", settings.hologramTint);
                effect.SetFloat("_FresnelStrength", settings.fresnelStrength);
                effect.SetVector("_NoiseOffset", new Vector4(
                    (float)random.NextDouble() * 19f,
                    (float)random.NextDouble() * 19f,
                    (float)random.NextDouble() * 19f,
                    0f));
                effects[i] = effect;
                allocatedMaterials.Add(effect);
            }

            rendererStates.Add(new RendererState
            {
                renderer = renderer,
                originalMaterials = originals,
                effectMaterials = effects
            });
            renderer.sharedMaterials = effects;
        }
    }

    private static void CopySurface(Material source, Material target)
    {
        if (source == null)
        {
            target.SetTexture("_BaseMap", Texture2D.whiteTexture);
            target.SetColor("_BaseColor", Color.white);
            return;
        }

        string textureProperty = source.HasProperty("_BaseMap") ? "_BaseMap" : (source.HasProperty("_MainTex") ? "_MainTex" : null);
        if (textureProperty != null)
        {
            Texture texture = source.GetTexture(textureProperty);
            target.SetTexture("_BaseMap", texture != null ? texture : Texture2D.whiteTexture);
            target.SetTextureScale("_BaseMap", source.GetTextureScale(textureProperty));
            target.SetTextureOffset("_BaseMap", source.GetTextureOffset(textureProperty));
        }
        else target.SetTexture("_BaseMap", Texture2D.whiteTexture);

        if (source.HasProperty("_BaseColor")) target.SetColor("_BaseColor", source.GetColor("_BaseColor"));
        else if (source.HasProperty("_BodyColor")) target.SetColor("_BaseColor", source.GetColor("_BodyColor"));
        else if (source.HasProperty("_Color")) target.SetColor("_BaseColor", source.GetColor("_Color"));
        else target.SetColor("_BaseColor", Color.white);

        if (source.HasProperty("_Metallic")) target.SetFloat("_Metallic", source.GetFloat("_Metallic"));
        if (source.HasProperty("_Smoothness")) target.SetFloat("_Smoothness", source.GetFloat("_Smoothness"));
        else if (source.HasProperty("_Glossiness")) target.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
    }

    private void SetDissolveProgress(float progress)
    {
        for (int i = 0; i < allocatedMaterials.Count; i++)
            if (allocatedMaterials[i] != null) allocatedMaterials[i].SetFloat("_DissolveProgress", progress);
    }

    /// <summary>批量启停玩家 renderer（用于 dissolve 前隐藏一帧，防止等待 sceneLoaded 派发完成时原材质闪帧）。</summary>
    private static void SetRenderersEnabled(Renderer[] renderers, bool enabled)
    {
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].enabled = enabled;
    }

    private void LockPlayer(GameObject playerRoot)
    {
        controlsLocked = true;
        previousGameplayInputBlocked = PlayerController.GameplayInputBlocked;
        PlayerController.GameplayInputBlocked = true;
        behaviourStates.Clear();
        // 传送门：FootMagneticCarry 不在锁定列表——跨场景传送恢复携带物后若被禁用会触发 OnDisable→Detach 把携带物拆下掉落。
        string[] lockedTypes = { "PlayerController", "BeginnerRollController", "PlayerMagnet", "FootMagnetController" };
        foreach (MonoBehaviour behaviour in playerRoot.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || !lockedTypes.Contains(behaviour.GetType().Name)) continue;
            behaviourStates.Add(new BehaviourState { behaviour = behaviour, enabled = behaviour.enabled });
            behaviour.enabled = false;
        }

        rigidbodyStates.Clear();
        foreach (Rigidbody body in playerRoot.GetComponentsInChildren<Rigidbody>(true))
        {
            rigidbodyStates.Add(new RigidbodyState
            {
                body = body,
                wasKinematic = body.isKinematic,
                constraints = body.constraints
            });
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }
    }

    private void SetupPostProcessing()
    {
        cameraStates.Clear();
        foreach (UniversalAdditionalCameraData data in FindObjectsOfType<UniversalAdditionalCameraData>(true))
        {
            Camera camera = data.GetComponent<Camera>();
            if (camera == null || !camera.isActiveAndEnabled) continue;
            cameraStates.Add(new CameraState { data = data, postProcessing = data.renderPostProcessing });
            data.renderPostProcessing = true;
        }

        var volumeGo = new GameObject("Spawn Dissolve Post FX");
        volumeGo.transform.SetParent(transform, false);
        effectVolume = volumeGo.AddComponent<Volume>();
        effectVolume.isGlobal = true;
        effectVolume.priority = 1000f;
        effectVolume.weight = 0f;
        runtimeVolumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        runtimeVolumeProfile.hideFlags = HideFlags.HideAndDontSave;
        effectVolume.profile = runtimeVolumeProfile;

        Bloom bloom = runtimeVolumeProfile.Add<Bloom>(true);
        bloom.threshold.value = 0.65f;
        bloom.intensity.value = settings.bloomIntensity;
        bloom.scatter.value = settings.bloomScatter;
        bloom.clamp.value = 24f;

        Vignette vignette = runtimeVolumeProfile.Add<Vignette>(true);
        vignette.color.value = new Color(0.02f, 0.015f, 0.08f, 1f);
        vignette.intensity.value = settings.vignetteIntensity;
        vignette.smoothness.value = 0.72f;
    }

    private void SetupBoundaryFx(Bounds bounds)
    {
        activeFxRoot = new GameObject("Player Spawn Energy Boundary");
        activeFxRoot.transform.SetParent(transform, false);
        activeFxRoot.transform.position = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

        boundaryParticles = activeFxRoot.AddComponent<ParticleSystem>();
        boundaryParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = boundaryParticles.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = Mathf.Max(0.5f, settings.duration);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 1.25f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, Mathf.Max(0.06f, bounds.size.y * 0.045f));
        main.startColor = new ParticleSystem.MinMaxGradient(
            NormalizeHdrColor(settings.hotEdgeColor, 0.92f),
            NormalizeHdrColor(settings.coolEdgeColor, 0.88f));
        main.maxParticles = 600;

        ParticleSystem.EmissionModule emission = boundaryParticles.emission;
        emission.rateOverTime = settings.particleRate;

        ParticleSystem.ShapeModule shape = boundaryParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(Mathf.Max(0.2f, bounds.size.x * 1.08f), Mathf.Max(0.025f, bounds.size.y * 0.025f), Mathf.Max(0.2f, bounds.size.z * 1.08f));

        ParticleSystem.VelocityOverLifetimeModule velocity = boundaryParticles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.35f, 0.95f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);

        ParticleSystem.NoiseModule noise = boundaryParticles.noise;
        noise.enabled = true;
        noise.quality = ParticleSystemNoiseQuality.High;
        noise.strength = 0.24f;
        noise.frequency = 0.72f;
        noise.scrollSpeed = 0.35f;

        ParticleSystem.ColorOverLifetimeModule colorOverLife = boundaryParticles.colorOverLifetime;
        colorOverLife.enabled = true;
        Color brightParticleColor = NormalizeHdrColor(settings.hotEdgeColor, 1f);
        Color deepParticleColor = NormalizeHdrColor(settings.coolEdgeColor, 1f);
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(brightParticleColor, 0f), new GradientColorKey(brightParticleColor, 0.45f), new GradientColorKey(deepParticleColor, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = gradient;

        ParticleSystemRenderer particleRenderer = activeFxRoot.GetComponent<ParticleSystemRenderer>();
        Shader particleShader = Shader.Find(ParticleShaderName);
        if (particleShader != null)
        {
            Material particleMaterial = new Material(particleShader)
            {
                name = "PlayerSpawnParticles_Runtime",
                hideFlags = HideFlags.HideAndDontSave
            };
            particleMaterial.SetColor("_TintColor", settings.hologramTint);
            particleRenderer.sharedMaterial = particleMaterial;
            boundaryParticleMaterial = particleMaterial;
            allocatedMaterials.Add(particleMaterial);
        }
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.sortingOrder = 50;
        boundaryParticles.Play(true);

        var lightGo = new GameObject("Spawn Boundary Light");
        lightGo.transform.SetParent(activeFxRoot.transform, false);
        boundaryLight = lightGo.AddComponent<Light>();
        boundaryLight.type = LightType.Point;
        boundaryLight.color = NormalizeHdrColor(settings.hotEdgeColor, 1f);
        boundaryLight.range = Mathf.Max(2.5f, bounds.size.magnitude * 1.35f);
        boundaryLight.intensity = 0f;
        boundaryLight.shadows = LightShadows.None;
    }

    private static Color NormalizeHdrColor(Color color, float alpha)
    {
        float peak = Mathf.Max(1f, color.r, color.g, color.b);
        return new Color(color.r / peak, color.g / peak, color.b / peak, alpha);
    }

    private void UpdateBoundaryFx(Bounds bounds, float progress, float normalizedTime)
    {
        if (activeFxRoot != null)
        {
            float y = Mathf.Lerp(bounds.min.y, bounds.max.y, progress);
            activeFxRoot.transform.position = new Vector3(bounds.center.x, y, bounds.center.z);
        }
        if (boundaryLight != null)
        {
            float pulse = Mathf.Sin(Mathf.Clamp01(normalizedTime) * Mathf.PI);
            boundaryLight.intensity = settings.boundaryLightIntensity * pulse * (0.82f + Mathf.Sin(Time.unscaledTime * 23f) * 0.18f);
        }
    }

    private void StopBoundaryEmission()
    {
        if (boundaryParticles != null)
        {
            ParticleSystem.EmissionModule emission = boundaryParticles.emission;
            emission.enabled = false;
            boundaryParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private void RestoreMaterialsAndControls()
    {
        for (int i = 0; i < rendererStates.Count; i++)
        {
            RendererState state = rendererStates[i];
            if (state.renderer != null) state.renderer.sharedMaterials = state.originalMaterials;
        }
        rendererStates.Clear();

        if (controlsLocked)
        {
            for (int i = 0; i < behaviourStates.Count; i++)
                if (behaviourStates[i].behaviour != null) behaviourStates[i].behaviour.enabled = behaviourStates[i].enabled;
            for (int i = 0; i < rigidbodyStates.Count; i++)
            {
                RigidbodyState state = rigidbodyStates[i];
                if (state.body == null) continue;
                state.body.isKinematic = state.wasKinematic;
                state.body.constraints = state.constraints;
                state.body.velocity = Vector3.zero;
                state.body.angularVelocity = Vector3.zero;
            }
            PlayerController.GameplayInputBlocked = previousGameplayInputBlocked;
            controlsLocked = false;
        }
        behaviourStates.Clear();
        rigidbodyStates.Clear();
    }

    private void CleanupTransientFx(bool immediate)
    {
        for (int i = 0; i < cameraStates.Count; i++)
            if (cameraStates[i].data != null) cameraStates[i].data.renderPostProcessing = cameraStates[i].postProcessing;
        cameraStates.Clear();

        if (effectVolume != null) Destroy(effectVolume.gameObject);
        effectVolume = null;
        if (runtimeVolumeProfile != null) Destroy(runtimeVolumeProfile);
        runtimeVolumeProfile = null;

        if (activeFxRoot != null)
        {
            if (immediate) Destroy(activeFxRoot);
            else Destroy(activeFxRoot, 1.35f);
        }
        activeFxRoot = null;
        boundaryParticles = null;
        boundaryLight = null;

        for (int i = 0; i < allocatedMaterials.Count; i++)
        {
            Material material = allocatedMaterials[i];
            if (material == null) continue;
            if (!immediate && material == boundaryParticleMaterial)
                Destroy(material, 1.4f);
            else
                Destroy(material);
        }
        allocatedMaterials.Clear();
        boundaryParticleMaterial = null;
    }

    private void RestoreImmediate(bool immediateFx)
    {
        if (restoring) return;
        restoring = true;
        RestoreMaterialsAndControls();
        CleanupTransientFx(immediateFx);
        restoring = false;
    }

    public static void PlayForCurrentScene()
    {
        if (instance == null) Bootstrap();
        if (instance != null) instance.ScheduleForScene(SceneManager.GetActiveScene());
    }
}
