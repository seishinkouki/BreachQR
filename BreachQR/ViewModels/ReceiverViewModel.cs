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
    public partial class ReceiverViewModel : TransferViewModelBase
    {
        private const int ScanIntervalMilliseconds = 25;

        private readonly BarcodeReader barcodeReader = new();
        private readonly FountainSessionReceiver sessionReceiver = new();

        [ObservableProperty] private string fileName;
        [ObservableProperty] private long fileBytes;
        [ObservableProperty] private long totalChunks;
        [ObservableProperty] private long queryedChunkCount;
        [ObservableProperty] private long receivedPacketCount;
        [ObservableProperty] private string statusMessage = "将接收框覆盖到发送端二维码上";
        [ObservableProperty] private string outputPath;

        protected override async Task RunAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(1000, token);
                while (!token.IsCancellationRequested)
                {
                    IntPtr handle = new WindowInteropHelper(Application.Current.MainWindow).Handle;
                    if (handle != IntPtr.Zero && GetWindowRect(handle, out RECT rect))
                    {
                        ReceiverDisplayUpdate update = await Task.Run(() => CaptureDecodeProcessAndSave(rect), token);
                        if (update != null)
                        {
                            ApplyUpdate(update);
                        }
                    }

                    await Task.Delay(ScanIntervalMilliseconds, token);
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

        private ReceiverDisplayUpdate CaptureDecodeProcessAndSave(RECT rect)
        {
            using Bitmap snapshot = CaptureScreenSnapshot(rect.Left, rect.Top, rect.Right, rect.Bottom);
            Result result = barcodeReader.Decode(snapshot);
            if (!QrBinaryPayload.TryExtract(result, out byte[] frame))
            {
                return null;
            }

            FountainReceiveUpdate update = sessionReceiver.AddFrame(frame, DateTime.UtcNow);
            if (update == null)
            {
                return null;
            }

            string outputPath = null;
            if (update.CompletedData != null)
            {
                string receiveDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "BreachQR Received");
                Directory.CreateDirectory(receiveDirectory);
                outputPath = CreateUniquePath(receiveDirectory, update.Manifest.FileName);
                File.WriteAllBytes(outputPath, update.CompletedData);
            }

            return new ReceiverDisplayUpdate(update, outputPath);
        }

        private void ApplyUpdate(ReceiverDisplayUpdate displayUpdate)
        {
            FountainReceiveUpdate update = displayUpdate.Update;
            FileName = update.Manifest.FileName;
            FileBytes = update.Manifest.FileSize;
            TotalChunks = update.Manifest.BlockCount;
            QueryedChunkCount = update.RecoveredBlocks;
            ReceivedPacketCount = update.ReceivedPackets;

            if (displayUpdate.OutputPath == null)
            {
                StatusMessage = $"已恢复 {update.RecoveredBlocks}/{update.Manifest.BlockCount} 个源分片";
                return;
            }

            OutputPath = displayUpdate.OutputPath;
            StatusMessage = $"接收完成: {displayUpdate.OutputPath}";
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

        private sealed record ReceiverDisplayUpdate(FountainReceiveUpdate Update, string OutputPath);
    }
}
