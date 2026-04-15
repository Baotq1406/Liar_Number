using LiarNumberServer.Network;

namespace LiarNumberServer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Liar's Number Server ===");

            // Tao game server voi IP va port
            var server = new GameServer("192.168.1.25", 5555);
            
            // Bat dau lang nghe ket noi
            server.Start();

            Console.WriteLine("Server dang chay. Nhan Enter de dung...");
            Console.ReadLine();

            // Dung server va ngat tat ca ket noi
            server.Stop();
        }
    }
}
