using System.Windows;
using System.Windows.Input;
using MixOverlays.ViewModels;
using MixOverlays.Models;

namespace MixOverlays.Views
{
    public partial class MatchDetailWindow : Window
    {
        public MatchDetailWindow(MatchDetailViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragMove();

        private void CloseButton_Click(object sender, RoutedEventArgs e)
            => Close();
    }
}