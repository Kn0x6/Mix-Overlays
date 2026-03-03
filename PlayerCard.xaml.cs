using System.Windows.Controls;
using System.Windows.Input;
using MixOverlays.Models;

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
    }
}
