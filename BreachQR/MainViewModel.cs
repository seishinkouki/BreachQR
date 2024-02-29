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

namespace BreachQR
{
    public partial class MainViewModel : INotifyPropertyChanged
    {
        //private static TaskFactory? uiFactory;
        private static int ChunkSize = 2000;
        XmlSerializer serializer = new XmlSerializer(typeof(svg));
        private CancellationTokenSource cts;
        private bool PulseFlag = false;
        private bool ShouldLoop = true;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void NotifyPropertyChanged(String info)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(info));
        }
        private string fileName;
        public string FileName
        {
            set 
            {
                fileName = value;
                NotifyPropertyChanged("FileName");
            }
            get { return fileName; }
        }
        private long fileBytes;
        public long FileBytes
        {
            set { fileBytes = value; }
            get { return fileBytes; }
        }
        private long totalChunks;
        public long TotalChunks
        {
            set
            {
                totalChunks = value;
                NotifyPropertyChanged("totalChunks");
            }
            get { return totalChunks; }
        }
        private long currentChunk;
        public long CurrentChunk
        {
            set
            {
                currentChunk = value;
                NotifyPropertyChanged("currentChunk");
            }
            get { return currentChunk; }
        }
        private string svgStr;
        public string SvgStr
        {
            set
            {
                svgStr = value;
                NotifyPropertyChanged("svgStr");
            }
            get { return svgStr; }

        }

        public MainViewModel() 
        {
            List<string> arguments = Environment.GetCommandLineArgs().ToList();
            arguments.Add(Path.Combine(AppContext.BaseDirectory, "HandyControl.dll"));
            Console.WriteLine("GetCommandLineArgs: {0}", string.Join(", ", arguments));
            if (arguments.Count > 1 )
            {
                FileInfo fileInfo = new FileInfo(arguments[1]);

                FileBytes = fileInfo.Length;
                FileName = fileInfo.Name;

                TotalChunks = FileBytes % ChunkSize == 0 ? FileBytes / ChunkSize : FileBytes / ChunkSize + 1;
            }
            else
            {
                FileInfo fileInfo = new FileInfo(arguments[0]);
                FileName = fileInfo.Name;
            }

            //uiFactory = new TaskFactory(TaskScheduler.FromCurrentSynchronizationContext());

            StartTask(arguments[1]).ConfigureAwait(false);
        }
        [RelayCommand]
        public void PulseOrResume()
        {
            PulseFlag = !PulseFlag;
        }
        [RelayCommand]
        public void ToggleLooping()
        {
            ShouldLoop = !ShouldLoop;
        }
        public async Task StartTask(string filePath)
        {
            cts = new CancellationTokenSource();
            var token = cts.Token;
            await Task.Run(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    if(!ShouldLoop)
                    {
                        continue;
                    }
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    byte[] buffer = new byte[ChunkSize];
                    int bytesRead;
                    int i = 0;

                    while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {

                        // 处理读取到的数据
                        var qr_svg = QrCode.EncodeText(Convert.ToBase64String(buffer), QrCode.Ecc.Low).ToSvgString(1);
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
    }
}
