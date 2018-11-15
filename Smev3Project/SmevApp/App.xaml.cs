using System;
using System.Windows;

namespace SmevApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App() : base()
        {
            Dispatcher.UnhandledException += OnDispatcherUnhandledException;

        }

        private void OnAppStartup_UpdateThemeName(object sender, StartupEventArgs e)
        {
            DevExpress.Xpf.Core.ApplicationThemeHelper.UpdateApplicationThemeName();

        }

        private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            switch (e.Exception.InnerException)
            {
                case OperationCanceledException _:
                    MessageBox.Show("Операция отменена пользователем", @"Предупреждение",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    e.Handled = true;
                    return;
                default:
                    MessageBox.Show(
                        $"Произошла ошибка, подробно:\r\n{e.Exception.InnerException?.Message ?? e.Exception.Message}",
                        @"Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Handled = true;
                    return;
            }
        }
    }
}
