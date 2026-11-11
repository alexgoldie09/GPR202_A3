using System.Globalization;

namespace GPR202_A3
{
    /// <summary>
    /// Entry point: GPR2 Balloon Hunt (Torrens University 2024), extended for GPR202 Assessment 3.
    /// </summary>
    internal class Program
    {
        // Starting window size and title
        private const int WINDOWWIDTH = 1280;
        private const int WINDOWHEIGHT = 720;
        private const string TITLE = "GPR2 Balloon Hunt";

        static void Main(string[] args)
        {
            // Numbers always use '.' for decimals, whatever the computer's region
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            Console.WriteLine("GPR Balloon Hunt: Torrens University 2024");

            using var game = new Game(WINDOWWIDTH, WINDOWHEIGHT, TITLE);
            game.Run();
        }
    }
}
