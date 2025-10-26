using OLearyMapGen.math;
using SharpVoronoiLib;
using System.Drawing;
using System.Linq;
using System.Numerics;

namespace OLearyMapGen
{
	public class WorldMapGenerator
	{
		private NodeMap<double> _heightMap;
		private NodeMap<double> _tempMap;
		private NodeMap<double> _waterMap;
		private NodeMap<double> _cityScoreMap;
		private NodeMap<int> _biomeMap;
		private FastNoiseLite _noise;
		private GenParams _config;
		private Extents2d _modifiedExtents;

		private double mountain_level = 0;
		private double snow_level = 0;

		public WorldMapGenerator(GenParams config)
		{
			_config = config;
			int extentsMult = (int)(_config.chunkExtents.Width / _config.resolution);
			int extentsInset = 2;
			int randomMulti = 1;
			//int fixedMulti = 0;
			mountain_level = _config.sea_level + 0.45;
			snow_level = mountain_level + 0.15 + (_config.temp_bias / 5);

			_modifiedExtents = new Extents2d(
				-_config.resolution * (extentsMult - extentsInset),
				-_config.resolution * (extentsMult - extentsInset),
				_config.chunkExtents.Width + _config.resolution * (extentsMult - extentsInset),
				_config.chunkExtents.Height + _config.resolution * (extentsMult - extentsInset));

			VoronoiPlane plane = new VoronoiPlane(-_config.resolution * extentsMult, -_config.resolution * extentsMult, _config.chunkExtents.Width + _config.resolution * extentsMult, _config.chunkExtents.Height + config.resolution * extentsMult);
			UniformPoissonDiskSampler.SetSeed((uint)_config.seed);
			List<Vector2> pts = UniformPoissonDiskSampler.SampleRectangle(new Vector2(-(float)_config.resolution * randomMulti, -(float)_config.resolution * randomMulti), new Vector2((float)(_config.chunkExtents.Width + _config.resolution * randomMulti), (float)(_config.chunkExtents.Height + _config.resolution * randomMulti)), (float)_config.resolution, 512).ToList();
			
			IEnumerable<Vector2> newPts = pts;

			if (_config.encourageTileability)
			{
				newPts = newPts.Concat(pts.Select(p => new Vector2(p.X - 256, p.Y)))
					.Concat(pts.Select(p => new Vector2(p.X - 256, p.Y - 256)))
					.Concat(pts.Select(p => new Vector2(p.X, p.Y - 256)))
					.Concat(pts.Select(p => new Vector2(p.X + 256, p.Y)))
					.Concat(pts.Select(p => new Vector2(p.X + 256, p.Y + 256)))
					.Concat(pts.Select(p => new Vector2(p.X, p.Y + 256)))
					.Concat(pts.Select(p => new Vector2(p.X + 256, p.Y - 256)))
					.Concat(pts.Select(p => new Vector2(p.X - 256, p.Y + 256)))
					.Where(p => Math.Abs(p.X - _config.chunkExtents.Width / 2) < (_config.chunkExtents.Width * 3 / 2) 
					            && Math.Abs(p.Y - _config.chunkExtents.Height / 2) < (_config.chunkExtents.Height * 3 / 2));
			}

			plane.SetSites(newPts.Select(p => new VoronoiSite(p.X, p.Y))
				.ToList());
			plane.Tessellate();
			plane.Relax(2);

			plane.MergeSites((a, b) => GetDistance(a, b) < _config.resolution / 4 ? VoronoiSiteMergeDecision.MergeIntoSite1 : VoronoiSiteMergeDecision.DontMerge);

			Console.WriteLine($"Tessellation complete {Program.timer.Elapsed}");
			VertexMap _vm = new VertexMap(plane, _config.chunkExtents);
			_heightMap = new NodeMap<double>(_vm, 0.0);
			Dictionary<int, int[]> neighborMap = _heightMap.GetNeighborMap();
			_tempMap = new NodeMap<double>(_vm, 0.0, neighborMap);
			_waterMap = new NodeMap<double>(_vm, 0.0, neighborMap);
			_biomeMap = new NodeMap<int>(_vm, 0, neighborMap);
			_cityScoreMap = new NodeMap<double>(_vm, 0.0, neighborMap);
			_noise = new FastNoiseLite(_config.seed);
			_noise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
		}

