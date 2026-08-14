# Unity URP Shader Showcase

Portfolio source code for two real-time rendering effects built in Unity URP:

- Stylized volumetric cube water
- Procedural player-spawn dissolve and particles

The repository focuses on shader architecture and runtime control code. It intentionally excludes the original game, third-party assets, paid packages, and production textures.

## Stylized Volumetric Water

The water shader reconstructs a stylized WaterPro-inspired look for URP and adapts it to a cube-shaped water volume.

Key features:

- Dual-layer animated normal sampling
- Fresnel-driven horizon response
- URP Scene Color refraction
- Scene Depth-based water thickness and color gradient
- Edge foam based on scene intersection depth
- Scale-independent world-density tiling
- Per-face refraction control through `MaterialPropertyBlock`
- Reduced refraction work on less important cube faces

Core files:

- `Assets/Shaders/Water/StylizedWaterVolumeURP.shader`
- `Assets/Scripts/Water/WaterURPVisualController.cs`

### Setup notes

The shader expects a wave normal texture, a Fresnel lookup texture, and a reflective color gradient. The original project textures are not included because they come from third-party or legacy sample content. Use your own textures with equivalent roles.

URP settings required for the full result:

- Opaque Texture: enabled
- Depth Texture: enabled

The controller expects child renderers named `Up` and `Front` when refraction should be enabled on those faces.

## Procedural Spawn Dissolve

The dissolve effect uses procedural 3D value noise and a height-based dissolve field. It does not require a dissolve mask texture.

Key features:

- Procedural FBM-style noise
- Height-directed dissolve progression
- Dual-color HDR edge emission
- Fresnel hologram tint
- Vertex displacement near the dissolve front
- Companion particle shader and runtime settings

Core files:

- `Assets/Shaders/Dissolve/PlayerSpawnDissolve.shader`
- `Assets/Shaders/Dissolve/PlayerSpawnParticles.shader`
- `Assets/Scripts/Dissolve/PlayerSpawnEffectSettings.cs`
- `Assets/Scripts/Dissolve/GlobalPlayerSpawnEffect.cs`

## Environment

- Unity Universal Render Pipeline
- HLSL / ShaderLab
- C#

## Repository scope

This is a portfolio code showcase, not a complete redistributable Unity game project. Game assets, models, audio, scenes, third-party packages, and production credentials are deliberately omitted.

Copyright (c) 2026 Lin Zhuona. No license is granted for redistribution or commercial use.
