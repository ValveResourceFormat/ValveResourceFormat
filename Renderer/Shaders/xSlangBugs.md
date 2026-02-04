# List of Slang bugs discovered in our use so far

- extern static const bools are not evaluated as compile time constant in attributes and in conditionals for reflection(only there, code still gets eliminated):
    ```csharp
    extern static const bool testBool = false;
    struct testStruct
    {
    [IsUsed(testBool)] Conditional<float4, testBoolBecauseFuckThisStupidLanguageHolyFuck> testFloat4;
    float testFloat1;
    };
    ``` 
    Will fail with a compiler error: "expression does not evaluate to a compile-time constant". The array length of the conditional type shows up as "unknown"(json) or 2^64 - 2 in reflection API when it comes to the extern in the Conditional generic.

- Logical operators don't work in conditionals as of now:
    ```
    Conditional<Sampler2D, testBool && someOtherStaticConstBool> testSampler;
    //complains about .get() not being a compile time constant when you try to do testSampler.get()
    ```
    See: https://github.com/shader-slang/slang/issues/9855

- Funky code around switch statements without cases can compile fine but cause illegal SPIR-V as output:
    ```
    public bool HandleMaterialRenderModes(inout vec4 outputColor, MaterialProperties_t mat)
    {
        switch (g_iRenderMode)
        {
            if (g_iRenderMode == renderMode_FullBright)
            {
                return true;
            }
            else if (g_iRenderMode == renderMode_Color)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
    ```
    It is a silent fail, as it does not produce a compiler error. This code has been simplified, but it was about the structure.
