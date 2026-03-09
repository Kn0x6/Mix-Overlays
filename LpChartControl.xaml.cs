using System;
using System.Collections.Generic;
using System.Linq;

// Types WPF explicites — pas d'ambiguïté possible
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

        // ─── Couleurs (types Media explicites) ────────────────────────────────
        private static readonly System.Windows.Media.Brush LineBrush    = new SolidColorBrush(Color.FromRgb(0x59, 0xA6, 0xEF));
        private static readonly System.Windows.Media.Brush LineForecast = new SolidColorBrush(Color.FromArgb(0x80, 0x59, 0xA6, 0xEF));
        private static readonly System.Windows.Media.Brush GainBrush    = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));
        private static readonly System.Windows.Media.Brush LossBrush    = new SolidColorBrush(Color.FromRgb(0xF8, 0x51, 0x49));
        private static readonly System.Windows.Media.Brush GridBrush    = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
        private static readonly System.Windows.Media.Brush TextBrush    = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E));
        private static readonly System.Windows.Media.Brush DotBorder    = new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17));

        private static LinearGradientBrush MakeFill(byte a) => new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(a,    0x59, 0xA6, 0xEF), 0.0),
                new GradientStop(Color.FromArgb(0x00, 0x59, 0xA6, 0xEF), 1.0),
            },
            new System.Windows.Point(0, 0),
            new System.Windows.Point(0, 1));

        public LpChartControl() => InitializeComponent();

        // ─── API ──────────────────────────────────────────────────────────────

        public void SetSnapshots(IEnumerable<LpSnapshot> snapshots)
        {
            _snapshots = snapshots.Take(30).Reverse().ToList();
            Redraw();
        }

        private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

        // ─── Rendu ────────────────────────────────────────────────────────────

        private void Redraw()
        {
            ChartCanvas.Children.Clear();
            double w = ChartCanvas.ActualWidth;
            double h = ChartCanvas.ActualHeight;

            if (w < 10 || h < 10 || _snapshots.Count < 2)
            {
                DrawPlaceholder(w, h);
                return;
            }

            bool isForecast = _snapshots.All(s =>
                s.ChampionName == "Previsionnel"  ||
                s.ChampionName == "Prévisionnel"  ||
                s.ChampionName == "Actuel");

            const double padX = 6, padY = 10;
            var values  = BuildCumulative(_snapshots);
            double minV = values.Min(), maxV = values.Max();
            double range = Math.Max(maxV - minV, 1);
            double cW = w - padX * 2, cH = h - padY * 2;

            double px(int i) => padX + i * cW / Math.Max(values.Count - 1, 1);
            double py(double v) => padY + cH - (v - minV) / range * cH;

            var pts = Enumerable.Range(0, values.Count)
                .Select(i => new System.Windows.Point(px(i), py(values[i])))
                .ToList();

            // Grille horizontale
            for (int g = 0; g <= 2; g++)
            {
                ChartCanvas.Children.Add(new Line
                {
                    X1 = padX, X2 = w - padX,
                    Y1 = padY + g * cH / 2,
                    Y2 = padY + g * cH / 2,
                    Stroke          = GridBrush,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 3, 3 },
                });
            }

            // Remplissage
            var fillPts = new PointCollection(pts)
            {
                new System.Windows.Point(pts.Last().X,  h - padY),
                new System.Windows.Point(pts.First().X, h - padY),
            };
            ChartCanvas.Children.Add(new Polygon
            {
                Points = fillPts,
                Fill   = MakeFill(isForecast ? (byte)0x15 : (byte)0x35),
            });

            // Courbe
            var curve = new Polyline
            {
                Points          = new PointCollection(pts),
                Stroke          = isForecast ? LineForecast : LineBrush,
                StrokeThickness = isForecast ? 1.5 : 2.0,
                StrokeLineJoin  = PenLineJoin.Round,
            };
            if (isForecast)
                curve.StrokeDashArray = new DoubleCollection { 5, 3 };
            ChartCanvas.Children.Add(curve);

            // Point final coloré
            var last    = pts.Last();
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

            // Badge "prévisionnel"
            if (isForecast)
            {
                var lbl = new TextBlock
                {
                    Text       = "✦ prévisionnel",
                    Foreground = new SolidColorBrush(Color.FromArgb(0x70, 0x59, 0xA6, 0xEF)),
                    FontSize   = 8,
                };
                Canvas.SetRight(lbl, 2);
                Canvas.SetTop(lbl,   0);
                ChartCanvas.Children.Add(lbl);
            }
        }

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
            Canvas.SetTop(tb,  h / 2 - 8);
            ChartCanvas.Children.Add(tb);
        }

        // ─── Valeurs cumulées ─────────────────────────────────────────────────

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
    }
}
