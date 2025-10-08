using OLearyMapGen.math;
using SharpVoronoiLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using static System.Formats.Asn1.AsnWriter;

namespace OLearyMapGen
{
	public class WorldMapGenerator
	{
		public struct GenParams
		{
			public long seed;
			public Extents2d chunkExtents;
			public double temp_bias;
			public double wet_bias;
			public double sea_level;
			public double global_modifier;
			public double resolution;
			public double fluxCapPercentile;
			public double erosionRiverFactor;
			public double erosionCreepFactor;
			public double maxErosionRate;
			public double ersionStrength;
			public double riverFluxThreshold;
			public double lakeFillThreshold;
		}

		public enum BiomeDef
		{
			Ocean, Grass, Desert, Savanna,
			Rainforest, Forest, Temperate, Tundra,
			Taiga, Mountain, SnowIce, River, Lake
		}

		private NodeMap<double> _heightMap;
		private NodeMap<double> _tempMap;
		private NodeMap<double> _waterMap;
		private NodeMap<int> _biomeMap;
		private FastNoiseLite _noise;
		private GenParams _config;

		public WorldMapGenerator(GenParams config)
		{
			_config = config;
			VoronoiPlane plane = new VoronoiPlane(-_config.resolution/2, -_config.resolution/2, _config.chunkExtents.Width + _config.resolution/2, _config.chunkExtents.Height + _config.resolution/2);

			UniformPoissonDiskSampler.SetSeed((uint)_config.seed);
			IEnumerable<VoronoiSite> pts = UniformPoissonDiskSampler.SampleRectangle(new Vector2(0, 0), new Vector2((float)_config.chunkExtents.Width, (float)_config.chunkExtents.Height), (float)_config.resolution, 512)
				.Select(p => new VoronoiSite(p.X, p.Y));
			plane.SetSites(pts.ToList());
			plane.Tessellate();
			plane.Relax(5);
			Console.WriteLine($"Tessellation complete {Program.timer.Elapsed}");
			VertexMap _vm = new VertexMap(plane, _config.chunkExtents);
			_heightMap = new NodeMap<double>(_vm, 0.0);
			Dictionary<int, int[]> neighborMap = _heightMap.GetNeighborMap();
			_tempMap = new NodeMap<double>(_vm, 0.0, neighborMap);
			_waterMap = new NodeMap<double>(_vm, 0.0, neighborMap);
			_biomeMap = new NodeMap<int>(_vm, 0, neighborMap);
			_noise = new FastNoiseLite(_config.seed);
			_noise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
		}

		public MapChunk GenerateChunk(int x, int y)
		{
			ElevationPass(x, y);
			Console.WriteLine($"Elevation Pass Complete {Program.timer.Elapsed}");
			TemperaturePass(x, y);
			Console.WriteLine($"Temperature Pass Complete {Program.timer.Elapsed}");
			MoisturePass(x, y);
			Console.WriteLine($"Moisture Pass Complete {Program.timer.Elapsed}");
			(NodeMap<double> ersionDelta, List<int> riverVertices) = ErosionPass(_config.ersionStrength);
			Console.WriteLine($"Erosion Pass Complete {Program.timer.Elapsed}");
			_heightMap = CleanupPass(_heightMap, _config.sea_level, 3);
			ersionDelta = CleanupPass(ersionDelta, 0, 3);
			BiomeAssignmentPass(ersionDelta, riverVertices);
			Console.WriteLine($"Biome Assignment Pass Complete {Program.timer.Elapsed}");

			return new MapChunk()
			{
				heightMap = _heightMap,
				erosionFillMap = ersionDelta,
				tempMap = _tempMap,
				waterMap = _waterMap,
				biomeMap = _biomeMap,
				riverVertices = riverVertices
			};
		}

