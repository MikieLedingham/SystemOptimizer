// File: AutoCleanWarningDialog.xaml.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace SystemOptimizer.Dialogs
{
    public partial class AutoCleanWarningDialog : Window
    {
        public AutoCleanWarningDialog()
        {
            InitializeComponent();
            List<(string Name, long RamMB)> processes = Process.GetProcesses()
                .Select(p =>
                {
                    try { return (p.ProcessName, p.WorkingSet64 / 1024 / 1024); }
                    catch { return ("N/A", 0L); }
                })
                .Where(p => p.Item2 > 0 && p.Item1 != "Memory Compression")
                .OrderByDescending(p => p.Item2)
                .Take(15)
                .ToList();
            long totalMB = 0;
            foreach (var (name, ramMB) in processes)
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 2)
                };
                row.Children.Add(new TextBlock
                {
                    Text = name,
                    Width = 130,
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 10, 0),
                    TextAlignment = TextAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Left
                });
                row.Children.Add(new TextBlock
                {
                    Text = $"{ramMB} MB",
                    Width = 75,
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 0, 0),
                    TextAlignment = TextAlignment.Right,
                    HorizontalAlignment = HorizontalAlignment.Right
                });
                ProcessStack.Children.Add(row);
                totalMB += ramMB;
            }
            SummaryText.Text = $"Total RAM Used: {totalMB} MB";
        }
        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
