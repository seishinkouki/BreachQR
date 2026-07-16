using System.Windows;
using BreachQR.ViewModels;

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

            state.SetViewModel((ITransferViewModel)args.NewValue);
        }

        private sealed class LifecycleState
        {
            private readonly FrameworkElement element;
            private ITransferViewModel viewModel;
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

            public void SetViewModel(ITransferViewModel value)
            {
                Stop();
                viewModel = value;
                if (element.IsLoaded)
                {
                    Start();
                }
            }

            private void OnLoaded(object sender, RoutedEventArgs args)
            {
                Start();
            }

            private void OnUnloaded(object sender, RoutedEventArgs args)
            {
                Stop();
            }

            private void OnClosed(object sender, System.EventArgs args)
            {
                Stop();
            }

            private void Start()
            {
                if (isStarted || viewModel == null)
                {
                    return;
                }

                viewModel.Start();
                isStarted = true;
            }

            private void Stop()
            {
                if (!isStarted)
                {
                    return;
                }

                viewModel.Stop();
                isStarted = false;
            }
        }
    }
}
