using Robust.Shared.GameStates;

namespace Content.Server.Flash.Components;
/// <summary>
///     Makes the entity immune to being flashed.
///     When given to clothes in the "head", "eyes" or "mask" slot it protects the wearer.
/// </summary>
[RegisterComponent] // Goob edit
public sealed partial class FlashImmunityComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("enabled")]
    public bool Enabled { get; set; } = true;

    [ViewVariables(VVAccess.ReadWrite)] // Goob edit
    [DataField("protectionRange")]
    public float ProtectionRange { get; set; } = 0f; // Goob edit
}
