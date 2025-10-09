using OLearyMapGen.math;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OLearyMapGen
{
	public struct MapChunk
	{
		/// <summary>
		/// Chunk coordinates
		/// </summary>
		public Point position;
		/// <summary>
		/// Terrain height data
		/// </summary>
		public NodeMap<double> heightMap;
		/// <summary>
		/// Flow map ancillary data; used to draw lakes
		/// </summary>
		public NodeMap<double> erosionFillMap;
		/// <summary>
		/// Terrain temperature data
		/// </summary>
		public NodeMap<double> tempMap;
		/// <summary>
		/// Terrain rainfall data
		/// </summary>
		public NodeMap<double> waterMap;
		/// <summary>
		/// Terrain biome data
		/// </summary>
		public NodeMap<int> biomeMap;
		/// <summary>
		/// Computed rivers.<br/>
		/// Well-ordered by vertex index in downhill flow direction, negative values indicate the end of a river segment (which may empty into another river earlier in the list or into the ocean).
		/// </summary>
		public List<int> riverVertices;
		/// <summary>
		/// Valuation of sites for placing cities and other habitation sites
		/// </summary>
		public NodeMap<double> cityPlacementScores;
	}
}
