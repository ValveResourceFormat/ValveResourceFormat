using System;
using SteamDatabase.ValvePak;

namespace ValveResourceFormat;

public class VrfContext : IDisposable
{
    public virtual string FileName { get { return CurrentPackage.FileName; } }

    public Package CurrentPackage { get; set; }

    public VrfContext(Package package)
    {
        CurrentPackage = package;
    }

    protected virtual void Dispose(bool disposing) { }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
