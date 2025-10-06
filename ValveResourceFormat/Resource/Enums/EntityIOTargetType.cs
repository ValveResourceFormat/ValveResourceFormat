namespace ValveResourceFormat;

public enum EntityIOTargetType
{
#pragma warning disable CS1591
    ClassName = 0,
    ClassNameDerivesFrom = 1,
    EntityName = 2,
    ContainsComponent = 3,
    SpecialActivator = 4,
    SpecialCaller = 5,
    EHandle = 6,
    EntityNameOrClassName = 7,
#pragma warning restore CS1591
}
