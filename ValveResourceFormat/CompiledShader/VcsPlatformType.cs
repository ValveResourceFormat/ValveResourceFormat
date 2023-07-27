namespace ValveResourceFormat.CompiledShader
{
    /*
     * PCGL and MOBILE_GLES are working
     * PC, VULKAN, IOS_VULKAN, ANDROID_VULKAN parse without error, but lack decompiling source (source may be viewed as bytecode)
     * X360, MAC are not implemented
     *
     */
    public enum VcsPlatformType
    {
        VULKAN,
        PC,
        PCGL,
        X360,
        MAC,
        MOBILE_GLES,
        IOS_VULKAN,
        ANDROID_VULKAN,
        Undetermined,
    }
}
