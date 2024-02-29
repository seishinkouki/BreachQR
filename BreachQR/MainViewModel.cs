using HandyControl.Controls;
using QRCoder;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BreachQR
{
    public partial class MainViewModel : INotifyPropertyChanged
    {
        private static TaskFactory? uiFactory;

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
        private ImageSource imageSource;
        public ImageSource ImageSource
        {
            set
            {
                imageSource = value;
                NotifyPropertyChanged("imageSource");
            }
            get { return imageSource; }
        }
        private static Task task;
        public MainViewModel() 
        {
            var tokenSource = new CancellationTokenSource();
            CancellationToken ct = tokenSource.Token;


            List<string> arguments = Environment.GetCommandLineArgs().ToList();
            arguments.Add(Path.Combine(AppContext.BaseDirectory, "HandyControl.dll"));
            Console.WriteLine("GetCommandLineArgs: {0}", string.Join(", ", arguments));
            if (arguments.Count > 1 )
            {
                FileInfo fileInfo = new FileInfo(arguments[1]);

                FileBytes = fileInfo.Length;
                FileName = fileInfo.Name;

                TotalChunks = FileBytes % 1024 == 0 ? FileBytes / 1024 : FileBytes / 1024 + 1;
            }
            else
            {
                //tokenSource.Cancel();
                FileInfo fileInfo = new FileInfo(arguments[0]);
                FileName = fileInfo.Name;
            }

            uiFactory = new TaskFactory(TaskScheduler.FromCurrentSynchronizationContext());

            task = Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                while (true)
                {
                    if (ct.IsCancellationRequested)
                    {
                        ct.ThrowIfCancellationRequested();
                    }
                    int i = 0;
                    CurrentChunk = i;
                    const int chunkSize = 1024;
                    //int bytesRead;
                    var buffer = new byte[chunkSize];
                    var base64string = "";

                    QRCodeGenerator qrGenerator = new QRCodeGenerator();
                    //using (var file = File.OpenRead(arguments[1]))
                    //{
                    //    while ((bytesRead = file.Read(buffer, 0, buffer.Length)) > 0)
                    //    { 
                    //        base64string = Convert.ToBase64String(buffer);
                    //        QRCodeData qrCodeData = qrGenerator.CreateQrCode(base64string, QRCodeGenerator.ECCLevel.Q);
                    //        QRCode qrCode = new QRCode(qrCodeData);
                    //        Bitmap qrCodeImage = qrCode.GetGraphic(20);

                    //        uiFactory!.StartNew(() =>
                    //        {
                    //            CurrentChunk = i;
                    //            ImageSource = ImageSourceFromBitmap(qrCodeImage);
                    //        });
                    //        i++;
                    //        //Thread.Sleep(100);
                    //    }
                    //}

                    var bytesRead = 0;

                    using (var stream = File.OpenRead(arguments[1]))
                    {
                        do
                        {
                            //buffer = new byte[bufferSize];
                            bytesRead = stream.Read(buffer, 0, chunkSize);
                            base64string = Convert.ToBase64String(buffer);
                            QRCodeData qrCodeData = qrGenerator.CreateQrCode(base64string, QRCodeGenerator.ECCLevel.Q);
                            QRCode qrCode = new QRCode(qrCodeData);
                            //qrCodeData.Dispose();
                            Bitmap qrCodeImage = qrCode.GetGraphic(20);
                            
                            uiFactory!.StartNew(() =>
                            {
                                CurrentChunk = i;
                                ImageSource = ImageSourceFromBitmap(qrCodeImage);
                                qrCodeImage.Dispose();
                            });
                            qrCodeData.Dispose();
                            qrCode.Dispose();
                            i++;
                            // Process buffer

                        } while (bytesRead > 0);
                    }
                }
            }, tokenSource.Token);

            //try
            //{
            //    Task.WaitAll(task);
            //    //await task;
            //}
            //catch (OperationCanceledException e)
            //{
            //    Console.WriteLine($"{nameof(OperationCanceledException)} thrown with message: {e.Message}");
            //}
            //finally
            //{
            //    tokenSource.Dispose();
            //}
        }
        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject([In] IntPtr hObject);

        public ImageSource ImageSourceFromBitmap(Bitmap bmp)
        {
            var handle = bmp.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            finally { DeleteObject(handle); }
        }
    }
}
