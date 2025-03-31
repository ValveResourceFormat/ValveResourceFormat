
using System.Linq;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.SoftbodyPhysics;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;
public class FeModelAggregateData
{
    public FeModelAggregateData(KVObject data)
    {
        Data = data;
    }
    /* 
     * Names of all bones and procedural nodes involved in the softbody sim. 
     * Most `nNode` properties in other structures seem to be indices into this list. 
     * The fields `m_nRotLockStaticNodes` and `m_nStaticNodes`  seem to correspond to 
     * the `Simulate` and `Allow Rotation` flags you can set in ClothChains for example, 
     * each describing the number of elements from the beginning of the list.
    */
    public string[] CtrlName => Data.GetArray<string>("m_CtrlName");

    /*
     * Seems to describe ClothChains. The first `m_nRopeCount` entries 
     * are index boundaries into the list itself, each describing a chain while the ones behind
     * that are indices into `m_CtrlName`.
     * So the first rope is between `m_nRopeCount` inclusive and `m_Ropes[0]`
     * exclusive, the second between `m_Ropes[0]` and `m_Ropes[1]` and so on.
    */
    public long[][] Ropes => ropes ??= GetRopes();

    /* 
     * Basic Colliders. `vSphere` encodes the caps in the 
     * form `[x, y, z, r]` where `[x, y, z]` are the point coordinates and `r` the sphere 
     * radius. `nNode` is the index of the parent bone. 
    */
    public SphereCollectionRigid[] SphereRigids
        => sphereRigids ??= Data.GetArray<KVObject>("m_SphereRigids")
        .Select(c => new SphereCollectionRigid(c)).ToArray();

    public SphereCollectionRigid[] TaperedCapsuleRigids
        => taperedCapsuleRigids ??= Data.GetArray<KVObject>("m_TaperedCapsuleRigids")
        .Select(c => new SphereCollectionRigid(c)).ToArray();

    public BoxRigid[] BoxRigids
        => boxRigids ??= Data.GetArray<KVObject>("m_BoxRigids")
        .Select(b => new BoxRigid(b)).ToArray();

    private long[][] GetRopes()
    {
        var nRopes = Data.GetInt32Property("m_nRopes");
        var rawRopes = Data.GetIntegerArray("m_Ropes");
        var ropes = new long[nRopes][];

        if (nRopes > 0)
        {
            ropes[0] = new ArraySegment<long>(rawRopes, nRopes, (int)rawRopes[0]).ToArray();

            for (var i = 1; i < nRopes; i++)
            {
                ropes[i] = new ArraySegment<long>(rawRopes, (int)rawRopes[i - 1], (int)rawRopes[i]).ToArray();
            }
        }

        return ropes;
    }

    private long[][] ropes;

    private SphereCollectionRigid[] sphereRigids;

    private SphereCollectionRigid[] taperedCapsuleRigids;

    private BoxRigid[] boxRigids;

    private KVObject Data;
}
