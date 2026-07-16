using BreachQR.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using Net.Codecrete.QrCodeGenerator;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace BreachQR.ViewModels
{
    public partial class SenderViewModel : ObservableObject, ITransferViewModel
    {
        private const int DisplayIntervalMilliseconds = 50;

        private readonly string filePath;
        private readonly XmlSerializer serializer = new(typeof(svg));
        private CancellationTokenSource cancellation;
        private bool started;

        [ObservableProperty] private string fileName;
        [ObservableProperty] private long fileBytes;
        [ObservableProperty] private long totalChunks;
        [ObservableProperty] private long currentChunk;
        [ObservableProperty] private string svgStr;
        [ObservableProperty] private bool pauseFlag;
        [ObservableProperty] private string statusMessage = "正在初始化";

        public SenderViewModel(string filePath)
        {
            this.filePath = filePath;
        }

        public void Start()
        {
            if (started)
            {
                return;
            }

            started = true;
            cancellation = new CancellationTokenSource();
            _ = SendTask(cancellation.Token);
        }

        public void Stop()
        {
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
            started = false;
        }

        private async Task SendTask(CancellationToken token)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                FileBytes = fileInfo.Length;
                FileName = fileInfo.Name;
                StatusMessage = "正在准备喷泉编码";

                FountainEncoder encoder = await Task.Run(() => FountainEncoder.FromFile(filePath), token);
                TotalChunks = encoder.BlockCount;
                ulong sequence = 0;
                StatusMessage = "正在发送喷泉包";

                await Task.Delay(500, token);
                while (!token.IsCancellationRequested)
                {
                    if (PauseFlag)
                    {
                        await Task.Delay(50, token);
                        continue;
                    }

                    FountainPacket packet = encoder.CreatePacket(sequence);
                    string qrSvg = QrCode.EncodeText(packet.Serialize(), QrCode.Ecc.Low).ToSvgString(1);
                    using var textReader = new StringReader(qrSvg);
                    SvgStr = ((svg)serializer.Deserialize(textReader)).path.d;
                    CurrentChunk = checked((long)sequence + 1);
                    sequence++;
                    await Task.Delay(DisplayIntervalMilliseconds, token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                StatusMessage = $"发送失败: {ex.Message}";
                Trace.WriteLine(ex);
            }
        }
    }
}
