using System;

namespace Strassio.Core.Geometry
{
    /// <summary>Точка или вектор на плоскости, в миллиметрах.</summary>
    public readonly struct Point2D : IEquatable<Point2D>
    {
        public double X { get; }
        public double Y { get; }

        public Point2D(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static Point2D Zero => new Point2D(0, 0);

        public static Point2D operator +(Point2D a, Point2D b) => new Point2D(a.X + b.X, a.Y + b.Y);
        public static Point2D operator -(Point2D a, Point2D b) => new Point2D(a.X - b.X, a.Y - b.Y);
        public static Point2D operator *(Point2D a, double k) => new Point2D(a.X * k, a.Y * k);
        public static Point2D operator *(double k, Point2D a) => new Point2D(a.X * k, a.Y * k);
        public static Point2D operator /(Point2D a, double k) => new Point2D(a.X / k, a.Y / k);
        public static Point2D operator -(Point2D a) => new Point2D(-a.X, -a.Y);
        public static bool operator ==(Point2D a, Point2D b) => a.Equals(b);
        public static bool operator !=(Point2D a, Point2D b) => !a.Equals(b);

        public double Length => Math.Sqrt(X * X + Y * Y);

        public double Dot(Point2D other) => X * other.X + Y * other.Y;

        /// <summary>Z-компонента векторного произведения (this × other) — знак показывает сторону поворота.</summary>
        public double Cross(Point2D other) => X * other.Y - Y * other.X;

        public static double Distance(Point2D a, Point2D b) => (a - b).Length;

        public Point2D Normalized()
        {
            double len = Length;
            return len < 1e-12 ? Zero : new Point2D(X / len, Y / len);
        }

        public static Point2D Lerp(Point2D a, Point2D b, double t) => a + (b - a) * t;

        public bool Equals(Point2D other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object? obj) => obj is Point2D other && Equals(other);
        public override int GetHashCode() => X.GetHashCode() * 397 ^ Y.GetHashCode();
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }
}
