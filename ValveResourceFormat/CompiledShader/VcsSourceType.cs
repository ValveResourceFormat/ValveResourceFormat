namespace ValveResourceFormat.ShaderParser
{
    /*
     * type labels found in the game code are
     * "PC"
     * "PCGL"
     * "X360"
     * "MAC"
     * "VULKAN"
     * "MOBILE_GLES"
     * "IOS_VULKAN"
     * "ANDROID_VULKAN"
     *
     */
    public enum VcsSourceType {
        Glsl,
        DXIL,
        DXBC,
        Vulkan,
    }
}