		public MapChunk GenerateChunk(int x, int y)
		{
			ElevationPass(x, y, _config.elevationRamping);
			Console.WriteLine($"Elevation Pass Complete {Program.timer.Elapsed}");
			TemperaturePass(x, y);
			Console.WriteLine($"Temperature Pass Complete {Program.timer.Elapsed}");
			MoisturePass(x, y, _config.elevationRamping);
			Console.WriteLine($"Moisture Pass Complete {Program.timer.Elapsed}");
			(NodeMap<double> ersionDelta, List<int> riverVertices) = ErosionPass(_config.ersionStrength, _config.fluxCapPercentile, _config.riverFluxThreshold, 
				_config.erosionRiverFactor, _config.erosionCreepFactor, _config.maxErosionRate, _config.resolution/2.0, _config.lakeFillThreshold, _config.sea_level);
			Console.WriteLine($"Erosion Pass Complete {Program.timer.Elapsed}");
			_heightMap = CleanupPass(_heightMap, _config.sea_level, 3);
			ersionDelta = CleanupPass(ersionDelta, 0, 3);
			BiomeAssignmentPass(ersionDelta);
			Console.WriteLine($"Biome Assignment Pass Complete {Program.timer.Elapsed}");
			_cityScoreMap = ComputeCityScores(_config, x, y, _heightMap, _tempMap, _biomeMap);
			Console.WriteLine($"City Placement Pass Complete {Program.timer.Elapsed}");

			return new MapChunk()
			{
				genParams = _config,
				position = new Point(x, y),
				heightMap = _heightMap,
				tempMap = _tempMap,
				waterMap = _waterMap,
				biomeMap = _biomeMap,
				erosionFillMap = ersionDelta,
				riverVertices = riverVertices,
				cityPlacementScores = _cityScoreMap
			};
		}

		/// <summary>
		/// Compute city location score map. Results will be cached and can be fetched with <href a="GetPotentialCityLocations">GetPotentialCityLocations</href>
		/// </summary>
		/// <param name="config">MapGen config parameters</param>
		/// <param name="chunkX">Where this chunk is in the world</param>
		/// <param name="chunkY">Where this chunk is in the world</param>
		/// <param name="heightMap">Height map</param>
		/// <param name="tempMap">Temperature map</param>
		/// <param name="biomeMap">Biome map</param>
		/// <param name="fluxScoreBonus">Desirability factor to place near rivers</param>
		/// <param name="citiesAndTowns">Dictionary of existing locations to their distance score multiplier.</param>
		/// <param name="minCityDistance">Expected city separation distance</param>
		/// <returns></returns>
		public static NodeMap<double> ComputeCityScores(GenParams config, int chunkX, int chunkY, NodeMap<double> heightMap, NodeMap<double> tempMap, NodeMap<int> biomeMap, double fluxScoreBonus=2.5, Dictionary<Point,double> citiesAndTowns = null, double minCityDistance = 4.0)
		{
			NodeMap<double> cityScoreMap = new NodeMap<double>(heightMap.GetVertexMap(), 0.0, heightMap.GetNeighborMap());
			cityScoreMap.Fill(0);
			NodeMap<int> flowMap = CalculateFlowMap(heightMap);
			NodeMap<double> fluxMap = CalculateFluxMap(heightMap, flowMap, config.fluxCapPercentile);
			fluxMap.Relax();

			const double neginf = -100;
			double _maxPenaltyDistance = minCityDistance * 2;
			Parallel.For(0, cityScoreMap.Size(), i =>
			{
				double score = IsCoastVertex(heightMap, i, config.sea_level) ? 0.05 : 0;
				if (!IsLandVertex(heightMap, i, config.sea_level) || biomeMap.Get(i) >= (int)BiomeDef.Lake)
				{
					score = neginf;
				}

				score += fluxScoreBonus * Math.Sqrt(fluxMap.Get(i)); // * (IsCoastVertex(heightMap, i, config.sea_level) ? 1 : 0.8);
				score -= 0.2 * heightMap.Get(i) * heightMap.Get(i);
				score -= 0.2 * Math.Abs(tempMap.Get(i) - 0.5);

				if (citiesAndTowns != null)
				{
					VoronoiPoint v = cityScoreMap.GetVertex(i);
					foreach (KeyValuePair<Point, double> t in citiesAndTowns)
					{
						//Vector2 l = new Vector2((float)(t.Key.X - chunkX * config.chunkExtents.Width), (float)(t.Key.Y + chunkY * config.chunkExtents.Height));
						double dist = GetDistance(v, t.Key);
						double distfactor = 1 - dist / _maxPenaltyDistance;
						score -= t.Value * distfactor * distfactor;
					}
				}

				cityScoreMap.Set(i, Math.Clamp(score, 0, 1));
			});
			cityScoreMap.Normalize();
			return cityScoreMap;
		}