		private NodeMap<double> CleanupPass(NodeMap<double> heightMap, double level, int iterations = 1)
		{
			Dictionary<int, int[]> neighbors = heightMap.GetNeighborMap();
			NodeMap<double> newh = null;

			for (; iterations-- > 0;)
			{
				newh = new NodeMap<double>(heightMap.GetVertexMap(), 0, neighbors);
				// First pass: Decrease height values
				int changed = 0;
				int belowSea = 0;
				int unchanged = 0;
				for (int i = 0; i < heightMap.Size(); i++)
				{
					double orig = heightMap.Get(i);
					newh.Set(i, orig);
					int[] nbs = neighbors[i];

					if (orig <= level || nbs.Length < 3)
					{
						belowSea++;
						continue;
					}

					double[] hbh = new double[nbs.Length];
					int count = 0;
					double best = heightMap.Min();
					for (int j = 0; j < nbs.Length; j++)
					{
						double nh = heightMap.Get(nbs[j]);
						hbh[j] = nh;
						if (nh > level)
							count++;
						if (nh > best)
							best = nh;
					}

					if (count > 1)
					{
						unchanged++;
						continue;
					}

					newh.Set(i, (orig - level) / 2 + level);
					changed += 1;
				}

				heightMap = newh;

				// Second pass: Increase height values
				changed = 0;
				belowSea = 0;
				unchanged = 0;
				newh = new NodeMap<double>(heightMap.GetVertexMap(), 0, neighbors);
				for (int i = 0; i < heightMap.Size(); i++)
				{
					double orig = heightMap.Get(i);
					newh.Set(i, orig);
					int[] nbs = neighbors[i];
					if (orig > level || nbs.Length < 3)
					{
						belowSea++;
						continue;
					}

					int count = 0;
					double best = heightMap.Max();
					for (int j = 0; j < nbs.Length; j++)
					{
						double nh = heightMap.Get(nbs[j]);
						if (nh <= level)
							count++;
						if (nh < best)
							best = nh;
					}

					if (count < nbs.Length)
					{
						unchanged++;
						continue;
					}

					newh.Set(i, (orig - level) / 2 + level);
					changed += 1;
				}

				heightMap = newh;
			}

			return heightMap;
		}

		private (NodeMap<double> ersionDelta, List<int> riverVertices) ErosionPass(double amount)
		{
			NodeMap<double> filledMap = FillDepressions(_heightMap);
			(NodeMap<double> erosionMap, List<int> riverVertices) results = CalculateErosionMap(filledMap);

			NodeMap<double> ersionDelta = new NodeMap<double>(_heightMap.GetVertexMap(), 0.0, _heightMap.GetNeighborMap());

			for (int i = 0; i < _heightMap.Size(); i++)
			{
				ersionDelta.Set(i, filledMap.Get(i) - _heightMap.Get(i));

				double currlevel = _heightMap.Get(i);
				double newlevel = currlevel - amount * results.erosionMap.Get(i);
				_heightMap.Set(i, newlevel);
			}

			return (ersionDelta, results.riverVertices);
		}

		private (NodeMap<double> erosionMap, List<int> riverVertices) CalculateErosionMap(NodeMap<double> heightMap)
		{
			NodeMap<double> erosionMap = new NodeMap<double>(heightMap.GetVertexMap(), 0.0, heightMap.GetNeighborMap());

			NodeMap<int> flowMap = CalculateFlowMap(heightMap);
			Console.WriteLine($"Flow Map Calculation Complete {Program.timer.Elapsed}");
			NodeMap<double> fluxMap = CalculateFluxMap(heightMap, flowMap);
			Console.WriteLine($"Flux Map Calculation Complete {Program.timer.Elapsed}");
			NodeMap<double> slopeMap = CalculateSlopeMap(heightMap);
			Console.WriteLine($"Slope Map Calculation Complete {Program.timer.Elapsed}");

			for (int i = 0; i < erosionMap.Size(); i++)
			{
				double flux = fluxMap.Get(i);
				double slope = slopeMap.Get(i);
				double river = _config.erosionRiverFactor * Math.Sqrt(flux) * slope;
				double creep = _config.erosionCreepFactor * slope * slope;
				double erosion = Math.Min(river + creep, _config.maxErosionRate);
				erosionMap.Set(i, erosion);
			}
			erosionMap.Normalize();
			Console.WriteLine($"Erosion Complete {Program.timer.Elapsed}");

			SmoothCoastline();

			List<int> riverVertices = GetRiverVerticies(heightMap, fluxMap, flowMap).ToList();
			Console.WriteLine($"River Calculation Complete {Program.timer.Elapsed}");

			return (erosionMap, riverVertices);
		}

