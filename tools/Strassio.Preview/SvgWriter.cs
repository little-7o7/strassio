using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Preview
{
    /// <summary>Рисует результат алгоритмов Core в SVG — только для визуальной проверки, не для производства.</summary>
    internal static class SvgWriter
    {
        public static string Render(
            IReadOnlyList<Point2D> referencePolyline,
            bool referenceClosed,
            IReadOnlyList<PlacedStone> stones,
            double marginMm = 5,
            double pixelsPerMm = 8)
        {
            return RenderMulti(
                new[] { (Points: referencePolyline, Color: "#cccccc") },
                stones, marginMm, pixelsPerMm);
        }

        /// <summary>
        /// Картинка для сайта: только камни, одинаковые, без служебных цветов (красным помечают
        /// углы) и без контура фигуры. Камень рисуется как настоящая страза — светлая середина,
        /// края темнее, сверху блик; цвет взят из логотипа Strassio.
        /// </summary>
        public static string RenderSite(
            IReadOnlyList<PlacedStone> stones,
            double marginMm = 2,
            double pixelsPerMm = 12)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (PlacedStone s in stones)
            {
                double r = s.DiameterMm / 2;
                if (s.Center.X - r < minX) minX = s.Center.X - r;
                if (s.Center.Y - r < minY) minY = s.Center.Y - r;
                if (s.Center.X + r > maxX) maxX = s.Center.X + r;
                if (s.Center.Y + r > maxY) maxY = s.Center.Y + r;
            }

            if (stones.Count == 0)
            {
                minX = minY = 0;
                maxX = maxY = 1;
            }

            minX -= marginMm; minY -= marginMm; maxX += marginMm; maxY += marginMm;

            var ci = CultureInfo.InvariantCulture;
            double w = (maxX - minX) * pixelsPerMm;
            double h = (maxY - minY) * pixelsPerMm;

            string N(double v) => v.ToString("0.##", ci);
            double X(double mmX) => (mmX - minX) * pixelsPerMm;
            double Y(double mmY) => (mmY - minY) * pixelsPerMm;

            var sb = new StringBuilder();
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(w)}\" height=\"{N(h)}\" viewBox=\"0 0 {N(w)} {N(h)}\">");
            sb.AppendLine("<defs>");
            sb.AppendLine("<radialGradient id=\"stone\" cx=\"38%\" cy=\"32%\" r=\"72%\">");
            sb.AppendLine("<stop offset=\"0\" stop-color=\"#EAF4FF\"/>");
            sb.AppendLine("<stop offset=\"0.55\" stop-color=\"#7CB2F0\"/>");
            sb.AppendLine("<stop offset=\"1\" stop-color=\"#2F74E0\"/>");
            sb.AppendLine("</radialGradient>");
            sb.AppendLine("</defs>");

            foreach (PlacedStone s in stones)
            {
                double r = s.DiameterMm / 2 * pixelsPerMm;
                sb.AppendLine($"<circle cx=\"{N(X(s.Center.X))}\" cy=\"{N(Y(s.Center.Y))}\" r=\"{N(r)}\" fill=\"url(#stone)\" stroke=\"#0F3A8A\" stroke-opacity=\"0.35\" stroke-width=\"{N(r * 0.08)}\"/>");
                sb.AppendLine($"<circle cx=\"{N(X(s.Center.X) - r * 0.28)}\" cy=\"{N(Y(s.Center.Y) - r * 0.32)}\" r=\"{N(r * 0.22)}\" fill=\"#FFFFFF\" fill-opacity=\"0.75\"/>");
            }

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        public static string RenderMulti(
            IReadOnlyList<(IReadOnlyList<Point2D> Points, string Color)> polylines,
            IReadOnlyList<PlacedStone> stones,
            double marginMm = 5,
            double pixelsPerMm = 8)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

            void Expand(Point2D p, double r)
            {
                if (p.X - r < minX) minX = p.X - r;
                if (p.Y - r < minY) minY = p.Y - r;
                if (p.X + r > maxX) maxX = p.X + r;
                if (p.Y + r > maxY) maxY = p.Y + r;
            }

            foreach ((IReadOnlyList<Point2D> pts, _) in polylines)
            {
                foreach (Point2D p in pts)
                {
                    Expand(p, 0);
                }
            }

            foreach (PlacedStone s in stones)
            {
                Expand(s.Center, s.DiameterMm / 2);
            }

            minX -= marginMm; minY -= marginMm; maxX += marginMm; maxY += marginMm;
            double widthMm = maxX - minX;
            double heightMm = maxY - minY;

            var sb = new StringBuilder();
            var ci = CultureInfo.InvariantCulture;

            double W = widthMm * pixelsPerMm;
            double H = heightMm * pixelsPerMm;

            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{W.ToString("0.##", ci)}\" height=\"{H.ToString("0.##", ci)}\" viewBox=\"0 0 {W.ToString("0.##", ci)} {H.ToString("0.##", ci)}\">");
            sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");

            double X(double mmX) => (mmX - minX) * pixelsPerMm;
            double Y(double mmY) => (mmY - minY) * pixelsPerMm;

            foreach ((IReadOnlyList<Point2D> pts, string color) in polylines)
            {
                if (pts.Count < 2)
                {
                    continue;
                }

                sb.Append("<polyline points=\"");
                foreach (Point2D p in pts)
                {
                    sb.Append(X(p.X).ToString("0.##", ci)).Append(',').Append(Y(p.Y).ToString("0.##", ci)).Append(' ');
                }

                sb.AppendLine($"\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1\"/>");
            }

            foreach (PlacedStone s in stones)
            {
                double r = s.DiameterMm / 2 * pixelsPerMm;
                string fill = s.IsCorner ? "#E53935" : "#43A047";
                sb.AppendLine($"<circle cx=\"{X(s.Center.X).ToString("0.##", ci)}\" cy=\"{Y(s.Center.Y).ToString("0.##", ci)}\" r=\"{r.ToString("0.##", ci)}\" fill=\"{fill}\" fill-opacity=\"0.85\" stroke=\"#333333\" stroke-width=\"0.5\"/>");
            }

            sb.AppendLine("</svg>");
            return sb.ToString();
        }
    }
}
