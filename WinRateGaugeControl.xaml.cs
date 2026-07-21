using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MixOverlays.Views
{
    /// <summary>Jauge semi-circulaire affichant le bilan Solo/Duo d'un joueur.</summary>
    public partial class WinRateGaugeControl : UserControl
    {
        private static readonly Brush WinBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x6D, 0xD9));
        private static readonly Brush LossBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0x3B, 0x78));
        private static readonly Brush EmptyBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));

        public static readonly DependencyProperty WinRateProperty = DependencyProperty.Register(
            nameof(WinRate), typeof(double), typeof(WinRateGaugeControl),
            new PropertyMetadata(0d, OnGaugeValueChanged));

        public static readonly DependencyProperty WinsProperty = DependencyProperty.Register(
            nameof(Wins), typeof(int), typeof(WinRateGaugeControl),
            new PropertyMetadata(0, OnGaugeValueChanged));

        public static readonly DependencyProperty LossesProperty = DependencyProperty.Register(
            nameof(Losses), typeof(int), typeof(WinRateGaugeControl),
            new PropertyMetadata(0, OnGaugeValueChanged));

        public double WinRate
        {
            get => (double)GetValue(WinRateProperty);
            set => SetValue(WinRateProperty, value);
        }

        public int Wins
        {
            get => (int)GetValue(WinsProperty);
            set => SetValue(WinsProperty, value);
        }

        public int Losses
        {
            get => (int)GetValue(LossesProperty);
            set => SetValue(LossesProperty, value);
        }

        public WinRateGaugeControl()
        {
            InitializeComponent();
            Loaded += (_, _) => Redraw();
        }

        private static void OnGaugeValueChanged(DependencyObject source, DependencyPropertyChangedEventArgs args) =>
            ((WinRateGaugeControl)source).Redraw();

        private void GaugeCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

        private void Redraw()
        {
            if (GaugeCanvas == null)
                return;

            GaugeCanvas.Children.Clear();

            double width = GaugeCanvas.ActualWidth;
            double height = GaugeCanvas.ActualHeight;
            const double thickness = 13;
            if (width <= thickness || height <= thickness)
                return;

            double radius = Math.Min(width / 2 - thickness / 2, height - thickness / 2 - 2);
            if (radius <= 0)
                return;

            var center = new Point(width / 2, height - thickness / 2 - 1);
            double winRatio = Math.Clamp(WinRate / 100d, 0d, 1d);

            // Une Polyline échantillonnée est volontairement utilisée à la place
            // d'ArcSegment : le sens du balayage de ce dernier peut inverser l'arc
            // en WPF et former un "V" au centre de la jauge.
            if (Wins + Losses == 0)
            {
                AddArc(180, 0, EmptyBrush, thickness);
                return;
            }

            double splitAngle = 180 - 180 * winRatio;
            if (winRatio > 0)
                AddArc(180, splitAngle, WinBrush, thickness);

            if (winRatio < 1)
                AddArc(splitAngle, 0, LossBrush, thickness);

            void AddArc(double startAngle, double endAngle, Brush stroke, double strokeThickness)
            {
                if (Math.Abs(startAngle - endAngle) < 0.01)
                    return;

                Point PointAt(double angle)
                {
                    double radians = angle * Math.PI / 180d;
                    return new Point(
                        center.X + radius * Math.Cos(radians),
                        center.Y - radius * Math.Sin(radians));
                }

                int pointCount = Math.Max(2, (int)Math.Ceiling(Math.Abs(endAngle - startAngle) / 3));
                var points = new PointCollection(pointCount + 1);
                for (int i = 0; i <= pointCount; i++)
                {
                    double progress = (double)i / pointCount;
                    points.Add(PointAt(startAngle + (endAngle - startAngle) * progress));
                }

                GaugeCanvas.Children.Add(new Polyline
                {
                    Points = points,
                    Stroke = stroke,
                    StrokeThickness = strokeThickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                });
            }
        }
    }
}