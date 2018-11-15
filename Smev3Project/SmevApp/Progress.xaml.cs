using System.Threading;
using System.Windows;
using System.Windows.Forms;

namespace SmevApp
{
    public partial class Progress : Window
    {
        private int _rowCounter = 0;
        private readonly int _totalCount = 0;

        public CancellationTokenSource CancellationTokenSource { get; }

        public Progress()
        {
            InitializeComponent();
        }

        public Progress(string caption, CancellationTokenSource cancellationTokenSource, int totalCount)
        {
            InitializeComponent();

            LblText.Content = string.IsNullOrEmpty(caption) ? "Пожалуйста, подождите..." : caption;

            _totalCount = totalCount;

            CancellationTokenSource = cancellationTokenSource;
        }

        public void IncProgress()
        {
            ProgressBar.Value = ++_rowCounter * 100 / _totalCount;
            LblProgress.Content = $"{ProgressBar.Value}%";
        }

        public void Finish()
        {
            ProgressBar.Value = 100;
            BtnOk.Visibility = Visibility.Visible;
            LblText.Content = @"Готово!";
        }

        private void Progress_FormClosing(object sender, FormClosingEventArgs e)
        {
            CancellationTokenSource?.Cancel();
        }

        private void BtnOk_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnCancel_OnClick(object sender, RoutedEventArgs e)
        {
            if (CancellationTokenSource != null)
            {
                CancellationTokenSource.Cancel();
                BtnCancel.IsEnabled = false;
            }
        }
    }
}
