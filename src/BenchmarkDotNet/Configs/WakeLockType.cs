namespace BenchmarkDotNet.Configs
{
    public enum WakeLockType
    {
        /// <summary>
        /// Forces the system to be in the working state while benchmarks are running.
        /// </summary>
        RequireSystem,

        /// <summary>
        /// Allows the system to enter sleep and/or turn off the display while benchmarks are running.
        /// </summary>
        None,

        /// <summary>
        /// Forces the display to be on while benchmarks are running.
        /// </summary>
        RequireDisplay
    }
}