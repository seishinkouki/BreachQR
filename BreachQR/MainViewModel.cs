using HandyControl.Controls;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Net.Codecrete.QrCodeGenerator;
using System.Xml.Serialization;
using CommunityToolkit.Mvvm.Input;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace BreachQR
{
    public partial class MainViewModel : INotifyPropertyChanged
    {
        private double curX;
        public double CurX
        {
            set { curX = value; OnPropertyChanged(); }
            get { return curX; }
        }
        private double curY;
        public double CurY { set { curY = value; OnPropertyChanged(); } get { return curY; } }
        private int bootPage;
        public int BootPage { set { bootPage = value; OnPropertyChanged(); } get { return bootPage; } }
        //private static TaskFactory? uiFactory;
        private static int ChunkSize = 2000;
        XmlSerializer serializer = new XmlSerializer(typeof(svg));
        private CancellationTokenSource cts;

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        private string fileName;
        public string FileName { set { fileName = value; OnPropertyChanged(); } get { return fileName; } }
        private long fileBytes;
        public long FileBytes { set { fileBytes = value; OnPropertyChanged(); } get { return fileBytes; } }
        private long totalChunks;
        public long TotalChunks { set { totalChunks = value; OnPropertyChanged(); } get { return totalChunks; } }
        private long currentChunk;
        public long CurrentChunk { set { currentChunk = value; OnPropertyChanged(); } get { return currentChunk; } }
        private string svgStr;
        public string SvgStr { set { svgStr = value; OnPropertyChanged(); } get { return svgStr; } }

        private bool pulseFlag;
        public bool PulseFlag { set { pulseFlag = value; OnPropertyChanged(); } get { return pulseFlag; } }

        private bool shouldLoop;
        public bool ShouldLoop { set { shouldLoop = value; OnPropertyChanged(); } get { return shouldLoop; } }

        public MainViewModel()
        {
            PulseFlag = false;
            ShouldLoop = true;


            List<string> arguments = Environment.GetCommandLineArgs().ToList();
            //arguments.Add(Path.Combine(AppContext.BaseDirectory, "HandyControl.dll"));
            Console.WriteLine("GetCommandLineArgs: {0}", string.Join(", ", arguments));
            if (arguments.Count > 1)
            {
                BootPage = 0;
                FileInfo fileInfo = new FileInfo(arguments[1]);

                FileBytes = fileInfo.Length;
                FileName = fileInfo.Name;

                CurrentChunk = 0;
                TotalChunks = FileBytes % ChunkSize == 0 ? FileBytes / ChunkSize : FileBytes / ChunkSize + 1;
                SendTask(arguments[1]).ConfigureAwait(false);
            }
            else
            {
                BootPage = 1;
                FileInfo fileInfo = new FileInfo(arguments[0]);
                FileName = fileInfo.Name;
                ReceiveTask().ConfigureAwait(false);
            }

            //uiFactory = new TaskFactory(TaskScheduler.FromCurrentSynchronizationContext()); 
        }
        public async Task SendTask(string filePath)
        {
            cts = new CancellationTokenSource();
            var token = cts.Token;
            await Task.Run(() =>
            {
                Thread.Sleep(500);
                while (!token.IsCancellationRequested)
                {
                    if (!ShouldLoop)
                    {
                        continue;
                    }
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    byte[] buffer = new byte[ChunkSize];
                    int bytesRead;
                    int i = 1;

                    while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {

                        // 处理读取到的数据
                        var qr_svg = QrCode.EncodeText(string.Concat(i, "|", TotalChunks, "|", Convert.ToBase64String(buffer)), QrCode.Ecc.Low).ToSvgString(1);
                        CurrentChunk = i;
                        using (TextReader reader = new StringReader(qr_svg))
                        {
                            svg result = (svg)serializer.Deserialize(reader);
                            SvgStr = result.path.d;
                            i++;
                        }
                        // 检查是否需要取消任务
                        if (token.IsCancellationRequested)
                        {
                            break;
                        }

                        // 检查是否需要暂停任务
                        while (PulseFlag)
                        {
                            Thread.Sleep(1);
                        }
                    }
                }
            }, token);
        }
        public async Task ReceiveTask()
        {
            cts = new CancellationTokenSource();
            var token = cts.Token;
            await Task.Run(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    if (CurX == 0 | CurY == 0)
                    {
                        continue;
                    }
                    Trace.WriteLine($"{CurX},{CurY}");
                    Thread.Sleep(200);
                }
            }, token);
        }
    }
}
