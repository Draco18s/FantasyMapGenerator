using SharpVoronoiLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OLearyMapGen.math
{
	public struct Extents2d
	{
		public readonly double minX;
		public readonly double minY;
		public readonly double maxX;
		public readonly double maxY;

		public double Width => maxX - minX;
		public double Height => maxY - minY;

		public Extents2d(double x1, double y1, double x2, double y2)
		{
			minX = x1;
			minY = y1;
			maxX = x2;
			maxY = y2;
		}

		public bool ContainsPoint(VoronoiPoint v)
		{
			return v.X >= minX && v.X <= maxX && v.Y >= minY && v.Y <= maxY;
		}

		public bool ContainsPoint(VoronoiSite v)
		{
			return v.X >= minX && v.X <= maxX && v.Y >= minY && v.Y <= maxY;
		}
	}
}
