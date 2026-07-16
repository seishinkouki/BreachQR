using System;
using System.Runtime.Versioning;

namespace BreachQR.ViewModels
{
    [SupportedOSPlatform("windows")]
    public sealed class MainViewModel
    {
        public MainViewModel()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            CurrentViewModel = arguments.Length > 1
                ? new SenderViewModel(arguments[1])
                : new ReceiverViewModel();
        }

        public ITransferViewModel CurrentViewModel { get; }
    }

    public interface ITransferViewModel
    {
        void Start();
        void Stop();
    }
}
