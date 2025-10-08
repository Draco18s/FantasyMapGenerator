using OLearyMapGen.math;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OLearyMapGen
{
	public struct MapChunk
	{
		public NodeMap<double> heightMap;
		public NodeMap<double> erosionFillMap;
		public NodeMap<double> tempMap;
		public NodeMap<double> waterMap;
		public NodeMap<int> biomeMap;
		public List<int> riverVertices;
	}
}
