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
     * PCGL is implemented
     * PC and VULKAN are correctly parsed, but lack support for exporting source (source may be viewed as bytecode)
     * The rest are not implementated (and have not been attempted)
     *
     *
     */
    public enum VcsSourceType {
        Glsl,       // "PCGL"
        DXIL,       // "PC" is seen as two sub-types, DXIL and DXBC (all v.30 files are DXIL encoded, all v.40+ are DXBC)
        DXBC,
        Vulkan,
    }
}
