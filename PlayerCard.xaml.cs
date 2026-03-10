using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MixOverlays.ViewModels;

namespace MixOverlays.Views
{
    public partial class PlayerCard : UserControl
    {
        private PlayerViewModel? _vm;

        public PlayerCard()
        {
            InitializeComponent();
        }

        private void MatchRow_StopBubble(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property != DataContextProperty) return;

            if (_vm != null)
            {
                App.Log("[PlayerCard] Désabonnement ancien VM");
                _vm.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (DataContext is PlayerViewModel vm)
            {
                _vm = vm;
                _vm.PropertyChanged += OnViewModelPropertyChanged;
                App.Log($"[PlayerCard] DataContext set — LpSnapshots={vm.LpSnapshots.Count}, HasLpData={vm.HasLpData}");
                LpChart?.SetSnapshots(vm.LpSnapshots);
            }
            else
            {
                App.Log($"[PlayerCard] DataContext n'est pas un PlayerViewModel : {DataContext?.GetType().Name ?? "null"}");
                _vm = null;
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            App.Log($"[PlayerCard] PropertyChanged reçu : {e.PropertyName}");
            if (e.PropertyName == nameof(PlayerViewModel.LpSnapshots) && _vm != null)
            {
                App.Log($"[PlayerCard] → SetSnapshots({_vm.LpSnapshots.Count} points)");
                LpChart?.SetSnapshots(_vm.LpSnapshots);
            }
        }
    }
}