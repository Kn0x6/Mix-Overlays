using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using MixOverlays.Models;
using MixOverlays.ViewModels;

namespace MixOverlays.Views
{
    public partial class PlayerCard : UserControl
    {
        public PlayerCard()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Arrête la propagation du clic sur une ligne de match vers les éléments parents.
        /// Sans cela, le clic sur une ligne peut déclencher une recherche de joueur
        /// si la souris survole un élément cliquable dans le parent (LiveSession, etc.).
        /// Le MouseBinding InputBinding sur la Border gère déjà la commande via l'événement
        /// tunneling (Preview) — on stoppe ici uniquement la phase bubbling.
        /// </summary>
        private void MatchRow_StopBubble(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        /// <summary>
        /// Rafraîchit le graphique LP lorsque le DataContext change.
        /// Cette méthode est appelée automatiquement lorsque le DataContext est mis à jour.
        /// </summary>
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == DataContextProperty && DataContext is PlayerViewModel vm)
                LpChart?.SetSnapshots(vm.LpSnapshots);
        }
    }
}
