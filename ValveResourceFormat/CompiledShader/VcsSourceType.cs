namespace ValveResourceFormat.ShaderParser
{
    /*
     * platform enums found in the game code are
     * "PC"
     * "PCGL"
     * "X360"
     * "MAC"
     * "VULKAN"
     * "MOBILE_GLES"
     * "IOS_VULKAN"
     * "ANDROID_VULKAN"
     *
     * PCGL and MOBILE_GLES work well
     * PC, VULKAN, IOS_VULKAN, ANDROID_VULKAN parse without error, but lack decompiling source (source may be viewed as bytecode)
     * X360, MAC are not implemented
     *
     *
     */
    public enum VcsSourceType {
        Glsl,       // "PCGL"
        DXIL,       // "PC" is seen as two sub-types, DXIL and DXBC (all v.30 files are DXIL encoded, all v.40+ are DXBC)
        DXBC,
        X360,
        Mac,
        Vulkan,
        MobileGles,
        IosVulkan,
        AndroidVulkan,
    }
}
