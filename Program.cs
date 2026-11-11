using System.Globalization;

namespace GPR202_A3
{
    /// <summary>
    /// Entry point: GPR2 Balloon Hunt (Torrens University 2024), extended for GPR202 Assessment 3.
    /// </summary>
    internal class Program
    {
        static void Main(string[] args)
        {
            // Invariant culture so numbers always format with a '.' decimal point, whatever the computer's region
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            Console.WriteLine("GPR Balloon Hunt: Torrens University 2024");

            using var game = new Game(1280, 720, "GPR2 Balloon Hunt");
            game.Run();
        }
    }
}
