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
    public partial class SenderViewModel : TransferViewModelBase
    {
        private const int DisplayIntervalMilliseconds = 50;
        private const int ManifestInterval = 20;

        private readonly string filePath;

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

        protected override async Task RunAsync(CancellationToken token)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                FileBytes = fileInfo.Length;
                FileName = fileInfo.Name;
                StatusMessage = "正在准备喷泉编码";

                FountainEncoder encoder = await Task.Run(() => FountainEncoder.FromFile(filePath), token);
                byte[] manifestFrame = FountainWireProtocol.CreateManifestFrame(encoder);
                TotalChunks = encoder.BlockCount;
                ulong sequence = 0;
                long displayedFrame = 0;
                StatusMessage = "正在发送喷泉包";

                await Task.Delay(500, token);
                while (!token.IsCancellationRequested)
                {
                    if (PauseFlag)
                    {
                        await Task.Delay(50, token);
                        continue;
                    }

                    bool isManifest = displayedFrame % ManifestInterval == 0;
                    ulong packetSequence = sequence;
                    string geometry = await Task.Run(() =>
                    {
                        byte[] wireFrame = manifestFrame;
                        if (!isManifest)
                        {
                            FountainPacket packet = encoder.CreatePacket(packetSequence);
                            wireFrame = FountainWireProtocol.CreateDataFrame(packet);
                        }

                        return CreateQrGeometry(wireFrame);
                    }, token);
                    if (!isManifest)
                    {
                        sequence++;
                    }

                    SvgStr = geometry;
                    CurrentChunk = checked((long)sequence);
                    displayedFrame++;
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

        private static string CreateQrGeometry(byte[] frame)
        {
            string qrSvg = QrCode.EncodeBinary(frame, QrCode.Ecc.Low).ToSvgString(1);
            using var textReader = new StringReader(qrSvg);
            var serializer = new XmlSerializer(typeof(svg));
            return ((svg)serializer.Deserialize(textReader)).path.d;
        }
    }
}
