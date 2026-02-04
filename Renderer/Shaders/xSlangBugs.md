# List of Slang bugs discovered in our use so far

- Link-time constants are not evaluated as compile time constant in conditionals for reflection(only there, code still gets eliminated):
    ```csharp
    extern static const bool testBool = false;
    struct testStruct
    {
    Conditional<float4, testBool> testFloat4;
    float testFloat1;
    };
    ``` 
    Will fail with a compiler error: "expression does not evaluate to a compile-time constant". The array length of the conditional type shows up as "unknown"(json) or 2^64 - 2 in reflection API when it comes to the extern in the Conditional generic.

- Link-time constants are not evaluated as compile time constant for (user-defined) attributes. See: https://github.com/shader-slang/slang/issues/9891

- Logical operators don't work in conditionals as of now:
    ```csharp
    Conditional<Sampler2D, testBool && someOtherStaticConstBool> testSampler;
    //complains about .get() not being a compile time constant when you try to do testSampler.get()
    ```
    See: https://github.com/shader-slang/slang/issues/9855
    This was partially resolved already in #9833, but only for AND and OR, not NOT.


- Funky code around switch statements without cases can compile fine but cause illegal SPIR-V as output:
    ```csharp
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