		private IEnumerable<int> GetRiverVerticies(NodeMap<double> heightMap, NodeMap<double> fluxMap, NodeMap<int> flowMap)
		{
			List<int> paths = new List<int>();
			List<int> pathVertices = new List<int>();
			HashSet<int> visited = new HashSet<int>();
			for (int i = 0; i < fluxMap.Size(); i++)
			{
				if (fluxMap.Get(i) < _config.riverFluxThreshold || IsCoastVertex(heightMap,i))
					continue;

				int next = flowMap.Get(i);
				pathVertices.Clear();
				//pathVertices.Add(i);
				while (next >= 0)
				{
					pathVertices.Add(next);
					if (!visited.Add(next))
					{
						break;
					}
					if (IsCoastVertex(heightMap, next))
						break;
					if (IsLandVertex(heightMap, next))
					{
						next = flowMap.Get(next);
						continue;
					}
					pathVertices.Clear();
					break;
				}

				if (pathVertices.Count > 1)
				{
					pathVertices.Insert(0, i);
					if(next >= 0 && flowMap.Get(next) >= 0)
						pathVertices.Add(flowMap.Get(next));
					pathVertices.Add(-i);
					paths.AddRange(pathVertices);
				}
			}

			return paths;//.Distinct();
		}

		private bool IsLandVertex(NodeMap<double> heightMap, int i)
		{
			VoronoiPoint v = heightMap.GetVertex(i);
			IEnumerable<VoronoiSite> incidentFaces = heightMap.GetVertexMap().Sites.Where(s => s.Points.Contains(v));
			return incidentFaces.Any(face => IsLandFace(heightMap, face));
		}

		private bool IsCoastVertex(NodeMap<double> heightMap, int i)
		{
			VoronoiPoint v = heightMap.GetVertex(i);
			IEnumerable<VoronoiSite> incidentFaces = heightMap.GetVertexMap().Sites.Where(s => s.Points.Contains(v));
			bool hasLand = false;
			bool hasSea = false;

			foreach(VoronoiSite face in incidentFaces)
			{
				if (IsLandFace(heightMap, face))
				{
					hasLand = true;
				}
				else
				{
					hasSea = true;
				}
				if(hasLand && hasSea) // may as well early-out
					return true;
			}

			return hasLand && hasSea;
		}

		private bool IsLandFace(NodeMap<double> heightMap, VoronoiSite face)
		{
			double avg = face.Points.Select(p => heightMap.Get(heightMap.GetNodeIndex(p))).Average();
			return avg > _config.sea_level;
		}

		private void SmoothCoastline()
		{
		}

		private NodeMap<double> CalculateSlopeMap(NodeMap<double> heightMap)
		{
			NodeMap<double> slopeMap = new NodeMap<double>(heightMap.GetVertexMap(), 0.0, heightMap.GetNeighborMap());
			for (int i = 0; i < slopeMap.Size(); i++)
			{
				slopeMap.Set(i, CalculateSlope(heightMap, i));
			}

			return slopeMap;
		}

