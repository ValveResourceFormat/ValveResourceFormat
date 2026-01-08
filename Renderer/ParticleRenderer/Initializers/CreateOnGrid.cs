using GUI.Utils;

namespace GUI.Types.ParticleRenderer.Initializers
{
    // this single initializer delayed this release by months
    class CreateOnGrid : ParticleFunctionInitializer
    {
        private readonly INumberProvider dimenX = new LiteralNumberProvider(0);
        private readonly INumberProvider dimenY = new LiteralNumberProvider(0);
        private readonly INumberProvider dimenZ = new LiteralNumberProvider(0);

        private readonly INumberProvider spacingX = new LiteralNumberProvider(0);
        private readonly INumberProvider spacingY = new LiteralNumberProvider(0);
        private readonly INumberProvider spacingZ = new LiteralNumberProvider(0);

        private readonly int controlPointNumber;
        private readonly bool center = true;
        private readonly bool hollow; // misery
        //private readonly bool localSpace;

        public CreateOnGrid(ParticleDefinitionParser parse) : base(parse)
        {
            dimenX = parse.NumberProvider("m_nXCount", dimenX);
            dimenY = parse.NumberProvider("m_nYCount", dimenY);
            dimenZ = parse.NumberProvider("m_nZCount", dimenZ);
            spacingX = parse.NumberProvider("m_flXSpacing", spacingX);
            spacingY = parse.NumberProvider("m_flYSpacing", spacingY);
            spacingZ = parse.NumberProvider("m_flZSpacing", spacingZ);
            controlPointNumber = parse.Int32("m_nControlPointNumber", controlPointNumber);
            center = parse.Boolean("m_bCenter", center);
            hollow = parse.Boolean("m_bHollow", hollow);
        }

        private static bool HollowTest(int x, int dimenX, int hollowDimenX)
        {
            // skip first one
            if (x == 0 && dimenX != 1)
            {
                return false;
            }
            return x + 1 > hollowDimenX;
        }

        // We're simulating a lot of weird and incorrect behavior here, but it's accurate to source 2
        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var rawDimenX = this.dimenX.NextNumber(ref particle, particleSystemState);
            var rawDimenY = this.dimenY.NextNumber(ref particle, particleSystemState);
            var rawDimenZ = this.dimenZ.NextNumber(ref particle, particleSystemState);
            var spacingX = this.spacingX.NextNumber(ref particle, particleSystemState);
            var spacingY = this.spacingY.NextNumber(ref particle, particleSystemState);
            var spacingZ = this.spacingZ.NextNumber(ref particle, particleSystemState);

            // things wrap around differently when between two whole numbers. the hollow grid is larger
            rawDimenX = Math.Max(rawDimenX, 1.0f);
            rawDimenY = Math.Max(rawDimenY, 1.0f);
            rawDimenZ = Math.Max(rawDimenZ, 1.0f);

            var dimenX = (int)MathF.Ceiling(rawDimenX);
            var dimenY = (int)MathF.Ceiling(rawDimenY);
            var dimenZ = (int)MathF.Ceiling(rawDimenZ);

            var sizeX = dimenX * spacingX;
            var sizeY = dimenY * spacingY;
            var sizeZ = dimenZ * spacingZ;


            // Slower but infinitely better and more stable code

            // If hollow but the size never gets above 2 in any dimension.
            // Important note: hollow + 1x1x1 actually will cause a crash in Source 2
            var totalCount = dimenX * dimenY * dimenZ;

            var hollowDimenX = 0;
            var hollowDimenY = 0;
            var hollowDimenZ = 0;

            if (hollow)
            {
                /// dimenX = ceil(rawDimenX)
                /// ceil(rawDimX) - RawDimX = extra leftover value
                /// ceil(leftover) + dimenX - 2

                /// This is the most accurate way I've thought of currently to correctly emulate these errors
                /// even though we have to go out of our way to do it
                hollowDimenX = (dimenX == 1) ? 1 : (int)MathF.Ceiling(dimenX - rawDimenX) + dimenX - 2;
                hollowDimenY = (dimenY == 1) ? 1 : (int)MathF.Ceiling(dimenY - rawDimenY) + dimenY - 2;
                hollowDimenZ = (dimenZ == 1) ? 1 : (int)MathF.Ceiling(dimenZ - rawDimenZ) + dimenZ - 2;
            }
            var hollowSize = hollowDimenX * hollowDimenY * hollowDimenZ;
            totalCount -= hollowSize;


            var relativeCount = particle.ParticleID % totalCount;

            var position = new Vector3();
            var found = false;
            var i = 0;

            var hollowX = false;
            var hollowY = false;
            var hollowZ = false;

            // really slow but the cleanest way to do it
            for (var z = 0; z < dimenZ; z++)
            {
                hollowZ = HollowTest(z, dimenZ, hollowDimenZ);
                for (var y = 0; y < dimenY; y++)
                {
                    hollowY = HollowTest(y, dimenY, hollowDimenY);
                    for (var x = 0; x < dimenX; x++)
                    {
                        hollowX = HollowTest(x, dimenX, hollowDimenX);

                        if (hollowX && hollowY && hollowZ)
                        {
                            break;
                        }
                        if (i == relativeCount)
                        {
                            found = true;
                            position = new Vector3(x * spacingX, y * spacingY, z * spacingZ);
                            break;
                        }
                        i++;
                    }

                    if (found)
                    {
                        break;
                    }
                }

                if (found)
                {
                    break;
                }
            }

            if (!found)
            {
                Log.Warn(nameof(CreateOnGrid), "unable to find correct position on particle grid");
            }

            if (center)
            {
                var centerPoint = new Vector3(rawDimenX * spacingX, rawDimenY * spacingY, rawDimenZ * spacingZ) / 2.0f;

                position -= centerPoint;
            }

            if (controlPointNumber > -1)
            {
                position += particleSystemState.GetControlPoint(controlPointNumber).Position;
            }

            particle.Position = position;
            particle.PositionPrevious = position; // reset velocity
            particle.Velocity = Vector3.Zero; // because positionprevious isn't used for velocity yet

            return particle;
        }
    }
}
