namespace ValveResourceFormat
{
    /// <summary>
    /// Resource file block types.
    /// </summary>
    public enum BlockType : uint
    {
        /// <summary>
        /// Undefined or unknown block type.
        /// </summary>
        Undefined = 0,

        /// <summary>
        /// Resource External Reference List. Contains a list of external resource references.
        /// </summary>
        RERL = 'R' | ('E' << 8) | ('R' << 16) | ('L' << 24),

        /// <summary>
        /// Resource Edit Info. Contains dependency and compilation metadata for the resource.
        /// </summary>
        REDI = 'R' | ('E' << 8) | ('D' << 16) | ('I' << 24),

        /// <summary>
        /// Resource Edit Info 2. An updated version of <see cref="REDI"/> stored in KV3 format.
        /// </summary>
        RED2 = 'R' | ('E' << 8) | ('D' << 16) | ('2' << 24),

        /// <summary>
        /// Resource Introspection Manifest. Contains structure and enum definitions for the resource data.
        /// </summary>
        NTRO = 'N' | ('T' << 8) | ('R' << 16) | ('O' << 24),

        /// <summary>
        /// Resource Data. The main data block containing the resource's primary content.
        /// </summary>
        DATA = 'D' | ('A' << 8) | ('T' << 16) | ('A' << 24),

        /// <summary>
        /// Vertex and Index Buffer Information Block. Contains mesh vertex and index buffer data.
        /// </summary>
        VBIB = 'V' | ('B' << 8) | ('I' << 16) | ('B' << 24),

        /// <summary>
        /// Voxel Visibility. Contains voxel-based visibility data.
        /// </summary>
        VXVS = 'V' | ('X' << 8) | ('V' << 16) | ('S' << 24),

        /// <summary>
        /// Particle Snapshot. Contains snapshot data for particle systems.
        /// </summary>
        SNAP = 'S' | ('N' << 8) | ('A' << 16) | ('P' << 24),

        /// <summary>
        /// Control Data. Contains configuration and control data in KV3 format for sounds, models, and other resources.
        /// </summary>
        CTRL = 'C' | ('T' << 8) | ('R' << 16) | ('L' << 24),

        /// <summary>
        /// Mesh Data. Contains mesh geometry data including hitbox sets and material groups.
        /// </summary>
        MDAT = 'M' | ('D' << 8) | ('A' << 16) | ('T' << 24),

        /// <summary>
        /// Morph Data. Contains morph target (flex/blend shape) data for meshes.
        /// </summary>
        MRPH = 'M' | ('R' << 8) | ('P' << 16) | ('H' << 24),

        /// <summary>
        /// Mesh Buffer. An alternative mesh vertex and index buffer format.
        /// </summary>
        MBUF = 'M' | ('B' << 8) | ('U' << 16) | ('F' << 24),

        /// <summary>
        /// Animation Data. Contains skeletal animation data.
        /// </summary>
        ANIM = 'A' | ('N' << 8) | ('I' << 16) | ('M' << 24),

        /// <summary>
        /// Animation Sequence. Contains animation sequence group data.
        /// </summary>
        ASEQ = 'A' | ('S' << 8) | ('E' << 16) | ('Q' << 24),

        /// <summary>
        /// Animation Group. Contains animation group resource data.
        /// </summary>
        AGRP = 'A' | ('G' << 8) | ('R' << 16) | ('P' << 24),

        /// <summary>
        /// Physics Aggregate Data. Contains physics collision meshes and constraint data.
        /// </summary>
        PHYS = 'P' | ('H' << 8) | ('Y' << 16) | ('S' << 24),

        /// <summary>
        /// Input Signature. Contains shader input layout and signature data for materials.
        /// </summary>
        INSG = 'I' | ('N' << 8) | ('S' << 16) | ('G' << 24),

        /// <summary>
        /// Source Map. Contains source mapping data for Panorama CSS files.
        /// </summary>
        SrMa = 'S' | ('r' << 8) | ('M' << 16) | ('a' << 24),

        /// <summary>
        /// Layout Content. Contains parsed VXML AST data for Panorama layouts.
        /// </summary>
        LaCo = 'L' | ('a' << 8) | ('C' << 16) | ('o' << 24),

        /// <summary>
        /// Statistics. Contains compiled JavaScript metadata (e.g., public methods) for Panorama scripts.
        /// </summary>
        STAT = 'S' | ('T' << 8) | ('A' << 16) | ('T' << 24),

        /// <summary>
        /// SPIR-V Shader. Contains compiled SPIR-V shader bytecode for S&amp;box.
        /// </summary>
        SPRV = 'S' | ('P' << 8) | ('R' << 16) | ('V' << 24),

        /// <summary>
        /// File/Line/Column Information. Contains source location data (file, line, column) for compiled vdata files.
        /// </summary>
        FLCI = 'F' | ('L' << 8) | ('C' << 16) | ('I' << 24),

        /// <summary>
        /// Distance Field. Contains distance field data in KV3 format.
        /// </summary>
        DSTF = 'D' | ('S' << 8) | ('T' << 16) | ('F' << 24),

        /// <summary>
        /// Tools Buffer. Contains vertex buffer data for unused attributes (tools only).
        /// </summary>
        TBUF = 'T' | ('B' << 8) | ('U' << 16) | ('F' << 24),

        /// <summary>
        /// Mesh Vertex Buffer. Contains vertex buffer data for meshes.
        /// </summary>
        MVTX = 'M' | ('V' << 8) | ('T' << 16) | ('X' << 24),

        /// <summary>
        /// Mesh Index Buffer. Contains index buffer data for meshes.
        /// </summary>
        MIDX = 'M' | ('I' << 8) | ('D' << 16) | ('X' << 24),
    }
}