		private double CalculateSlope(NodeMap<double> heightMap, int i)
		{
			if (!heightMap.IsInterior(heightMap.GetVertex(i)))
				return 0.0;
			CalculateVertexNormal(heightMap, i, out double nx, out double ny, out double nz);

			double slope = Math.Sqrt(nx * nx + ny * ny);

			return slope;
		}

		private void CalculateVertexNormal(NodeMap<double> heightMap, int i, out double x, out double y, out double z)
		{
			x = y = z = 0;
			int[] neighbors =heightMap.GetNeighbors(i);
			if (neighbors.Length != 3) return;
			VoronoiPoint p0 = heightMap.GetVertex(neighbors[0]);
			VoronoiPoint p1 = heightMap.GetVertex(neighbors[1]);
			VoronoiPoint p2 = heightMap.GetVertex(neighbors[2]);

			double v0x = p1.X - p0.X;
			double v0y = p1.Y - p0.Y;
			double v0z = heightMap.Get(neighbors[1]) - heightMap.Get(neighbors[0]);
			double v1x = p2.X - p0.X;
			double v1y = p2.Y - p0.Y;
			double v1z = heightMap.Get(neighbors[2]) - heightMap.Get(neighbors[0]);

			double vnx = v0y * v1z - v0z * v1y;
			double vny = v0z * v1x - v0x * v1z;
			double vnz = v0x * v1y - v0y * v1x;
			double invlen = 1.0 / Math.Sqrt(vnx * vnx + vny * vny + vnz * vnz);

			x = vnx * invlen;
			y = vny * invlen;
			z = vnz * invlen;
		}

		private NodeMap<double> CalculateFluxMap(NodeMap<double> heightMap, NodeMap<int> flowMap)
		{
			NodeMap<double> fluxMap = new NodeMap<double>(heightMap.GetVertexMap(), -1, heightMap.GetNeighborMap());

			for (int i = 0; i < flowMap.Size(); i++)
			{
				int next = i;
				while (next != -1)
				{
					fluxMap.Set(next, fluxMap.Get(next) + 1);
					next = flowMap.Get(next);
				}
			}

			double maxFlux = CalculateFluxCap(fluxMap);
			for (int i = 0; i < fluxMap.Size(); i++)
			{
				double f = fluxMap.Get(i);
				f = Math.Min(maxFlux, f);
				f /= maxFlux;
				fluxMap.Set(i, f);
			}
			return fluxMap;
		}

		private double CalculateFluxCap(NodeMap<double> fluxMap)
		{
			double max = fluxMap.Max();
			int size = fluxMap.Size();
			int nbins = 1000;
			int[] bins = new int[nbins];
			double step = max / nbins;
			double invstep = 1.0 / step;
			for (int i = 0; i < size; i++)
			{
				double f = fluxMap.Get(i);
				int binidx = (int)Math.Floor(f * invstep);
				if (binidx >= nbins)
				{
					binidx = nbins - 1;
				}
				bins[binidx]++;
			}
			double acc = 0.0;
			double maxflux = 0.0;

			for (int i = 0; i < nbins; i++)
			{
				double pct = bins[i] / (double)size;
				acc += pct;
				if (acc > _config.fluxCapPercentile)
				{
					maxflux = (i + 1) * step;
					break;
				}
			}

			return maxflux;
		}

		/// <summary>
		/// Returns the index where the CurrentIndex flows to
		/// </summary>
		/// <param name="heightMap"></param>
		/// <returns></returns>
		private NodeMap<int> CalculateFlowMap(NodeMap<double> heightMap)
		{
			NodeMap<int> flowMap = new NodeMap<int>(heightMap.GetVertexMap(), -1, heightMap.GetNeighborMap());

			for (int i = 0; i < flowMap.Size(); i++)
			{
				double minHeight = heightMap.Get(i);
				int minVert = -1;
				var neighbors = heightMap.GetNeighbors(i);
				foreach (int n in neighbors)
				{
					if (heightMap.Get(n) < minHeight)
					{
						minHeight = heightMap.Get(n);
						minVert = n;
					}
				}
				if(minVert >= 0)
					flowMap.Set(i, minVert);
			}

			return flowMap;
		}

