namespace GPR202_A3.Particles
{
    /// <summary>
    /// What a particle does when it reaches the ground (or the water).
    /// </summary>
    internal enum GroundContact
    {
        None, // passes through (smoke rises, so it never gets there)
        Kill, // disappears (fire sparks)
        Settle // stops and lies flat, then fades out where it landed (confetti)
    }
}
