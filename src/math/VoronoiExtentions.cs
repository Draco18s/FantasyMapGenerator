using SharpVoronoiLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OLearyMapGen.math
{
	public static class VoronoiExtentions
	{
		public static VoronoiEdge IncidentEdge(this VoronoiPlane plane, VoronoiPoint v)
		{
			return plane.Edges.Where(e => e.Start == v || e.End == v).FirstOrDefault();
		}

		public static List<VoronoiPoint> GetAllVerticies(this VoronoiPlane map)
		{
			Debug.Assert(map.Edges != null, "map.Edges != null");
			return map.Edges.Select(e => e.Start).Concat(map.Edges.Select(e => e.End)).Distinct().ToList();
		}

		public static bool Equals(this VoronoiPoint p1, VoronoiPoint p2)
		{
			return Math.Abs(p1.X - p2.X) < 0.001 && Math.Abs(p1.Y - p2.Y) < 0.001;
		}

		public static int GetHashCode(this VoronoiPoint p)
		{
			return ((int)(p.X * 1000)).GetHashCode() ^ ((int)(p.Y * 1000)).GetHashCode();
		}

		public static bool Equals(this VoronoiSite p1, VoronoiSite p2)
		{
			return Math.Abs(p1.X - p2.X) < 0.001 && Math.Abs(p1.Y - p2.Y) < 0.001;
		}

		public static int GetHashCode(this VoronoiSite p)
		{
			return ((int)(p.X * 1000)).GetHashCode() ^ ((int)(p.Y * 1000)).GetHashCode();
		}

		public static bool IsVertex(this VoronoiPoint v)
		{
			return true;
		}

		public static bool IsVertex(this VoronoiSite v)
		{
			return false;
		}
	}
}
