using System.Windows.Controls;

namespace MixOverlays.Views
{
    /// <summary>
    /// Panneau latéral affichant les statistiques par champion
    /// calculées depuis l'historique récent (TopChampionsFromHistory).
    /// DataContext attendu : PlayerViewModel.
    /// </summary>
    public partial class ChampionStatsCard : UserControl
    {
        public ChampionStatsCard()
        {
            InitializeComponent();
        }
    }
}
