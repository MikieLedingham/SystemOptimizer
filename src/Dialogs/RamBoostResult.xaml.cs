// File: RamBoostResult.xaml.cs
using System;
using System.Windows;
using System.Windows.Threading;
using SystemOptimizer.Core.Settings;

namespace SystemOptimizer.Dialogs
{
    public partial class RamBoostResult : Window
    {
        private const int AutoCloseSeconds = 5;
        private DispatcherTimer _timer;
        private int _remaining = AutoCloseSeconds;

        public RamBoostResult(string message)
        {
            InitializeComponent();

            MessageTextBlock.Text = string.IsNullOrWhiteSpace(message) ? "No boost recorded yet" : message;
            WhenTextBlock.Text = DescribeLastBoost();

            // Dismiss on any click, so the countdown is a floor rather than a wait.
            MouseLeftButtonDown += (s, e) => Close();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) =>
            {
                _remaining--;
                if (_remaining <= 0) { _timer.Stop(); Close(); }
                else UpdateCountdown();
            };
            UpdateCountdown();
            _timer.Start();
        }

        private void UpdateCountdown()
            => CountdownTextBlock.Text = $"Closing in {_remaining}s - click to dismiss";

        /// <summary>"Automatic, 2 minutes ago" / "Manual, at 14:32".</summary>
        private static string DescribeLastBoost()
        {
            var when = PreferencesManager.GetLastRamBoostTime();
            string kind = PreferencesManager.GetLastRamBoostWasAutomatic() ? "Automatic" : "Manual";

            if (when == null) return kind + " boost";

            var ago = DateTime.Now - when.Value;
            string rel =
                ago.TotalSeconds < 60 ? "just now" :
                ago.TotalMinutes < 60 ? $"{(int)ago.TotalMinutes} min ago" :
                ago.TotalHours   < 24 ? $"{(int)ago.TotalHours} hr ago" :
                                        $"{(int)ago.TotalDays} days ago";

            return $"{kind} boost, {rel} ({when.Value:HH:mm})";
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer?.Stop();
            base.OnClosed(e);
        }
    }
}
