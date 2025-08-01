namespace Content.Server.Stunnable.Components
{
    /// <summary>
    /// Adds stun when it collides with an entity
    /// </summary>
    [RegisterComponent, Access(typeof(StunOnCollideSystem))]
    public sealed partial class StunOnCollideComponent : Component
    {
        // TODO: Can probably predict this.

        /// <summary>
        /// How long we are stunned for
        /// </summary>
        [DataField]
        public TimeSpan StunAmount; // Forge-Change

        /// <summary>
        /// How long we are knocked down for
        /// </summary>
        [DataField]
        public TimeSpan KnockdownAmount; // Forge-Change

        /// <summary>
        /// How long we are slowed down for
        /// </summary>
        [DataField]
        public TimeSpan SlowdownAmount; // Forge-Change

        /// <summary>
        /// Multiplier for a mob's walking speed
        /// </summary>
        [DataField]
        public float WalkSpeedModifier = 1f; // Forge-Change

        /// <summary>
        /// Multiplier for a mob's sprinting speed
        /// </summary>
        [DataField]
        public float SprintSpeedModifier = 1f; // Forge-Change

        /// <summary>
        /// Refresh Stun or Slowdown on hit
        /// </summary>
        [DataField]
        public bool Refresh = true; // Forge-Change

        /// <summary>
        /// Should the entity try and stand automatically after being knocked down?
        /// </summary>
        [DataField]
        public bool AutoStand = true; // Forge-Change

        /// <summary>
        /// Fixture we track for the collision.
        /// </summary>
        [DataField("fixture")] public string FixtureID = "projectile";
    }
}