		private NodeMap<double> CleanupPass(NodeMap<double> heightMap, double level, int iterations = 1)
		{
			Dictionary<int, int[]> neighbors = heightMap.GetNeighborMap();
			NodeMap<double> newh = null;

			while (iterations-->0)
			{
				newh = new NodeMap<double>(heightMap.GetVertexMap(), 0, neighbors);
				// First pass: Decrease height values
				Parallel.For(0, heightMap.Size(), i =>
				{
					double orig = heightMap.Get(i);
					newh.Set(i, orig);
					int[] nbs = neighbors[i];

					if (orig <= level || nbs.Length < 3)
					{
						return;
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
						return;
					}

					newh.Set(i, (orig - level) / 2 + level);
				});

				heightMap = newh;

				// Second pass: Increase height values
				newh = new NodeMap<double>(heightMap.GetVertexMap(), 0, neighbors);
				Parallel.For(0, heightMap.Size(), i =>
				{
					double orig = heightMap.Get(i);
					newh.Set(i, orig);
					int[] nbs = neighbors[i];
					if (orig > level || nbs.Length < 3)
					{
						return;
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

					if (count > 1)
					{
						return;
					}

					newh.Set(i, (orig - level) / 2 + level);
				});

				heightMap = newh;
			}

			return heightMap;
		}

		private (NodeMap<double> ersionDelta, List<int> riverVertices) ErosionPass(double amount, double fluxCapPercentile, double riverFluxThreshold, double erosionRiverFactor, double erosionCreepFactor, double maxErosionRate, double riverMergeDist, double lakeFillThreshold, double sea_level)
		{
			NodeMap<double> filledMap = FillDepressions(_heightMap);
			(NodeMap<double> erosionMap, List<int> riverVertices) results = CalculateErosionMap(filledMap, fluxCapPercentile, riverFluxThreshold, erosionRiverFactor, erosionCreepFactor, maxErosionRate, riverMergeDist, sea_level);

			NodeMap<double> ersionDelta = new NodeMap<double>(_heightMap.GetVertexMap(), 0.0, _heightMap.GetNeighborMap());

			Parallel.For(0, _heightMap.Size(), i =>
			{
				double h = Math.Max(_heightMap.Get(i), sea_level);
				double f = Math.Max(filledMap.Get(i), sea_level);

				double currlevel = _heightMap.Get(i);
				double newlevel = currlevel - amount * results.erosionMap.Get(i);
				_heightMap.Set(i, newlevel);
				if (currlevel > sea_level + lakeFillThreshold)
					ersionDelta.Set(i, f - h);
			});

			return (ersionDelta, results.riverVertices);
		}

		private static (NodeMap<double> erosionMap, List<int> riverVertices) CalculateErosionMap(NodeMap<double> heightMap, double fluxCapPercentile, double riverFluxThreshold, double erosionRiverFactor, double erosionCreepFactor, double maxErosionRate, double riverMergeDist, double sea_level)
		{
			NodeMap<double> erosionMap = new NodeMap<double>(heightMap.GetVertexMap(), 0.0, heightMap.GetNeighborMap());

			NodeMap<int> flowMap = CalculateFlowMap(heightMap);
			Console.WriteLine($"Flow Map Calculation Complete {Program.timer.Elapsed}");
			NodeMap<double> fluxMap = CalculateFluxMap(heightMap, flowMap, fluxCapPercentile);
			Console.WriteLine($"Flux Map Calculation Complete {Program.timer.Elapsed}");
			NodeMap<double> slopeMap = CalculateSlopeMap(heightMap);
			Console.WriteLine($"Slope Map Calculation Complete {Program.timer.Elapsed}");

			Parallel.For(0, erosionMap.Size(), i =>
			{
				double flux = fluxMap.Get(i);
				double slope = slopeMap.Get(i);
				double river = erosionRiverFactor * Math.Sqrt(flux) * slope;
				double creep = erosionCreepFactor * slope * slope;
				double erosion = Math.Min(river + creep, maxErosionRate);
				erosionMap.Set(i, erosion);
			});
			erosionMap.Normalize();
			Console.WriteLine($"Erosion Complete {Program.timer.Elapsed}");

			SmoothCoastline();

			IEnumerable<int> riverVertices = GetRiverVerticies(heightMap, fluxMap, flowMap, riverFluxThreshold, sea_level);
			riverVertices = CleanupRivers(riverVertices, heightMap, fluxMap, flowMap, riverMergeDist);
			Console.WriteLine($"River Calculation Complete {Program.timer.Elapsed}");

			return (erosionMap, riverVertices.ToList());
		}

		private static IEnumerable<int> CleanupRivers(IEnumerable<int> riverVertices, NodeMap<double> heightMap, NodeMap<double> fluxMap, NodeMap<int> flowMap, double mergeDist)
		{
			List<int> revisedRivers = new List<int>();
			List<int> singlePath = new List<int>();
			Dictionary<int, int[]> neighbormap = heightMap.GetNeighborMap();
			int prev = -1;
			bool shortSkip = false;
			foreach (int r in riverVertices)
			{
				if (r < 0)
				{
					revisedRivers.AddRange(singlePath);
					revisedRivers.Add(r);
					singlePath.Clear();
					shortSkip = false;
					continue;
				}

				if (shortSkip) continue;

				singlePath.Add(r);
				int next = flowMap.Get(r);
				foreach (int n in neighbormap[r])
				{
					if (n == next || n == prev || !revisedRivers.Contains(n) || revisedRivers.Contains(next)) continue;
					VoronoiPoint v1 = heightMap.GetVertex(r);
					VoronoiPoint v2 = heightMap.GetVertex(n);
					if (!(GetDistance(v1, v2) < mergeDist)) continue;
					singlePath.Add(n);
					shortSkip = true;
					break;
				}

				prev = r;
			}

			return revisedRivers;
		}

		private static IEnumerable<int> GetRiverVerticies(NodeMap<double> heightMap, NodeMap<double> fluxMap, NodeMap<int> flowMap, double riverFluxThreshold, double sea_level)
		{
			List<int> paths = new List<int>();
			List<int> pathVertices = new List<int>();
			HashSet<int> visited = new HashSet<int>();
			// not parallelizable
			for (int i = 0; i < fluxMap.Size(); i++)
			{
				if (fluxMap.Get(i) < riverFluxThreshold || IsCoastVertex(heightMap, i, sea_level))
					continue;

				int next = flowMap.Get(i);

				pathVertices.Clear();

				while (next >= 0)
				{
					pathVertices.Add(next);
					if (!visited.Add(next))
					{
						break;
					}
					if (IsCoastVertex(heightMap, next, sea_level))
						break;
					if (IsLandVertex(heightMap, next, sea_level))
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
					if (next >= 0 && flowMap.Get(next) >= 0 && visited.Add(next))
						pathVertices.Add(flowMap.Get(next));
					pathVertices.Add(-i);
					paths.AddRange(pathVertices);
				}
			}

			return paths;//.Distinct();
		}

		private static bool IsLandVertex(NodeMap<double> heightMap, int i, double sea_level)
		{
			VoronoiPoint v = heightMap.GetVertex(i);
			IEnumerable<VoronoiSite> incidentFaces = heightMap.GetVertexMap().Sites.Where(s => s.Points.Contains(v));
			return incidentFaces.Any(face => IsLandFace(heightMap, face, sea_level));
		}

		private static bool IsCoastVertex(NodeMap<double> heightMap, int i, double sea_level)
		{
			VoronoiPoint v = heightMap.GetVertex(i);

			IEnumerable<VoronoiSite> incidentFaces = heightMap.GetVertexMap().Sites.Where(s => s.Points.Contains(v));
			bool hasLand = false;
			bool hasSea = false;

			foreach(VoronoiSite face in incidentFaces)
			{
				if (IsLandFace(heightMap, face, sea_level))
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

			return false;
		}

		private static bool IsLandFace(NodeMap<double> heightMap, VoronoiSite face, double sea_level)
		{
			double avg = face.Points.Select(p => heightMap.Get(heightMap.GetNodeIndex(p))).Average();
			return avg > sea_level;
		}

		private static void SmoothCoastline()
		{
			// TODO
		}

		private static NodeMap<double> CalculateSlopeMap(NodeMap<double> heightMap)
		{
			NodeMap<double> slopeMap = new NodeMap<double>(heightMap.GetVertexMap(), 0.0, heightMap.GetNeighborMap());
			Parallel.For(0, slopeMap.Size(), i =>
			{
				slopeMap.Set(i, CalculateSlope(heightMap, i));
			});

			return slopeMap;
		}

		private static double CalculateSlope(NodeMap<double> heightMap, int i)
		{
			if (!heightMap.IsInterior(heightMap.GetVertex(i)))
				return 0.0;
			CalculateVertexNormal(heightMap, i, out double nx, out double ny, out double nz);

			double slope = Math.Sqrt(nx * nx + ny * ny);

			return slope;
		}

		private static void CalculateVertexNormal(NodeMap<double> heightMap, int i, out double x, out double y, out double z)
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

		private static NodeMap<double> CalculateFluxMap(NodeMap<double> heightMap, NodeMap<int> flowMap, double fluxCapPercentile)
		{
			NodeMap<double> fluxMap = new NodeMap<double>(heightMap.GetVertexMap(), -1, heightMap.GetNeighborMap());

			Parallel.For(0, flowMap.Size(), i =>
			{
				int next = i;
				while (next != -1)
				{
					fluxMap.Set(next, fluxMap.Get(next) + 1);
					next = flowMap.Get(next);
				}
			});

			double maxFlux = CalculateFluxCap(fluxMap, fluxCapPercentile);
			Parallel.For(0, fluxMap.Size(), i =>
			{
				double f = fluxMap.Get(i);
				f = Math.Min(maxFlux, f);
				f /= maxFlux;
				fluxMap.Set(i, f);
			});
			return fluxMap;
		}

		private static double CalculateFluxCap(NodeMap<double> fluxMap, double fluxCapPercentile)
		{
			double max = Math.Max(fluxMap.Max(), 0.01);
			int size = fluxMap.Size();
			int nbins = 1000;
			int[] bins = new int[nbins];
			double step = max / nbins;
			double invstep = 1.0 / step;
			// not parallelizable
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

			// not parallelizable
			for (int i = 0; i < nbins; i++)
			{
				double pct = bins[i] / (double)size;
				acc += pct;
				if (acc > fluxCapPercentile)
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
		private static NodeMap<int> CalculateFlowMap(NodeMap<double> heightMap)
		{
			NodeMap<int> flowMap = new NodeMap<int>(heightMap.GetVertexMap(), -1, heightMap.GetNeighborMap());

			Parallel.For(0, flowMap.Size(), i =>
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

				if (minVert >= 0)
				{
					flowMap.Set(i, minVert);
				}
			});

			return flowMap;
		}

		private static double GetDistance(VoronoiPoint v1, VoronoiPoint v2)
		{
			return Vector2.Distance(new Vector2((float)v1.X, (float)v1.Y), new Vector2((float)v2.X, (float)v2.Y));
		}

		private static double GetDistance(VoronoiPoint v1, Point v2)
		{
			return Vector2.Distance(new Vector2((float)v1.X, (float)v1.Y), new Vector2((float)v2.X, (float)v2.Y));
		}

		private static double GetDistance(VoronoiSite v1, VoronoiSite v2)
		{
			return Vector2.Distance(new Vector2((float)v1.X, (float)v1.Y), new Vector2((float)v2.X, (float)v2.Y));
		}

		private NodeMap<double> FillDepressions(NodeMap<double> heightMap)
		{
			const double eps = 1e-4;
			NodeMap<double> finalMap = new NodeMap<double>(heightMap.GetVertexMap(), heightMap.Max() + 1, heightMap.GetNeighborMap());
			Parallel.For(0, finalMap.Size(), i =>
			{
				double h = heightMap.Get(i);
				VoronoiPoint p = heightMap.GetVertex(i);
				if (p.X < _modifiedExtents.minX || p.Y < _modifiedExtents.minY || p.X > _modifiedExtents.maxX || p.Y > _modifiedExtents.maxY)
				{
					finalMap.Set(i, h);
				}

				if (h <= _config.sea_level)
				{
					finalMap.Set(i, h);
				}
			});

			bool changed = false;
			do
			{
				changed = false;
				Parallel.For(0, finalMap.Size(), i =>
				{
					if (Math.Abs(heightMap.Get(i) - finalMap.Get(i)) < eps) return;

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
				});
			} while (changed);
			return finalMap;
		}

		private void BiomeAssignmentPass(NodeMap<double> ersionDelta)
		{
			BiomeDef[,] biome_table = new BiomeDef[,]
			{																																																	//       +-------> increasing temperature
				{ BiomeDef.ColdDesert,  BiomeDef.ColdDesert,    BiomeDef.ColdDesert,    BiomeDef.Desert,        BiomeDef.Desert,         BiomeDef.Desert,      BiomeDef.Desert,          BiomeDef.Badlands },   //       |
				{ BiomeDef.ColdDesert,  BiomeDef.ColdDesert,    BiomeDef.ColdDesert,    BiomeDef.ColdDesert,    BiomeDef.Desert,         BiomeDef.Desert,      BiomeDef.Desert,          BiomeDef.Desert   },   //       |
				{ BiomeDef.Tundra,      BiomeDef.Plains,        BiomeDef.Plains,        BiomeDef.Plains,        BiomeDef.Savanna,        BiomeDef.Savanna,     BiomeDef.Desert,          BiomeDef.Desert   },   //       |
				{ BiomeDef.Tundra,      BiomeDef.Plains,        BiomeDef.Plains,        BiomeDef.Plains,        BiomeDef.Plains,         BiomeDef.Savanna,     BiomeDef.Savanna,         BiomeDef.Desert   },   //       |
				{ BiomeDef.Tundra,      BiomeDef.Taiga,         BiomeDef.SeasonForest,  BiomeDef.SeasonForest,  BiomeDef.SeasonForest,   BiomeDef.Savanna,     BiomeDef.Savanna,         BiomeDef.Desert   },   //       |
				{ BiomeDef.Taiga,       BiomeDef.Taiga,         BiomeDef.PineForest,    BiomeDef.SeasonForest,  BiomeDef.SeasonForest,   BiomeDef.Rainforest,  BiomeDef.TropRainforest,  BiomeDef.Beach    },   //       |
				{ BiomeDef.Taiga,       BiomeDef.PineForest,    BiomeDef.PineForest,    BiomeDef.PineForest,    BiomeDef.Rainforest,     BiomeDef.Rainforest,  BiomeDef.TropRainforest,  BiomeDef.Beach    },   //       V
				{ BiomeDef.RockShore,   BiomeDef.RockShore,     BiomeDef.Rainforest,    BiomeDef.Swamp,         BiomeDef.Swamp,          BiomeDef.Beach,       BiomeDef.Beach,           BiomeDef.Beach    }    // increasing wetness
			};
			double[] bins = [0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875];

			Parallel.For(0, _biomeMap.Size(), i =>
			{
				VoronoiPoint v = _biomeMap.GetVertex(i);
				double temp = _tempMap.Get(i);
				double wet = _waterMap.Get(i);
				double alt = _heightMap.Get(i);
				double lakefill = ersionDelta.Get(i) * wet * (1 - temp);

				_biomeMap.Set(i, (int)biome_table[Digitize(wet, bins), Digitize(temp, bins)]);
				if (temp < 0.1)
					_biomeMap.Set(i, (int)BiomeDef.IcePlains);
				if (alt > mountain_level)
					_biomeMap.Set(i, (int)BiomeDef.Mountain);
				if (alt > snow_level || temp < 0.15)
					_biomeMap.Set(i, (int)BiomeDef.Snow);
				if (alt < _config.sea_level)
					_biomeMap.Set(i, (int)BiomeDef.Ocean);
				if (alt > _config.sea_level + _config.lakeFillThreshold && lakefill > _config.lakeFillThreshold && (2 * wet + temp / 2 > 0.7))
					_biomeMap.Set(i, _biomeMap.Get(i) + (int)BiomeDef.Lake);
			});
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

		private double MoistureAt(double x, double y)
		{
			double moist_scale = 1024.0 * 4.0 * _config.global_modifier;
			double moist_noise = FbmNoise(x, y, moist_scale, 6, 0.5, 2.2, 10);
			double normalization_factor = 1.3;
			double moisture_map = Math.Clamp((moist_noise / normalization_factor + 1.0) / 2.0, 0, 1);
			moisture_map *= moisture_map * moisture_map;
			moisture_map += _config.wet_bias;

			return Math.Clamp(moisture_map * 2, 0, 1);
		}

		private void MoisturePass(int x, int y, ContinentalElevationRampFactors elevationRamping)
		{
			NodeMap<double> finalMap = new NodeMap<double>(_waterMap.GetVertexMap(), 0, _waterMap.GetNeighborMap());
			// not parallelizable
			for (int i = 0; i < _waterMap.Size(); i++)
			{
				VoronoiPoint v = _heightMap.GetVertex(i);
				double xx = v.X + x * _config.chunkExtents.Width;
				double yy = v.Y + y * _config.chunkExtents.Height;
				double moist = 0;
				double clouds1 = GetMoistureInAirToHere(xx, yy, elevationRamping);
				double clouds2 = GetMoistureInAirToHere(xx + _config.resolution * _config.global_modifier * 64, yy, elevationRamping);
				moist = 0.5 + (clouds1 - clouds2) / 2;

				_waterMap.Set(i, Math.Clamp(moist, 0, 1));
			}
		}

		private double GetMoistureInAirToHere(double xx, double yy, ContinentalElevationRampFactors elevationRamping)
		{
			double weatherDist = (_config.chunkExtents.Width / 4);
			double multi = 2 * _config.global_modifier;
			double moist = MoistureAt(xx,yy);
			// not parallelizable
			for (double ox = xx - weatherDist; ox < xx; ox += _config.resolution * _config.global_modifier)
			{
				double elev = ElevationAt(ox, yy, elevationRamping);
				double temp = TemperatureAt(ox, yy);
				if (elev < _config.sea_level)
				{
					moist += 0.075 * temp * multi;
				}
				else if (elev < _config.sea_level + 0.15)
				{
					moist -= 0.015 * (Math.Max(elev, _config.sea_level) + 0.15) * (1 - temp) * multi;
					moist += 0.005 * temp * multi;
				}
				else
				{
					moist -= 0.025 * Math.Max(elev, _config.sea_level) * (1 - temp) * multi;
				}

				moist = Math.Clamp(moist, 0.05, temp + 0.25);
			}

			return moist;
		}

		private void TemperaturePass(int x, int y)
		{
			Parallel.For(0, _tempMap.Size(), i =>
			{
				VoronoiPoint v = _tempMap.GetVertex(i);
				double xx = v.X + x * _config.chunkExtents.Width;
				double yy = v.Y + y * _config.chunkExtents.Height;
				_tempMap.Set(i, TemperatureAt(xx, yy, v));
			});
		}

		private double TemperatureAt(double x, double y)
		{
			double temp_scale = 1024.0 * 4.0 * _config.global_modifier;
			double temp_noise = FbmNoise(x, y, temp_scale, 5, 0.5, 2.1, 20);
			double normalization_factor = 1.2;
			double temp_map = Math.Clamp((temp_noise / normalization_factor + 1.0) / 2.0, 0, 1);
			temp_map *= temp_map * 7;
			temp_map += _config.temp_bias - 1.3;

			return Math.Clamp(temp_map, 0, 1);
		}

		private double TemperatureAt(double x, double y, VoronoiPoint v) 
		{
			double temp_map = TemperatureAt(x, y);
			double height_above_sea = _heightMap.Get(_heightMap.GetNodeIndex(v)) - _config.sea_level;
			double max_height_above_sea = 1.0 - _config.sea_level;
			if (max_height_above_sea > 0)
				height_above_sea /= max_height_above_sea;
			else
				height_above_sea = 0;

			double MAX_TEMP_DROP = 0.25;
			double altitude_modifier = Math.Pow(Math.Clamp(height_above_sea, 0, 1), 2.5) * MAX_TEMP_DROP;

			return Math.Clamp(temp_map - altitude_modifier, 0, 1);
		}

		private void ElevationPass(double x, double y, ContinentalElevationRampFactors elevationRamping)
		{
			// not parallelizable
			for (int i = 0; i < _heightMap.Size(); i++)
			{
				VoronoiPoint v = _heightMap.GetVertex(i);
				double xx = v.X + x * _config.chunkExtents.Width;
				double yy = v.Y + y * _config.chunkExtents.Height;
				_heightMap.Set(i, ElevationAt(xx,yy, elevationRamping));
			}
		}

		private double ElevationAt(double x, double y, ContinentalElevationRampFactors elevationRamping)
		{
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
			double mountain_contribution = mountain_noise * mountain_mask;
			elevation += mountain_contribution;
			elevation += (_config.sea_level - 0.15) / 3;
			elevation = Math.Tan(elevation * 1.15 - 1.2) / 1.20 + 0.9;
			double d = Math.Sqrt(x * x + y * y);
			if (elevationRamping.minRadius > 0 && d >= elevationRamping.minRadius)
			{
				double t = (d-elevationRamping.minRadius)*Math.Abs(elevationRamping.rampFactor) / elevationRamping.minRadius;
				t = Math.Clamp(t, 0, 1);
				elevation = double.Lerp(Math.Sign(elevationRamping.rampFactor) > 0 ? 1 : 0, Math.Sign(elevationRamping.rampFactor) > 0 ? 0 : 1, t);
			}

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
	}
}
