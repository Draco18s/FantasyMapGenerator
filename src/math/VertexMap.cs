using SharpVoronoiLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace OLearyMapGen.math
{

	/// <summary>
	/// Maps VoronoiPlane vertices that fall within specified extents and are not boundary vertices.
	/// </summary>
	public class VertexMap
	{
		/// <summary>
		/// Defines the type of vertex based on its location relative to the map boundary.
		/// </summary>
		public enum VertexType
		{
			Interior,
			Edge
		}

		private readonly VoronoiPlane cellMap;

		public List<VoronoiPoint> Vertices { get; }
		public List<VoronoiSite> Sites => cellMap.Sites;

		public List<VoronoiPoint> Interior { get; }
		public List<VoronoiPoint> Edge { get; }
		public List<VertexType> _vertexTypes { get; }

		public double MinX => cellMap.MinX;
		public double MaxX => cellMap.MaxX;
		public double MinY => cellMap.MinY;
		public double MaxY => cellMap.MaxY;
		public double Width => MaxX - MinX;
		public double Height => MaxY - MinY;

		public VertexMap(VoronoiPlane plane, Extents2d extents)
		{
			cellMap = plane ?? throw new ArgumentNullException(nameof(plane));

			Vertices = cellMap.GetAllVerticies();

			int dcelVertexCount = Vertices.Count;

			Interior = new List<VoronoiPoint>(dcelVertexCount);
			Edge = new List<VoronoiPoint>(dcelVertexCount);
			_vertexTypes = new List<VertexType>(dcelVertexCount);
			
			// Populate the VertexMap
			for (int i = 0; i < dcelVertexCount; i++)
			{
				VoronoiPoint v = Vertices[i];

				if (!extents.ContainsPoint(v) || IsBoundaryVertex(v))
				{
					continue;
				}

				VertexType type = GetVertexType(v);

				if (type == VertexType.Interior)
				{
					Interior.Add(v);
					_vertexTypes.Add(VertexType.Interior);
				}
				else
				{
					Edge.Add(v);
					_vertexTypes.Add(VertexType.Edge);
				}
			}
		}

		/// <summary>
		/// Returns the number of vertices mapped (internal vertices).
		/// </summary>
		public int Size()
		{
			return Vertices.Count;
		}
		
		/// <summary>
		/// Populates a list with the map indices of neighboring vertices of v.
		/// </summary>
		public List<VoronoiPoint> GetNeighbourIndices(VoronoiPoint v)
		{
			List<VoronoiEdge> edges = cellMap.Edges.Where(e => e.Start == v || e.End == v).ToList();
			return edges.Select(e => e.Start).Concat(edges.Select(e => e.End)).Where(p => p != v).ToList();
		}

		/// <summary>
		/// Checks if a VoronoiPlane vertex is included in this VertexMap.
		/// </summary>
		public bool IsVertex(VoronoiPoint v)
		{
			return true;
		}

		public bool IsVertex(VoronoiSite v)
		{
			return false;
		}

		/// <summary>
		/// Checks if a vertex is mapped and categorized as an edge vertex.
		/// </summary>
		public bool IsEdge(VoronoiPoint v)
		{
			return GetVertexType(v) == VertexType.Edge;
		}

		/// <summary>
		/// Checks if a vertex is mapped and categorized as an interior vertex.
		/// </summary>
		public bool IsInterior(VoronoiPoint v)
		{
			return GetVertexType(v) == VertexType.Interior;
		}

		/// <summary>
		/// Checks if the given VoronoiPlane vertex ID is within the bounds of the internal mapping array.
		/// </summary>
		private bool IsInRange(int id)
		{
			return id >= 0 && id < Vertices.Count;
		}

		/// <summary>
		/// Determines if a vertex lies on the boundary of the VoronoiPlane structure (e.g., incident to an unbounded face or an incomplete edge loop).
		/// </summary>
		private static bool IsBoundaryVertex(VoronoiPoint v)
		{
			return v.BorderLocation != PointBorderLocation.NotOnBorder;
		}

		/// <summary>
		/// Determines the vertex type (Interior or Edge) based on the number of valid neighbors within the extents.
		/// </summary>
		private static VertexType GetVertexType(VoronoiPoint v)
		{
			return IsBoundaryVertex(v) ? VertexType.Edge : VertexType.Interior;
		}
	}
}