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
using CommunityToolkit.Mvvm;
using CommunityToolkit.Mvvm.Input;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using System.Runtime.InteropServices;
using Size = System.Drawing.Size;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.ComponentModel;
using ZXing;
using System.Reflection.PortableExecutable;
using ZXing.Windows.Compatibility;

namespace BreachQR
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private int bootPage;
        private static int ChunkSize = 2000;
        XmlSerializer serializer = new XmlSerializer(typeof(svg));
        private CancellationTokenSource cts;

        [ObservableProperty] private string fileName;
        [ObservableProperty] private long fileBytes;
        [ObservableProperty] private long totalChunks;
        [ObservableProperty] private long currentChunk;
        [ObservableProperty] private string svgStr;
        [ObservableProperty] private bool pauseFlag;
        [ObservableProperty] private bool shouldLoop;

        private List<long> QueryedChunks;

        [ObservableProperty] private long queryedChunkCount;
        private static BarcodeReader reader;
        public MainViewModel()
        {
            PauseFlag = false;
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
                TotalChunks = 0;
                QueryedChunkCount = 0;
                QueryedChunks = new List<long>();
                reader = new BarcodeReader();
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
                    Thread.Sleep(300);
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
                        while (PauseFlag)
                        {
                            Thread.Sleep(1);
                        }
                        Thread.Sleep(300);
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
                Thread.Sleep(5000);
                while (!token.IsCancellationRequested)
                {
                    Thread.Sleep(100);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            GetWindowRect(new WindowInteropHelper(Application.Current.MainWindow).Handle, out RECT pRect);
                            var str = reader.Decode(CaptureScreenSnapshot(pRect.Left, pRect.Top, pRect.Right, pRect.Bottom));
                            if (str != null)
                            {
                                if(TotalChunks == 0)
                                {
                                    TotalChunks = Convert.ToInt32(str.Text.Split('|')[1]);
                                }
                                var cur = Convert.ToInt32(str.Text.Split('|')[0]);
                                if (!QueryedChunks.Contains(cur))
                                {
                                    QueryedChunks.Add(cur);
                                    QueryedChunkCount = QueryedChunks.Count;
                                }
                                Trace.WriteLine($"{TotalChunks}, {QueryedChunkCount}");
                                Trace.WriteLine(str.Text);
                            }
                        }
                        catch (Exception ex)
                        {

                        }
                        
                    });
                    
                    
                }
            }, token);
        }
        [RelayCommand]
        public void PrintXY()
        {
            GetWindowRect(new WindowInteropHelper(Application.Current.MainWindow).Handle, out RECT pRect);
            var abc = CaptureScreenSnapshot(pRect.Left, pRect.Top, pRect.Right, pRect.Bottom);
            //return abc;
            abc.Save(Path.Combine(AppContext.BaseDirectory, "123.jpg"), ImageFormat.Jpeg);
            Trace.WriteLine(pRect.Left + "," + pRect.Top + "," + pRect.Right + "," + pRect.Bottom);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hDc, int nIndex);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        /// <summary>
        /// 获取物理屏幕分辨率
        /// </summary>
        /// <returns></returns>
        public static System.Drawing.Size GetScreenByDevice()
        {
            IntPtr hDc = GetDC(IntPtr.Zero);
            var DESKTOPVERTRES = 117;
            var DESKTOPHORZRES = 118;
            return new System.Drawing.Size()
            {
                Width = GetDeviceCaps(hDc, DESKTOPHORZRES),
                Height = GetDeviceCaps(hDc, DESKTOPVERTRES)
            };
        }

        /// <summary>截取指定区域</summary>
        /// <param name="x1"></param>
        /// <param name="y1"></param>
        /// <param name="x2"></param>
        /// <param name="y2"></param>
        /// <returns>返回bitmap</returns>
        public static Bitmap CaptureScreenSnapshot(int x1, int y1, int x2, int y2)
        {
            System.Drawing.Size sysize = GetScreenByDevice();
            Bitmap background = new(x2 - x1, y2 - y1);
            using (Graphics gcs = Graphics.FromImage(background))
            {
                gcs.CopyFromScreen(x1, y1, 0, 0, sysize, CopyPixelOperation.SourceCopy);
            }
            return background;
        }
    }
}
