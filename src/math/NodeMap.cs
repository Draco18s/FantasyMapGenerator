using SharpVoronoiLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace OLearyMapGen.math
{
	public class NodeMap<T> where T : IComparable<T>, IAdditionOperators<T, T, T>, ISubtractionOperators<T,T,T>, IDivisionOperators<T,T,T>, IIncrementOperators<T>
	{
		private readonly List<T> _nodes;
		private readonly Dictionary<VoronoiPoint, int> _vertexMap;
		private readonly Dictionary<int, int[]> _neighborMap;
		private readonly VertexMap map;
		private static readonly T VALUE_TWO = default;
		private static readonly T VALUE_ZERO = default;

		static NodeMap()
		{
			VALUE_TWO++;
			VALUE_TWO++;
		}

		public NodeMap(VertexMap plane, T fillVal, Dictionary<int, int[]> neighbors=null)
		{
			List<VoronoiPoint> verts = plane.Vertices;
			_nodes = new List<T>(verts.Select(_ => fillVal));
			_vertexMap = verts.ToDictionary(v => v, v => verts.IndexOf(v));
			map = plane;

			_neighborMap = neighbors ?? verts.ToDictionary(GetNodeIndex, v => plane.GetNeighbourIndices(v).Select(GetNodeIndex).ToArray());
		}

		public Dictionary<int, int[]> GetNeighborMap()
		{
			return _neighborMap;
		}

		public VertexMap GetVertexMap()
		{
			return map;
		}

		public void Fill(T val)
		{
			for (int i = 0; i < _nodes.Count; i++)
			{
				_nodes[i] = val;
			}
		}

		public int Size()
		{
			return _nodes.Count();
		}

		public void Set(int idx, T val)
		{
			_nodes[idx] = val;
		}

		public T Get(int idx)
		{
			return _nodes[idx];
		}

		public VoronoiPoint GetVertex(int idx)
		{
			return map.Vertices[idx];
		}

		public T Min()
		{
			return _nodes.Min();
		}

		public T Max()
		{
			return _nodes.Max();
		}

		public int GetNodeIndex(VoronoiPoint p)
		{
			return _vertexMap[p];
		}

		public bool IsEdge(VoronoiPoint p)
		{
			return map.IsEdge(p);
		}

		public bool IsInterior(VoronoiPoint p)
		{
			return map.IsInterior(p);
		}

		public void Normalize()
		{
			T min = _nodes[0];
			T max = _nodes[0];
			
			for (int i = 0; i < Size(); i++)
			{
				if (min.CompareTo(_nodes[i]) > 0)
				{
					min = _nodes[i];
				}
				if (max.CompareTo(_nodes[i]) < 0)
				{
					max = _nodes[i];
				}
			}

			for (int i = 0; i < Size(); i++)
			{
				T val = _nodes[i];
				T normalized = (val - min) / (max - min);
				Set(i, normalized);
			}
		}

		/// <summary>
		/// Modifies all values by +offset
		/// </summary>
		/// <param name="offset">Value to add</param>
		public void Adjust(T offset)
		{
			for (int i = 0; i < Size(); i++)
			{
				T newval = _nodes[i] + offset;
				Set(i, newval);
			}
		}

		public void SetLevelToMedian()
		{
			List<T> values = new List<T>(Size());
			for (int i = 0; i < Size(); i++)
			{
				values.Add(Get(i));
			}
			values.Sort();
			int mididx = Size() / 2;
			T median;
			if (mididx % 2 == 0)
			{
				median = (values[mididx - 1] + values[mididx]) / VALUE_TWO;
			}
			else
			{
				median = values[mididx];
			}

			Adjust(VALUE_ZERO - median);
		}

		public int[] GetNeighbors(int i)
		{
			return _neighborMap[i];
		}
	}
}