		private NodeMap<double> FillDepressions(NodeMap<double> heightMap)
		{
			NodeMap<double> finalMap = new NodeMap<double>(heightMap.GetVertexMap(), heightMap.Max() + 1, heightMap.GetNeighborMap());
			for (int i = 0; i < finalMap.Size(); i++)
			{
				if (heightMap.IsEdge(heightMap.GetVertex(i)))
				{
					finalMap.Set(i, heightMap.Get(i));
				}
			}

			const double eps = 1e-4;
			bool changed = false;
			do
			{
				changed = false;
				for (int i = 0; i < finalMap.Size(); i++)
				{
					if (Math.Abs(heightMap.Get(i) - finalMap.Get(i)) < eps) continue;

					int[] neighbors = heightMap.GetNeighbors(i);
					foreach (int n in neighbors)
					{
						double nval = finalMap.Get(n) + eps;
						if (heightMap.Get(i) > nval)
						{
							finalMap.Set(i, heightMap.Get(i));
							changed = true;
						}
						else if (finalMap.Get(i) > nval && finalMap.Get(i) > heightMap.Get(i))
						{
							finalMap.Set(i, nval);
							changed = true;
						}
					}
				}
			} while (changed);
			return finalMap;
		}

		private void BiomeAssignmentPass(NodeMap<double> ersionDelta, List<int> riversVerts)
		{
			BiomeDef[,] biome_table = new BiomeDef[,]
			{                                                                                         //       +---> increasing temperature
				{ BiomeDef.Tundra,  BiomeDef.Desert,    BiomeDef.Desert,        BiomeDef.Desert },    //       |
				{ BiomeDef.Tundra,  BiomeDef.Grass,     BiomeDef.Grass,         BiomeDef.Savanna },   //       |
				{ BiomeDef.Taiga,   BiomeDef.Temperate, BiomeDef.Forest,        BiomeDef.Savanna },   //       V
				{ BiomeDef.Taiga,   BiomeDef.Temperate, BiomeDef.Rainforest,    BiomeDef.Rainforest } // increasing wetness
			};
			double[] bins = [0.25, 0.5, 0.75];

			double mountain_level = _config.sea_level + 0.45;
			double snow_level = mountain_level + 0.15;

			for (int i = 0; i < _biomeMap.Size(); i++)
			{
				VoronoiPoint v = _biomeMap.GetVertex(i);
				double temp = _tempMap.Get(i);
				double wet = _waterMap.Get(i);
				double alt = _heightMap.Get(i);
				double lakefill = ersionDelta.Get(i);

				_biomeMap.Set(i, (int)biome_table[Digitize(wet, bins), Digitize(temp, bins)]);

				if(alt > mountain_level)
					_biomeMap.Set(i, (int)BiomeDef.Mountain);
				if(alt > snow_level || temp < 0.15)
					_biomeMap.Set(i, (int)BiomeDef.SnowIce);
				if (alt < _config.sea_level)
					_biomeMap.Set(i, (int)BiomeDef.Ocean);
				//if (riversVerts.Contains(i))
				//	_biomeMap.Set(i, (int)BiomeDef.River);
				if (alt > _config.sea_level + 0.01 && lakefill > _config.lakeFillThreshold && (2 * wet + temp / 2 > 0.7))
					_biomeMap.Set(i, _biomeMap.Get(i) + (int)BiomeDef.Lake);
			}
		}

		private static int Digitize<T>(T input, T[] source) where T : IComparable<T>
		{
			for (int index = 0; index < source.Length; index++)
				if (input.CompareTo(source[index]) < 0)
				{
					return index;
				}
			return source.Length;
		}

