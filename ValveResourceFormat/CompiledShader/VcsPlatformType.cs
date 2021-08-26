namespace ValveResourceFormat.CompiledShader
{
    /*
     * PCGL and MOBILE_GLES work well
     * PC, VULKAN, IOS_VULKAN, ANDROID_VULKAN parse without error, but lack decompiling source (source may be viewed as bytecode)
     * X360, MAC are not implemented
     *
     */
    public enum VcsPlatformType {
        PC,
        PCGL,
        X360,
        MAC,
        VULKAN,
        MOBILE_GLES,
        IOS_VULKAN,
        ANDROID_VULKAN,
        Undetermined,
    }
}
