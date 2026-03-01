using System.Windows.Controls;

namespace MixOverlays.Views
{
    /// <summary>
    /// Carte joueur en forme de losange vertical, affichée dans l'overlay in-game.
    /// DataContext attendu : PlayerViewModel.
    /// </summary>
    public partial class DiamondPlayerCard : UserControl
    {
        public DiamondPlayerCard()
        {
            InitializeComponent();
        }
    }
}
