using BreachQR.ViewModels;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BreachQR
{
    public static class ViewModelLifecycleBehavior
    {
        public static readonly DependencyProperty ViewModelProperty = DependencyProperty.RegisterAttached(
            "ViewModel",
            typeof(ITransferViewModel),
            typeof(ViewModelLifecycleBehavior),
            new PropertyMetadata(null, OnViewModelChanged));

        private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
            "State",
            typeof(LifecycleState),
            typeof(ViewModelLifecycleBehavior));

        public static void SetViewModel(DependencyObject element, ITransferViewModel value)
        {
            element.SetValue(ViewModelProperty, value);
        }

        public static ITransferViewModel GetViewModel(DependencyObject element)
        {
            return (ITransferViewModel)element.GetValue(ViewModelProperty);
        }

        private static void OnViewModelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
        {
            if (dependencyObject is not FrameworkElement element)
            {
                return;
            }

            var state = (LifecycleState)element.GetValue(StateProperty);
            if (state == null)
            {
                state = new LifecycleState(element);
                element.SetValue(StateProperty, state);
            }

            state.ChangeViewModel((ITransferViewModel)args.NewValue);
        }

        private sealed class LifecycleState
        {
            private readonly FrameworkElement element;
            private readonly SemaphoreSlim transitionGate = new(1, 1);
            private ITransferViewModel viewModel;
            private bool isLoaded;
            private bool isStarted;

            public LifecycleState(FrameworkElement element)
            {
                this.element = element;
                element.Loaded += OnLoaded;
                element.Unloaded += OnUnloaded;

                if (element is Window window)
                {
                    window.Closed += OnClosed;
                }
            }

            public void ChangeViewModel(ITransferViewModel value)
            {
                RunTransition(() => ChangeViewModelAsync(value));
            }

            private void OnLoaded(object sender, RoutedEventArgs args)
            {
                RunTransition(() => SetLoadedAsync(true));
            }

            private void OnUnloaded(object sender, RoutedEventArgs args)
            {
                RunTransition(() => SetLoadedAsync(false));
            }

            private void OnClosed(object sender, EventArgs args)
            {
                RunTransition(() => SetLoadedAsync(false));
            }

            private async Task ChangeViewModelAsync(ITransferViewModel value)
            {
                await transitionGate.WaitAsync();
                try
                {
                    await StopCurrentAsync();
                    viewModel = value;
                    await StartCurrentAsync();
                }
                finally
                {
                    transitionGate.Release();
                }
            }

            private async Task SetLoadedAsync(bool value)
            {
                await transitionGate.WaitAsync();
                try
                {
                    isLoaded = value;
                    if (isLoaded)
                    {
                        await StartCurrentAsync();
                    }
                    else
                    {
                        await StopCurrentAsync();
                    }
                }
                finally
                {
                    transitionGate.Release();
                }
            }

            private async Task StartCurrentAsync()
            {
                if (!isLoaded || isStarted || viewModel == null)
                {
                    return;
                }

                await viewModel.StartAsync();
                isStarted = true;
            }

            private async Task StopCurrentAsync()
            {
                if (!isStarted || viewModel == null)
                {
                    return;
                }

                await viewModel.StopAsync();
                isStarted = false;
            }

            private static async void RunTransition(Func<Task> transition)
            {
                try
                {
                    await transition();
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex);
                }
            }
        }
    }
}