		private void MoisturePass(int x, int y)
		{
			for (int i = 0; i < _waterMap.Size(); i++)
			{
				VoronoiPoint v = _waterMap.GetVertex(i);
				double xx = v.X + x * _config.chunkExtents.Width;
				double yy = v.Y + y * _config.chunkExtents.Height;
				_waterMap.Set(i, MoistureAt(xx, yy));
			}
		}

		private double MoistureAt(double x, double y)
		{
			double moist_scale = 1024.0 * _config.global_modifier;
			double moist_noise = FbmNoise(x, y, moist_scale, 6, 0.5, 2.2, 10);
			double normalization_factor = 1.3;
			double moisture_map = Math.Clamp((moist_noise / normalization_factor + 1.0) / 2.0, 0, 1);
			moisture_map += _config.wet_bias;

			return Math.Clamp(moisture_map, 0, 1);
		}

		private void TemperaturePass(int x, int y)
		{
			for (int i = 0; i < _tempMap.Size(); i++)
			{
				VoronoiPoint v = _tempMap.GetVertex(i);
				double xx = v.X + x * _config.chunkExtents.Width;
				double yy = v.Y + y * _config.chunkExtents.Height;
				_tempMap.Set(i, TemperatureAt(xx, yy, v));
			}
		}

		private double TemperatureAt(double x, double y, VoronoiPoint v) 
		{
			double temp_scale = 1024.0 * _config.global_modifier;
			double temp_noise = FbmNoise(x, y, temp_scale, 5, 0.5, 2.1, 20);
			double normalization_factor = 1.2;
			double temp_map = Math.Clamp((temp_noise / normalization_factor + 1.0) / 2.0, 0, 1);
			temp_map += _config.temp_bias;

			double height_above_sea = _heightMap.Get(_heightMap.GetNodeIndex(v)) - _config.sea_level;
			double max_height_above_sea = 1.0 - _config.sea_level;
			if (max_height_above_sea > 0)
				height_above_sea = height_above_sea / max_height_above_sea;
			else
				height_above_sea = 0;

			double MAX_TEMP_DROP = 0.15;
			double altitude_modifier = Math.Pow(height_above_sea, 1.5) * MAX_TEMP_DROP;

			return Math.Clamp(temp_map - altitude_modifier, 0, 1);
		}

		private void ElevationPass(double x, double y)
		{
			for (int i = 0; i < _heightMap.Size(); i++)
			{
				VoronoiPoint v = _heightMap.GetVertex(i);
				double xx = v.X + x * _config.chunkExtents.Width;
				double yy = v.Y + y * _config.chunkExtents.Height;
				_heightMap.Set(i, ElevationAt(xx,yy));
			}
			Console.WriteLine($"Heightmap ranges from {_heightMap.Min()} to {_heightMap.Max()}");
			//_heightMap.Adjust(-_heightMap.Min());
			//_heightMap.SetLevelToMedian();
			//_heightMap.Adjust(_config.sea_level);
		}

