using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MusikArchivApp.Data;

namespace MusikArchivApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            base.OnStartup(e);

            DatabaseInitializer.Initialize();
            var connectionString = DatabaseInitializer.GetConnectionString();
            var repository = new PieceRepository(connectionString);

            var mainWindow = new MainWindow(repository);
            mainWindow.Show();
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            UiMessage.Show($"Unerwarteter Fehler (UI-Thread): {e.Exception}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            UiMessage.Show($"Unerwarteter Fehler (AppDomain): {ex}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            UiMessage.Show($"Unerwarteter Fehler (Task): {e.Exception}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            e.SetObserved();
        }
    }
}
