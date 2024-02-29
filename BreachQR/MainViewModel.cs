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
using Net.Codecrete.QrCodeGenerator;
using System.Drawing.Imaging;
using System.Xml.Serialization;

namespace BreachQR
{
    public partial class MainViewModel : INotifyPropertyChanged
    {
        private static TaskFactory? uiFactory;
        private static int ChunkSize = 2000;

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

                TotalChunks = FileBytes % ChunkSize == 0 ? FileBytes / ChunkSize : FileBytes / ChunkSize + 1;
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
                    //int bytesRead;
                    var buffer = new byte[ChunkSize];
                    var base64string = "";

                    //QRCodeGenerator qrGenerator = new QRCodeGenerator();
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
                            bytesRead = stream.Read(buffer, 0, ChunkSize);
                            base64string = Convert.ToBase64String(buffer);
                            //QRCodeData qrCodeData = qrGenerator.CreateQrCode(base64string, QRCodeGenerator.ECCLevel.Q);
                            //QRCode qrCode = new QRCode(qrCodeData);
                            //qrCodeData.Dispose();
                            //Bitmap qrCodeImage = qrCode.GetGraphic(20);

                            var qr = QrCode.EncodeText(base64string, QrCode.Ecc.Low);
                            


                            //uiFactory!.StartNew(() =>
                            //{
                                CurrentChunk = i;
                            var abc = qr.ToSvgString(1);

                            XmlSerializer serializer = new XmlSerializer(typeof(svg));
                            using (TextReader reader = new StringReader(abc))
                            {
                                svg result = (svg)serializer.Deserialize(reader);
                                SvgStr = result.path.d;
                            }

                            //ImageSource = QrCodeDrawing.CreateDrawing(qr, 200, 0);
                            //ImageSource = ImageSourceFromBitmap(qrCodeImage);
                            //qrCodeImage.Dispose();
                            //});
                            //qrCodeData.Dispose();
                            //qrCode.Dispose();
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
