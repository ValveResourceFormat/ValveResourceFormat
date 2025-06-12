namespace ValveResourceFormat;

public enum EntityIOTargetType
{
    ClassName = 0,
    ClassNameDerivesFrom = 1,
    EntityName = 2,
    ContainsComponent = 3,
    SpecialActivator = 4,
    SpecialCaller = 5,
    EHandle = 6,
    EntityNameOrClassName = 7,
}
