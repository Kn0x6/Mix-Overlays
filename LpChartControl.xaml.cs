using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MixOverlays.Models;

namespace MixOverlays.Views
{
    public partial class LpChartControl : System.Windows.Controls.UserControl
    {
        private List<LpSnapshot> _snapshots = new();

        // ─── Couleurs ─────────────────────────────────────────────────────────
        private static readonly Brush LineBrush    = new SolidColorBrush(Color.FromRgb(0x59, 0xA6, 0xEF));
        private static readonly Brush LineForecast = new SolidColorBrush(Color.FromArgb(0x80, 0x59, 0xA6, 0xEF));
        private static readonly Brush GainBrush    = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));
        private static readonly Brush LossBrush    = new SolidColorBrush(Color.FromRgb(0xF8, 0x51, 0x49));
        private static readonly Brush GridBrush    = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
        private static readonly Brush GridDivBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        private static readonly Brush TextBrush    = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63));
        private static readonly Brush DotBorder    = new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17));

        private static LinearGradientBrush MakeFill(byte a) => new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(a,    0x59, 0xA6, 0xEF), 0.0),
                new GradientStop(Color.FromArgb(0x00, 0x59, 0xA6, 0xEF), 1.0),
            },
            new Point(0, 0), new Point(0, 1));

        public LpChartControl() => InitializeComponent();

        public void SetSnapshots(IEnumerable<LpSnapshot> snapshots)
        {
            _snapshots = snapshots.Take(30).Reverse().ToList();
            App.Log($"[LpChart] SetSnapshots — {_snapshots.Count} points, ActualSize={ChartCanvas.ActualWidth}x{ChartCanvas.ActualHeight}");
            Redraw();
        }

        // ── Padding (identique à Redraw) ──────────────────────────────────────────
        private const double PadLeft   = 24;
        private const double PadRight  = 6;
        private const double PadTop    = 4;
        private const double PadBottom = 13;

        private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

        private void ChartCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_snapshots.Count < 2) { TooltipBorder.Visibility = Visibility.Collapsed; return; }

            var pos = e.GetPosition(ChartCanvas);
            double w  = ChartCanvas.ActualWidth;
            double h  = ChartCanvas.ActualHeight;
            double cW = w - PadLeft - PadRight;
            double cH = h - PadTop  - PadBottom;
            int    n  = _snapshots.Count;

            int idx = Math.Max(0, Math.Min(n - 1, (int)Math.Round((pos.X - PadLeft) / cW * (n - 1))));

            var    values      = BuildCumulative(_snapshots);
            double minGrid     = Math.Floor(values.Min() / 25.0) * 25.0;
            double maxGrid     = Math.Ceiling(values.Max() / 25.0) * 25.0;
            if (maxGrid <= minGrid) maxGrid = minGrid + 25;
            double range       = maxGrid - minGrid;

            // ── Rang calculé via ComputeRankLabel (déjà correct dans le code) ────────
            double deltaFromBase = values[idx] - values[0];
            string rankLabel     = ComputeRankLabel(_snapshots[0], deltaFromBase);

            // ── Remplir le tooltip (date + rang/LP seulement, pas de gain) ────────────
            var snap = _snapshots[idx];
            TipDate.Text = snap.Timestamp.ToLocalTime().ToString("dd/MM");
            TipRank.Text = $"{rankLabel} — {(int)values[idx] % 100} LP";

            // ── Position du tooltip ───────────────────────────────────────────────────
            double cx  = PadLeft + idx * cW / Math.Max(n - 1, 1);
            double cy  = PadTop  + cH - (values[idx] - minGrid) / range * cH;
            double tx  = cx + 10;
            double ty  = cy - 42;
            if (tx + 100 > w) tx = cx - 108;
            if (ty < 0)       ty = cy + 6;

            TooltipBorder.Margin     = new Thickness(tx, ty, 0, 0);
            TooltipBorder.Visibility = Visibility.Visible;

            // ── Ligne verticale + point de survol ─────────────────────────────────────
            Redraw();
            ChartCanvas.Children.Add(new Line
            {
                X1 = cx, X2 = cx, Y1 = PadTop, Y2 = PadTop + cH,
                Stroke = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 2 }
            });
            var dot = new Ellipse { Width = 7, Height = 7, Fill = LineBrush };
            Canvas.SetLeft(dot, cx - 3.5);
            Canvas.SetTop(dot,  cy - 3.5);
            ChartCanvas.Children.Add(dot);
        }

        private void ChartCanvas_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            TooltipBorder.Visibility = Visibility.Collapsed;
            Redraw();
        }

        // ── Calcule le rang correct depuis la valeur LP cumulée ──────────────────
        // Le snapshot[0] est le plus récent (position de référence réelle)
        private static string ComputeRankDisplay(LpSnapshot reference, double cumulativeValue)
        {
            // LP absolu = LP du snap[0] + delta cumulé par rapport à snap[0]
            // Attention : _snapshots est ordonné récent→ancien, values[0]=snap[0].LP
            // donc cumulativeValue est déjà l'absolu LP relatif
            double absoluteLp = cumulativeValue;

            string[] tiers = { "Iron", "Bronze", "Silver", "Gold", "Platinum", "Emerald", "Diamond" };
            string[] divs  = { "IV", "III", "II", "I" };

            int baseTier = TierVal(reference.Tier);
            int baseDiv  = RankVal(reference.Rank);
            int baseLp   = reference.LeaguePoints;

            // LP total absolu depuis Iron IV 0
            int baseAbsolute = baseTier * 400 + baseDiv * 100 + baseLp;
            int absTotal     = (int)Math.Round(baseAbsolute + (absoluteLp - baseLp));

            // Clamp
            absTotal = Math.Max(0, absTotal);

            int tier = Math.Min(absTotal / 400, tiers.Length - 1);
            int rem  = absTotal % 400;
            int div  = Math.Min(rem / 100, 3);
            int lp   = rem % 100;

            string tierAbbr = tier switch
            {
                0 => "F", 1 => "B", 2 => "S", 3 => "G", 4 => "P", 5 => "E", 6 => "D", _ => "?"
            };

            return $"{tierAbbr}{div + 1} — {lp} LP";
        }

        private static double BuildMinGrid(List<double> values)
            => Math.Floor(values.Min() / 25.0) * 25.0;

        private static double BuildRange(List<double> values)
        {
            double min = BuildMinGrid(values);
            double max = Math.Ceiling(values.Max() / 25.0) * 25.0;
            return Math.Max(max - min, 25);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  RENDU PRINCIPAL
        // ═════════════════════════════════════════════════════════════════════

        private void Redraw()
        {
            ChartCanvas.Children.Clear();
            double w = ChartCanvas.ActualWidth;
            double h = ChartCanvas.ActualHeight;

            App.Log($"[LpChart] Redraw — size={w}x{h}, snapshots={_snapshots.Count}");

            if (w < 10 || h < 10 || _snapshots.Count < 2)
            {
                App.Log($"[LpChart] ⚠ Redraw ignoré : w={w} h={h} snapshots={_snapshots.Count}");
                DrawPlaceholder(w, h);
                return;
            }

            bool isForecast = _snapshots.All(s =>
                s.ChampionName == "Previsionnel" ||
                s.ChampionName == "Prévisionnel" ||
                s.ChampionName == "Actuel");

            // ── Padding asymétrique ───────────────────────────────────────────
            // Gauche : labels rang (ex: "P4")
            // Bas    : labels dates
            const double padLeft   = 24;
            const double padRight  = 6;
            const double padTop    = 4;
            const double padBottom = 13;

            double cW = w - padLeft - padRight;
            double cH = h - padTop  - padBottom;

            var values = BuildCumulative(_snapshots);
            double minV = values.Min();
            double maxV = values.Max();

            // Étendre aux multiples de 25 pour aligner les lignes de division
            double minGrid = Math.Floor(minV / 25.0) * 25.0;
            double maxGrid = Math.Ceiling(maxV / 25.0) * 25.0;
            if (maxGrid <= minGrid) maxGrid = minGrid + 25;
            double range = maxGrid - minGrid;

            double px(int i)    => padLeft + i * cW / Math.Max(values.Count - 1, 1);
            double py(double v) => padTop  + cH - (v - minGrid) / range * cH;

            // ── Axe Y : lignes tous les 25 LP + labels rang ───────────────────
            DrawYAxis(w, padLeft, padRight, padTop, cH, minGrid, maxGrid, values[0]);

            // ── Remplissage dégradé ───────────────────────────────────────────
            var pts = Enumerable.Range(0, values.Count)
                .Select(i => new Point(px(i), py(values[i])))
                .ToList();

            var fillPts = new PointCollection(pts)
            {
                new Point(pts.Last().X,  padTop + cH),
                new Point(pts.First().X, padTop + cH),
            };
            ChartCanvas.Children.Add(new Polygon
            {
                Points = fillPts,
                Fill   = MakeFill(isForecast ? (byte)0x10 : (byte)0x30),
            });

            // ── Courbe ────────────────────────────────────────────────────────
            var curve = new Polyline
            {
                Points          = new PointCollection(pts),
                Stroke          = isForecast ? LineForecast : LineBrush,
                StrokeThickness = isForecast ? 1.5 : 2.0,
                StrokeLineJoin  = PenLineJoin.Round,
            };
            if (isForecast) curve.StrokeDashArray = new DoubleCollection { 5, 3 };
            ChartCanvas.Children.Add(curve);

            // ── Point final ───────────────────────────────────────────────────
            var last   = pts.Last();
            bool isGain = (_snapshots.Last().LpDelta ?? 0) >= 0;

            var outer = new Ellipse { Width = 10, Height = 10, Fill = DotBorder };
            Canvas.SetLeft(outer, last.X - 5);
            Canvas.SetTop(outer,  last.Y - 5);
            ChartCanvas.Children.Add(outer);

            var inner = new Ellipse
            {
                Width = 6, Height = 6,
                Fill  = isForecast ? LineForecast : (isGain ? GainBrush : LossBrush),
            };
            Canvas.SetLeft(inner, last.X - 3);
            Canvas.SetTop(inner,  last.Y - 3);
            ChartCanvas.Children.Add(inner);

            // ── Badge prévisionnel ────────────────────────────────────────────
            if (isForecast)
            {
                var lbl = new TextBlock
                {
                    Text       = "✦ prévisionnel",
                    Foreground = new SolidColorBrush(Color.FromArgb(0x60, 0x59, 0xA6, 0xEF)),
                    FontSize   = 7,
                };
                Canvas.SetRight(lbl, 2);
                Canvas.SetTop(lbl, padTop);
                ChartCanvas.Children.Add(lbl);
            }

            // ── Axe X : dates aux extrémités ──────────────────────────────────
            DrawXAxis(w, h, padLeft, padRight, padBottom);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  AXE Y — Lignes tous les 25 LP avec labels rang
        // ═════════════════════════════════════════════════════════════════════

        private void DrawYAxis(double w, double padLeft, double padRight,
                               double padTop, double cH,
                               double minGrid, double maxGrid, double baseValue)
        {
            double range = maxGrid - minGrid;

            for (double lp = minGrid; lp <= maxGrid; lp += 25)
            {
                double y = padTop + cH - (lp - minGrid) / range * cH;

                // Ignorer les lignes hors zone visible
                if (y < padTop - 1 || y > padTop + cH + 1) continue;

                // Est-ce une frontière de division (multiple de 100) ?
                bool isDivBoundary = (lp % 100 == 0);

                // Ligne de grille
                ChartCanvas.Children.Add(new Line
                {
                    X1 = padLeft, X2 = w - padRight,
                    Y1 = y,       Y2 = y,
                    Stroke          = isDivBoundary ? GridDivBrush : GridBrush,
                    StrokeThickness = isDivBoundary ? 1.0 : 0.8,
                    StrokeDashArray = new DoubleCollection { 3, 3 },
                });

                // Label rang à gauche
                double deltaFromBase = lp - baseValue;
                string label = ComputeRankLabel(_snapshots[0], deltaFromBase);
                var brush  = RankLabelBrush(label);

                var tb = new TextBlock
                {
                    Text       = label,
                    Foreground = brush,
                    FontSize   = 7,
                    FontWeight = isDivBoundary ? FontWeights.Bold : FontWeights.Normal,
                };
                Canvas.SetLeft(tb, 1);
                Canvas.SetTop(tb, y - 7);
                ChartCanvas.Children.Add(tb);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  AXE X — Dates aux extrémités
        // ═════════════════════════════════════════════════════════════════════

        private void DrawXAxis(double w, double h, double padLeft, double padRight, double padBottom)
        {
            if (_snapshots.Count == 0) return;

            var oldest = _snapshots.First().Timestamp.ToLocalTime();
            var newest = _snapshots.Last().Timestamp.ToLocalTime();

            string leftLabel  = FormatDate(oldest);
            string rightLabel = IsToday(newest) ? "Auj." : FormatDate(newest);

            double yDate = h - padBottom + 2;

            // Label gauche
            var tbLeft = new TextBlock
            {
                Text       = leftLabel,
                Foreground = TextBrush,
                FontSize   = 7,
            };
            Canvas.SetLeft(tbLeft, padLeft);
            Canvas.SetTop(tbLeft, yDate);
            ChartCanvas.Children.Add(tbLeft);

            // Label droit
            double approxCharWidth = 4.2;
            var tbRight = new TextBlock
            {
                Text       = rightLabel,
                Foreground = TextBrush,
                FontSize   = 7,
            };
            Canvas.SetLeft(tbRight, w - padRight - rightLabel.Length * approxCharWidth);
            Canvas.SetTop(tbRight, yDate);
            ChartCanvas.Children.Add(tbRight);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  HELPERS RANG
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Calcule le label rang (ex: "P4", "E1", "D3") à partir d'un snapshot
        /// de base et d'un delta LP cumulé depuis ce snapshot.
        /// </summary>
        private static string ComputeRankLabel(LpSnapshot baseSnap, double deltaFromBase)
        {
            int baseAbsLp = TierVal(baseSnap.Tier) * 400
                          + RankVal(baseSnap.Rank)  * 100
                          + baseSnap.LeaguePoints;

            int absLp = Math.Max(0, baseAbsLp + (int)deltaFromBase);

            int tier = Math.Min(absLp / 400, 9);
            int rank = Math.Min((absLp % 400) / 100, 3);

            string tierAbbr = tier switch
            {
                0 => "I",   // Iron
                1 => "B",   // Bronze
                2 => "S",   // Silver
                3 => "G",   // Gold
                4 => "P",   // Platinum
                5 => "E",   // Emerald
                6 => "D",   // Diamond
                7 => "M",   // Master
                8 => "GM",  // GrandMaster
                9 => "C",   // Challenger
                _ => "?"
            };

            // Master+ n'a pas de divisions
            string rankAbbr = tier >= 7 ? "" : (rank switch
            {
                0 => "4",
                1 => "3",
                2 => "2",
                3 => "1",
                _ => ""
            });

            return tierAbbr + rankAbbr; // ex: "P4", "E1", "D3", "M"
        }

        /// <summary>Couleur associée au rang pour le label.</summary>
        private static Brush RankLabelBrush(string label)
        {
            if (label.StartsWith("C"))  return new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xFF));
            if (label.StartsWith("GM")) return new SolidColorBrush(Color.FromRgb(0xFF, 0x7F, 0x00));
            if (label.StartsWith("M"))  return new SolidColorBrush(Color.FromRgb(0x9B, 0x59, 0xB6));
            if (label.StartsWith("D"))  return new SolidColorBrush(Color.FromRgb(0xA8, 0xD8, 0xEA));
            if (label.StartsWith("E"))  return new SolidColorBrush(Color.FromRgb(0x50, 0xC8, 0x78));
            if (label.StartsWith("P"))  return new SolidColorBrush(Color.FromRgb(0x00, 0xB4, 0xAA));
            if (label.StartsWith("G"))  return new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
            if (label.StartsWith("S"))  return new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0));
            if (label.StartsWith("B"))  return new SolidColorBrush(Color.FromRgb(0xCD, 0x7F, 0x32));
            return new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)); // Iron / défaut
        }

        // ═════════════════════════════════════════════════════════════════════
        //  HELPERS DATES
        // ═════════════════════════════════════════════════════════════════════

        private static string FormatDate(DateTime dt)
        {
            var now = DateTime.Now;
            if ((now - dt).TotalDays < 1)   return "Auj.";
            if ((now - dt).TotalDays < 2)   return "Hier";
            if ((now - dt).TotalDays < 7)   return dt.ToString("ddd");   // "Lun"
            if (dt.Year == now.Year)         return dt.ToString("d MMM"); // "14 fév"
            return dt.ToString("MM/yy");
        }

        private static bool IsToday(DateTime dt) =>
            dt.ToLocalTime().Date == DateTime.Today;

        // ═════════════════════════════════════════════════════════════════════
        //  PLACEHOLDER
        // ═════════════════════════════════════════════════════════════════════

        private void DrawPlaceholder(double w, double h)
        {
            var tb = new TextBlock
            {
                Text          = "Chargement…",
                Foreground    = TextBrush,
                FontSize      = 9,
                TextAlignment = TextAlignment.Center,
                Width         = w,
            };
            Canvas.SetLeft(tb, 0);
            Canvas.SetTop(tb, h / 2 - 8);
            ChartCanvas.Children.Add(tb);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  VALEURS CUMULÉES
        // ═════════════════════════════════════════════════════════════════════

        private static List<double> BuildCumulative(List<LpSnapshot> snaps)
        {
            var r = new List<double> { snaps[0].LeaguePoints };
            for (int i = 1; i < snaps.Count; i++)
            {
                double d = snaps[i].LpDelta.HasValue
                    ? snaps[i].LpDelta!.Value
                    : snaps[i].LeaguePoints - snaps[i - 1].LeaguePoints;
                r.Add(r.Last() + d);
            }
            return r;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  CONVERSION TIER/RANK
        // ═════════════════════════════════════════════════════════════════════

        private static int TierVal(string t) => t?.ToUpper() switch
        {
            "IRON"        => 0, "BRONZE"      => 1, "SILVER"      => 2,
            "GOLD"        => 3, "PLATINUM"    => 4, "EMERALD"     => 5,
            "DIAMOND"     => 6, "MASTER"      => 7, "GRANDMASTER" => 8,
            "CHALLENGER"  => 9, _ => 0
        };

        private static int RankVal(string r) => r?.ToUpper() switch
        { "IV" => 0, "III" => 1, "II" => 2, "I" => 3, _ => 0 };
    }
}