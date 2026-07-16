using BreachQR.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using ZXing;
using ZXing.Windows.Compatibility;

namespace BreachQR.ViewModels
{
    [SupportedOSPlatform("windows")]
    public partial class ReceiverViewModel : ObservableObject, ITransferViewModel
    {
        private readonly BarcodeReader barcodeReader = new();
        private CancellationTokenSource cancellation;
        private FountainDecoder decoder;
        private string savedSessionId;
        private bool started;

        [ObservableProperty] private string fileName;
        [ObservableProperty] private long fileBytes;
        [ObservableProperty] private long totalChunks;
        [ObservableProperty] private long queryedChunkCount;
        [ObservableProperty] private long receivedPacketCount;
        [ObservableProperty] private string statusMessage = "将接收框覆盖到发送端二维码上";
        [ObservableProperty] private string outputPath;

        public void Start()
        {
            if (started)
            {
                return;
            }

            started = true;
            cancellation = new CancellationTokenSource();
            _ = ReceiveTask(cancellation.Token);
        }

        public void Stop()
        {
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
            started = false;
        }

        private async Task ReceiveTask(CancellationToken token)
        {
            try
            {
                await Task.Delay(1000, token);
                while (!token.IsCancellationRequested)
                {
                    IntPtr handle = new WindowInteropHelper(Application.Current.MainWindow).Handle;
                    if (handle != IntPtr.Zero && GetWindowRect(handle, out RECT rect))
                    {
                        Result result = await Task.Run(() =>
                        {
                            using Bitmap snapshot = CaptureScreenSnapshot(rect.Left, rect.Top, rect.Right, rect.Bottom);
                            return barcodeReader.Decode(snapshot);
                        }, token);

                        if (result != null && FountainPacket.TryParse(result.Text, out FountainPacket packet))
                        {
                            ProcessPacket(packet);
                        }
                    }

                    await Task.Delay(10, token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                StatusMessage = $"接收失败: {ex.Message}";
                Trace.WriteLine(ex);
            }
        }

        private void ProcessPacket(FountainPacket packet)
        {
            if (savedSessionId == packet.SessionId)
            {
                return;
            }

            if (decoder == null || decoder.SessionId != packet.SessionId)
            {
                decoder = new FountainDecoder();
                savedSessionId = null;
                FileName = packet.FileName;
                FileBytes = packet.FileSize;
                TotalChunks = packet.BlockCount;
                QueryedChunkCount = 0;
                ReceivedPacketCount = 0;
                OutputPath = null;
            }

            if (!decoder.AddPacket(packet))
            {
                return;
            }

            ReceivedPacketCount = decoder.ReceivedPacketCount;
            QueryedChunkCount = decoder.SolvedBlockCount;
            StatusMessage = $"已恢复 {QueryedChunkCount}/{TotalChunks} 个源分片";
            if (decoder.IsComplete && savedSessionId != decoder.SessionId)
            {
                SaveDecodedFile();
            }
        }

        private void SaveDecodedFile()
        {
            byte[] data = decoder.GetDecodedData();
            string receiveDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "BreachQR Received");
            Directory.CreateDirectory(receiveDirectory);

            string path = CreateUniquePath(receiveDirectory, decoder.FileName);
            File.WriteAllBytes(path, data);
            savedSessionId = decoder.SessionId;
            OutputPath = path;
            StatusMessage = $"接收完成: {path}";
        }

        private static string CreateUniquePath(string directory, string fileName)
        {
            string path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
            {
                return path;
            }

            string name = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            for (int suffix = 1; ; suffix++)
            {
                path = Path.Combine(directory, $"{name} ({suffix}){extension}");
                if (!File.Exists(path))
                {
                    return path;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private static Bitmap CaptureScreenSnapshot(int x1, int y1, int x2, int y2)
        {
            var snapshot = new Bitmap(x2 - x1, y2 - y1);
            using Graphics graphics = Graphics.FromImage(snapshot);
            graphics.CopyFromScreen(x1, y1, 0, 0, snapshot.Size, CopyPixelOperation.SourceCopy);
            return snapshot;
        }
    }
}
