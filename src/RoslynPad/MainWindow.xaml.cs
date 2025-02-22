﻿using System;
using System.ComponentModel;
using System.Composition.Hosting;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;
using Avalon.Windows.Controls;
using AvalonDock;
using AvalonDock.Layout.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoslynPad.UI;

namespace RoslynPad
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private readonly MainViewModelBase _viewModel;
        private bool _isClosing;
        private bool _isClosed;

        public MainWindow()
        {
            Loaded += OnLoaded;

            var services = new ServiceCollection();
            services.AddLogging(l => l.AddSimpleConsole().AddDebug());

            var container = new ContainerConfiguration()
                .WithProvider(new ServiceCollectionExportDescriptorProvider(services))
                .WithAssembly(typeof(MainViewModelBase).Assembly)   // RoslynPad.Common.UI
                .WithAssembly(typeof(MainWindow).Assembly);         // RoslynPad
            var locator = container.CreateContainer().GetExport<IServiceProvider>();

            _viewModel = locator.GetRequiredService<MainViewModelBase>();

            DataContext = _viewModel;
            InitializeComponent();
            DocumentsPane.ToggleAutoHide();

            LoadWindowLayout();
            LoadDockLayout();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;

            await _viewModel.Initialize().ConfigureAwait(false);
        }

        protected override async void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            if (!_isClosing)
            {
                SaveDockLayout();
                SaveWindowLayout();
                
                _isClosing = true;
                IsEnabled = false;
                e.Cancel = true;

                try
                {
                    await Task.Run(() => _viewModel.OnExit()).ConfigureAwait(true);
                }
                catch
                {
                    // ignored
                }

                _isClosed = true;
                var closeTask = Dispatcher.InvokeAsync(Close);
            }
            else
            {
                e.Cancel = !_isClosed;
            }
        }

        private void LoadWindowLayout()
        {
            var boundsString = _viewModel.Settings.WindowBounds;
            if (!string.IsNullOrEmpty(boundsString))
            {
                try
                {
                    var bounds = Rect.Parse(boundsString);
                    if (bounds != default)
                    {
                        Left = bounds.Left;
                        Top = bounds.Top;
                        Width = bounds.Width;
                        Height = bounds.Height;
                    }
                }
                catch (FormatException)
                {
                }
            }

            if (Enum.TryParse(_viewModel.Settings.WindowState, out WindowState state) &&
                state != WindowState.Minimized)
            {
                WindowState = state;
            }

            if (_viewModel.Settings.WindowFontSize.HasValue)
            {
                FontSize = _viewModel.Settings.WindowFontSize.Value;
            }
        }

        private void SaveWindowLayout()
        {
            _viewModel.Settings.WindowBounds = RestoreBounds.ToString(CultureInfo.InvariantCulture);
            _viewModel.Settings.WindowState = WindowState.ToString();
        }

        private void LoadDockLayout()
        {
            var layout = _viewModel.Settings.DockLayout;
            if (string.IsNullOrEmpty(layout)) return;

            var serializer = new XmlLayoutSerializer(DockingManager);
            var reader = new StringReader(layout);
            try
            {
                serializer.Deserialize(reader);
            }
            catch
            {
                // ignored
            }
        }

        private void SaveDockLayout()
        {
            var serializer = new XmlLayoutSerializer(DockingManager);
            var document = new XDocument();
            using (var writer = document.CreateWriter())
            {
                serializer.Serialize(writer);
            }
            document.Root?.Element("FloatingWindows")?.Remove();
            _viewModel.Settings.DockLayout = document.ToString();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            Application.Current.Shutdown();
        }
        
        private async void DockingManager_OnDocumentClosing(object sender, DocumentClosingEventArgs e)
        {
            e.Cancel = true;
            var document = (OpenDocumentViewModel)e.Document.Content;
            await _viewModel.CloseDocument(document).ConfigureAwait(false);
        }

        private void ViewErrorDetails_OnClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel.LastError == null) return;

            TaskDialog.ShowInline(this, "Unhandled Exception",
                _viewModel.LastError.ToString(), string.Empty, TaskDialogButtons.Close);
        }

        private void ViewUpdateClick(object sender, RoutedEventArgs e)
        {
            _ = Task.Run(() => Process.Start(new ProcessStartInfo("https://roslynpad.net/") { UseShellExecute = true }));
        }
    }
}
