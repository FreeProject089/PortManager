using System;
using System.Threading.Tasks;
using System.Windows;

namespace PortManager
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
            StartApp();
        }

        private async void StartApp()
        {
            // Simulate initialization
            TxtLoading.Text = "Loading UI Resources...";
            await Task.Delay(800);
            
            TxtLoading.Text = "Updating Network Status...";
            await Task.Delay(600);
            
            TxtLoading.Text = "Ensuring Administrator Privileges...";
            await Task.Delay(500);

            // Open Main Window
            var main = new MainWindow();
            main.Show();
            
            this.Close();
        }
    }
}
