using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace SpectraGrab.HLS
{
    public static class HlsAssembler
    {
        public static async Task<string> AssembleAsync(string folder)
        {
            string output = Path.Combine(folder, "SpectraGrab_Output.mp4");

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-i \"concat:$(ls *.ts | tr ' ' '|')\" -c copy SpectraGrab_Output.mp4",
                WorkingDirectory = folder,
                CreateNoWindow = true,
                UseShellExecute = false
            };

            var proc = Process.Start(psi);
            await proc.WaitForExitAsync();

            return output;
        }
    }
}