		private double ElevationAt(double x, double y)
		{
			if (Math.Abs(x - 9) < 6 && Math.Abs(y - 65) < 6)
			{
				;
			}
			double warp_scale_large = 2048.0 * _config.global_modifier;
			double warp_strength_large = warp_scale_large * 0.1;

			double warp_x_l = FbmNoise(x, y, warp_scale_large, 3, 0.5, 2.0, 1) * warp_strength_large;
			double warp_y_l = FbmNoise(x, y, warp_scale_large, 3, 0.5, 2.0, 2) * warp_strength_large;

			double warp_scale_medium = 512.0 * _config.global_modifier;
			double warp_strength_medium = warp_scale_medium * 0.5;

			double warp_x_m = FbmNoise(x, y, warp_scale_medium, 5, 0.5, 2.0, 3) * warp_strength_medium;
			double warp_y_m = FbmNoise(x, y, warp_scale_medium, 5, 0.5, 2.0, 4) * warp_strength_medium;

			double warped_x = x + warp_x_l + warp_x_m;
			double warped_y = y + warp_y_l + warp_y_m;

			double base_scale = 2048.0 * _config.global_modifier;
			double base_noise = FbmNoise(x, y, base_scale, 6, 0.5, 2.0, 0);

			double detail_scale = 512.0 * _config.global_modifier;
			double detail_noise = FractalNoise(warped_x, warped_y, detail_scale, 8, 0.5, 2.1, 10);
			double normalization_factor_detail = 1.1;
			detail_noise = Math.Clamp((detail_noise / normalization_factor_detail + 1.0) / 2.0, 0, 1);

			double mountain_scale = 512.0 * _config.global_modifier;
			double mountain_noise = RidgedFbmNoise(x, y, mountain_scale, 6, 0.45, 2.0, 20);

			double mask_scale = 1536.0 * _config.global_modifier;
			int mask_x = (int)Math.Round(x + warp_x_l);
			int mask_y = (int)Math.Round(y + warp_y_l);
			double mask_noise = RidgedFbmNoise(mask_x, mask_y, mask_scale, 4, 0.5, 2.0, 21);

			double mask_norm_factor = 1.1;
			double mountain_mask = Math.Pow(Math.Clamp((mask_noise / mask_norm_factor + 1.0) / 2.0 - 0.5, 0, 0.5)+0.5,3);

			double detail_contribution = detail_noise * 0.3;
			double elevation = base_noise + detail_contribution;
			double mountain_contribution = mountain_noise * mountain_mask;// * 0.5;
			elevation += mountain_contribution;

			elevation = (Math.Tan((elevation * 2.4) - 1.2 + 0.35) + 0.35) / 3.75;

			return Math.Clamp(elevation,0,1);
		}

		private double FractalNoise(double x, double y, double scale, int octaves, double persistence, double lacunarity, int seed_offset = 0)
		{
			_noise.SetFractalType(FastNoiseLite.FractalType.None);
			_noise.SetSeed(_config.seed + seed_offset);
			_noise.SetFractalLacunarity((float)lacunarity);
			_noise.SetFractalOctaves(octaves);
			_noise.SetFractalPersistence((float)persistence);
			_noise.SetFrequency((float)(1.0 / scale));

			return _noise.GetNoise((float)x, (float)y);
		}

		private double FbmNoise(double x, double y, double scale, int octaves, double persistence, double lacunarity, int seed_offset = 0)
		{
			_noise.SetFractalType(FastNoiseLite.FractalType.FBm);
			_noise.SetSeed(_config.seed + seed_offset);
			_noise.SetFractalLacunarity((float)lacunarity);
			_noise.SetFractalOctaves(octaves);
			_noise.SetFractalPersistence((float)persistence);
			_noise.SetFrequency((float)(1.0 / scale));

			return _noise.GetNoise((float)x, (float)y);
		}

		private double RidgedFbmNoise(double x, double y, double scale, int octaves, double persistence, double lacunarity, int seed_offset = 0)
		{
			_noise.SetFractalType(FastNoiseLite.FractalType.Ridged);
			_noise.SetSeed(_config.seed + seed_offset);
			_noise.SetFractalLacunarity((float)lacunarity);
			_noise.SetFractalOctaves(octaves);
			_noise.SetFractalPersistence((float)persistence);
			_noise.SetFrequency((float)(1.0 / scale));

			return _noise.GetNoise((float)x, (float)y);
		}

		public IEnumerable<(Vector2, float)> GetHeights()
		{
			List<(Vector2,float)> list = new List<(Vector2, float)>();
			for (int i = 0; i < _heightMap.Size(); i++)
			{
				double v = _heightMap.Get(i);
				VoronoiPoint p = _heightMap.GetVertex(i);
				list.Add((new Vector2((float)p.X, (float)p.Y), (float)v));
			}
			return list;
		}
	}
}
