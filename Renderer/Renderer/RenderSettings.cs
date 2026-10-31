namespace ValveResourceFormat.Renderer;

/// <summary>Rendering toggles shared by all scenes.</summary>
public record struct RenderSettings
{
    /// <summary>Initializes the toggles to their defaults.</summary>
    public RenderSettings()
    {
    }

    /// <summary>
    /// Gets or sets what decides which materials of the nodes are drawn, such as a tool hiding tools materials.
    /// Without one, every material is drawn.
    /// </summary>
    public IDrawFilter? DrawFilter { get; set; }

    /// <summary>Gets or sets the shader id of the active render mode, 0 being the regular lit view.</summary>
    public int RenderMode { get; set; }

    /// <summary>Gets or sets whether fog is drawn.</summary>
    public bool FogEnabled { get; set; } = true;

    /// <summary>Gets or sets whether volumetric fog is drawn.</summary>
    public bool VolumetricFogEnabled { get; set; } = true;

    /// <summary>Gets or sets whether the 3D sky is drawn.</summary>
    public bool ShowSkybox { get; set; } = true;

    /// <summary>Gets or sets whether barn lights are drawn.</summary>
    public bool EnableBarnLights { get; set; } = true;

    /// <summary>Gets or sets whether static geometry casts sun shadows even where the lightmap already holds them.</summary>
    public bool StaticSunShadows { get; set; }

    /// <summary>Gets or sets whether a depth prepass is drawn before opaques.</summary>
    public bool EnableDepthPrepass { get; set; } = true;

    /// <summary>Gets or sets whether the surfaces that accept screen space ambient occlusion are darkened by it. Needs the depth prepass.</summary>
    public bool EnableScreenSpaceAo { get; set; } = true;

    /// <summary>Gets or sets whether aggregates are culled on the GPU and drawn indirectly.</summary>
    public bool EnableIndirectDraws { get; set; } = true;

    /// <summary>Gets or sets whether GPU culled draws are compacted.</summary>
    public bool EnableCompaction { get; set; } = true;

    /// <summary>Gets or sets whether consecutive draws of one mesh and material are drawn by a single multidraw.</summary>
    public bool EnableDrawBatching { get; set; } = true;

    /// <summary>Gets or sets whether GPU culling also tests occlusion.</summary>
    public bool EnableOcclusionCulling { get; set; } = true;

    /// <summary>Gets or sets whether occluded bounds are drawn.</summary>
    public bool OcclusionDebugEnabled { get; set; }

    /// <summary>Gets or sets whether lights are culled per screen tile.</summary>
    public bool EnableTiledLightCulling { get; set; } = true;

    /// <summary>Gets or sets whether PVS culling is enabled.</summary>
    public bool EnablePvsCulling { get; set; } = true;

    /// <summary>Gets or sets whether <c>info_visibility_box</c> volumes cull.</summary>
    public bool EnableVisBoxCulling { get; set; } = true;

    /// <summary>Gets or sets whether all culling is skipped.</summary>
    public bool DisableAllCulling { get; set; }
}
