using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BreachQR.ViewModels
{
    public interface ITransferViewModel
    {
        Task StartAsync();
        Task StopAsync();
    }

    public abstract class TransferViewModelBase : ObservableObject, ITransferViewModel
    {
        private readonly SemaphoreSlim lifecycleGate = new(1, 1);
        private CancellationTokenSource cancellation;
        private Task runTask;

        public async Task StartAsync()
        {
            await lifecycleGate.WaitAsync();
            try
            {
                if (runTask is { IsCompleted: false })
                {
                    return;
                }

                cancellation?.Dispose();
                cancellation = new CancellationTokenSource();
                runTask = RunAsync(cancellation.Token);
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        public async Task StopAsync()
        {
            await lifecycleGate.WaitAsync();
            try
            {
                if (runTask == null)
                {
                    return;
                }

                cancellation.Cancel();
                try
                {
                    await runTask;
                }
                catch (OperationCanceledException)
                {
                }

                runTask = null;
                cancellation.Dispose();
                cancellation = null;
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        protected abstract Task RunAsync(CancellationToken token);
    }
}
