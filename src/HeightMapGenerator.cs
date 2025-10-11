using OLearyMapGen.math;
using SharpVoronoiLib;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace OLearyMapGen
{
	public static class HeightMapGenerator
	{

		public static Bitmap HeightMapRenderer(MapChunk chunk, Extents2d extents, Func<BiomeDef, double, Color> getColor, Func<BiomeDef, double, bool> isBodyOfWater, bool doHillShading = true)
		{
			List<VoronoiPoint> verts = chunk.heightMap.GetVertexMap().Vertices;
			
			VoronoiPlane plane = new VoronoiPlane(0, 0, extents.Width, extents.Height);
			plane.SetSites(verts.Select(p => new VoronoiSite(p.X, p.Y)).ToList());
			plane.Tessellate();

			float[,] heightData = GenerateHeightMap(chunk.heightMap, (int)Math.Round(extents.Width) + 4, (int)Math.Round(extents.Height) + 4, true, true, 2);
			float[,] erosionData = GenerateHeightMap(chunk.erosionFillMap, (int)Math.Round(extents.Width), (int)Math.Round(extents.Height), true, false);
			float[,] biomeData = GenerateHeightMap(chunk.biomeMap, (int)Math.Round(extents.Width), (int)Math.Round(extents.Height), false, false);
			float[,] cityData = GenerateHeightMap(chunk.cityPlacementScores, (int)Math.Round(extents.Width), (int)Math.Round(extents.Height), false, false);
			float[,] waterData = GenerateHeightMap(chunk.waterMap, (int)Math.Round(extents.Width), (int)Math.Round(extents.Height), true, false);
			float[,] tempData = GenerateHeightMap(chunk.tempMap, (int)Math.Round(extents.Width), (int)Math.Round(extents.Height), true, false);

			var vv = verts.Where(v => v.X > 255 || v.Y > 255).Select(v => new Vector2((float)v.X, (float)v.Y));
			var rv = chunk.riverVertices.Select(v => v < 0 ? new Vector2(-1, -1) : new Vector2((float)verts[v].X, (float)verts[v].Y)).Where(v => v.X > 255 || v.Y > 255);

			IEnumerable<Vector2> rivs = chunk.riverVertices.Select(v => v < 0 ? new Vector2(-1,-1) : new Vector2((float)verts[v].X, (float)verts[v].Y));

			return RenderImage(chunk.genParams, heightData, erosionData, waterData, biomeData, new Point(chunk.position.X * (int)extents.Width, chunk.position.Y * (int)extents.Height), rivs, getColor, isBodyOfWater);
		}

		private static Bitmap RenderImage(GenParams conf, float[,] heightData, float[,] erosionData, float[,] waterData, float[,] biomeData, Point chunkOffset,
			IEnumerable<Vector2> rivers, Func<BiomeDef, double, Color> getColor, Func<BiomeDef, double, bool> isBodyOfWater, bool drawRivers = true, bool doHillShading = true, bool useRawHeight=false)
		{
			float[,] shading = doHillShading ? ComputeHillshade(heightData) : new float[0,0];
			int shadeBuffer = (heightData.GetLength(0) - erosionData.GetLength(0)) / 2;
			int width = biomeData.GetLength(0);
			int height = biomeData.GetLength(1);
			Bitmap result = new Bitmap(width, height);
			HashSet<Point> skipPixels = new HashSet<Point>();
			for (int x = 0; x < width; x++)
			{
				for (int y = 0; y < height; y++)
				{
					BiomeDef biome = (BiomeDef)(int)Math.Round(biomeData[x, y]);
					float shade = 1;

					//Math.Clamp(shading[x, y] + 0.5f, 0 , 1);

					double w = waterData[x, y];
					double d = erosionData[x, y] * w * 0.85;
					Color c = getColor(biome, d);

					if (isBodyOfWater(biome, d))
					{
						shade = 1;
						skipPixels.Add(new Point(x, y));
					}
					else if(doHillShading)
					{
						if (x > 0 && y > 0)
						{
							shade += Math.Clamp(shading[x - 1 + shadeBuffer, y + shadeBuffer] + 0.5f, 0, 1);
							shade += Math.Clamp(shading[x - 1 + shadeBuffer, y - 1 + shadeBuffer] + 0.5f, 0, 1);
							shade += Math.Clamp(shading[x + shadeBuffer, y - 1 + shadeBuffer] + 0.5f, 0, 1);
							shade /= 4;
						}
						shade = Math.Clamp(shading[x + shadeBuffer, y + shadeBuffer] + 0.5f, 0, 1);
					}

					c = Color.FromArgb((int)Math.Round(c.R * shade), (int)Math.Round(c.G * shade), (int)Math.Round(c.B * shade));
					double h = Math.Clamp(heightData[x + shadeBuffer, y + shadeBuffer], 0, 1);
					if (!double.IsFinite(h) || double.IsNaN(h))
						h = 0;
					if(useRawHeight)
						c = Color.FromArgb((int)Math.Floor(h * 255), (int)Math.Floor(h * 255), (int)Math.Floor(h * 255));
					result.SetPixel(x, y, c);
				}
			}
			if(drawRivers)
				DrawRivers(conf, result, chunkOffset, skipPixels, rivers, getColor(BiomeDef.River, 0));

			return result;
		}

		private static void DrawRivers(GenParams conf, Bitmap result, Point chunkOffset, HashSet<Point> skipPixels, IEnumerable<Vector2> rivers, Color color)
		{
			Vector2 last1 = new Vector2(-1, -1);
			Vector2 last2 = new Vector2(-1, -1);
			Vector2 last3 = new Vector2(-1, -1);
			List<Vector2> lRivers = rivers.ToList();
			Dictionary<Point,double> points = new Dictionary<Point, double>();
			const double hardness = 0.1;
			double first = 0;

			foreach (Vector2 v in lRivers)
			{
				if(v.X < 0 || v.Y < 0 || last1.X < 0 || last1.Y < 0 || last2.X < 0 || last2.Y < 0)
				{
					if ((v.X < 0 || v.Y < 0) && last1.X >= 0 && last1.Y >= 0)
						DrawLine(result, last3, last2, last1, v, color, points, 4, hardness);

					last3 = last2;
					last2 = last1;
					last1 = v;
					first = 0;
					continue;
				}

				double c = conf.resolution / 8;
				DrawLine(result, last3, last2, last1, v, color, points, first < 2 * c ? 2 : first < 6 * c ? 2.5 : first < 16 * c ? 3 : first < 64 * c ? 4 : 5, hardness);
				first += Vector2.Distance(last2, last1);
				last3 = last2;
				last2 = last1;
				last1 = v;
			}

			foreach (KeyValuePair<Point, double> p in points)
			{
				if(p.Key.X < 0 || p.Key.Y < 0 || p.Key.X >= result.Width || p.Key.Y >= result.Height)
					continue;
				if(skipPixels.Contains(p.Key))
					continue;
				Color c = result.GetPixel(p.Key.X, p.Key.Y);
				int r = (int)Math.Round(Lerp(color.R, c.R, p.Value));
				int g = (int)Math.Round(Lerp(color.G, c.G, p.Value));
				int b = (int)Math.Round(Lerp(color.B, c.B, p.Value));
				result.SetPixel(p.Key.X, p.Key.Y, Color.FromArgb(r, g, b));
			}
		}

		private static void DrawLine(Bitmap result, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Color color, Dictionary<Point, double> drawn, double thickness=0, double hardness=1)
		{
			const int scalar = 3;
			const double smoothingFactor = 0.5;
			double nHardness =  Math.Clamp(hardness, double.Epsilon, 1);
			var ln = Drawing.GetPointsOnLine(p1, p2, scalar);
			Point[] line = ln.ToArray();
			line = Relax(line, smoothingFactor);
			HashSet<Point> points = Drawing.DilateLine(line, thickness);
			IEnumerable<Vector2> nPoints = points.Select(p => new Vector2(p.X, p.Y));

			foreach (Vector2 v in nPoints)
			{
				int x = (int)v.X;
				int y = (int)v.Y;
				if(x < 0 || y < 0)
					continue;
				double dist = ShortestDistanceToSegments(v, p0, p1, p2, p3, 0) / (thickness / 2.0);

				dist = Math.Pow(dist, 1.0 / nHardness);

				Point pixel = new Point(x, y);
				if(!drawn.TryAdd(pixel, Math.Clamp(dist, 0, 1)))
					drawn[pixel] = Math.Min(drawn[pixel], Math.Clamp(dist, 0, 1));
			}
		}

		private static Point[] Relax(Point[] path, double factor)
		{
			Point[] smoothedPath = new Point[path.Length];
			for (int i = 1; i < path.Length - 1; i++)
			{
				Point v0 = path[i - 1];
				Point v1 = path[i];
				Point v2 = path[i + 1];

				v1.X = (int)Math.Round((1 - factor) * v1.X + factor * 0.5 * (v0.X + v2.X));
				v1.Y = (int)Math.Round((1 - factor) * v1.Y + factor * 0.5 * (v0.Y + v2.Y));

				smoothedPath[i] = v1;
			}

			return smoothedPath;
		}

		public static double ShortestDistanceToSegments(Vector2 v, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, double toleranceFraction)
		{
			double distSq0 = DistanceSquaredToSegment(v, p0, p1, toleranceFraction);
			double distSq1 = DistanceSquaredToSegment(v, p1, p2, toleranceFraction);
			double distSq2 = DistanceSquaredToSegment(v, p2, p3, toleranceFraction);

			if (p0.X < 0 || p0.Y < 0)
				distSq0 = distSq1;
			if (p3.X < 0 || p3.Y < 0)
				distSq2 = double.MaxValue;

			double minDistanceSq = Math.Min(distSq0, Math.Min(distSq1, distSq2));
			return Math.Sqrt(minDistanceSq);
		}

		private static float DistanceSquaredToSegment(Vector2 v, Vector2 p1, Vector2 p2, double toleranceFraction)
		{
			// 1. Define vectors
			Vector2 segmentVector = p2 - p1; // P2 - P1 (direction of the segment)
			Vector2 pointVector = v - p1;    // V - P1 (vector from P1 to V)

			double segmentLengthSq = Vector2.DistanceSquared(segmentVector, Vector2.Zero);

			// Handle the case where P1 and P2 are the same point (degenerate segment)
			if (segmentLengthSq == 0.0f)
			{
				return Vector2.DistanceSquared(pointVector, Vector2.Zero); // Distance is just V to P1
			}

			// 2. Calculate the projection parameter 't'
			// t = (V - P1) . (P2 - P1) / |P2 - P1|^2
			double t = Vector2.Dot(pointVector, segmentVector) / segmentLengthSq;

			// 3. Define the extended clamping range
			double t_min = -toleranceFraction;
			double t_max = 1.0f + toleranceFraction;

			// 3. Clamp t to the range [t_min, t_max] to ensure the closest point lies on the segment
			if (t < t_min) t = t_min;
			else if (t > t_max) t = t_max;

			Vector2 closestPoint = new Vector2(
				(float)(p1.X + t * segmentVector.X),
				(float)(p1.Y + t * segmentVector.Y)
			);

			// 4. Calculate the squared distance between V and the closest point
			Vector2 difference = v - closestPoint;
			return Vector2.DistanceSquared(difference, Vector2.Zero);
		}

		private static double cross(Vector2 a, Vector2 ab, Vector2 c)
		{
			return ab.X * (c.Y - a.Y) - ab.Y * (c.X - a.X);
		}

		private static double Lerp(double start, double end, double t)
		{
			return (end - start) * t + start;
		}

		/// <summary>
		/// Represents a triangle formed by three Voronoi Vertices.
		/// This is the result of the Delaunay Triangulation step.
		/// </summary>
		private class Triangle
		{
			// Indices corresponding to the global list of VoronoiPoints
			public int IndexA;
			public int IndexB;
			public int IndexC;

			public Triangle(int a, int b, int c)
			{
				IndexA = a;
				IndexB = b;
				IndexC = c;
			}
		}

		/// <summary>
		/// Finds the triangle containing the query point P by checking only the triangles 
		/// incident to the nearest site A, and returns the constructed Triangle object 
		/// along with the necessary interpolation data.
		/// </summary>
		/// <param name="p">The world coordinate query point.</param>
		/// <returns>A tuple containing the found Triangle and the heights (HA, HB, HC), 
		/// or null if no containing triangle is found.</returns>
		private static Triangle FindContainingTriangle(VertexMap vertexMap, VoronoiPlane plane, Vector2 p)
		{
			// 1. Find the nearest site (A)
			VoronoiSite siteA = plane.GetNearestSiteTo(p.X, p.Y);
			int n = 0;
			VoronoiSite[] neighbors = siteA.Neighbours.OrderBy(a => Vector2.DistanceSquared(p, new Vector2((float)a.X, (float)a.Y))).ToArray();
			Triangle r = null;
			VoronoiSite next = siteA;
			do
			{
				r = FindContainingTriangle(vertexMap, plane, p, next);
				if (n >= neighbors.Length)
					break;
				next = neighbors[n++];
			} while(r == null);

			// If no triangle incident to A contains P, P is likely outside the convex hull.
			return r;
		}

		private static Triangle FindContainingTriangle(VertexMap vertexMap, VoronoiPlane plane, Vector2 p, VoronoiSite siteA)
		{
			if (p.X == 2 && p.Y == 4)
				;
			VoronoiSite[] neighbors = siteA.Neighbours.ToArray();
			for (var i = 0; i < neighbors.Length; i++)
			{
				VoronoiSite siteB = neighbors[i];
				// Start j at i + 1 to ensure unique pairs and avoid checking the same triangle twice
				for (var j = 0; j < neighbors.Length; j++)
				{
					if(i == j) continue;
					VoronoiSite siteC = neighbors[j];
					if (!(siteC.Neighbours.Contains(siteB) || siteB.Neighbours.Contains(siteC)))
						continue;
					// Check if P is inside the candidate triangle (A, B, C)
					if (!IsInside(p, new Vector2((float)siteA.X, (float)siteA.Y), new Vector2((float)siteB.X, (float)siteB.Y), new Vector2((float)siteC.X, (float)siteC.Y))) continue;
					// Found the containing triangle!
					int siteA_idx = vertexMap.Vertices.FindIndex(vp => VoronoiExtentions.GetHashCode(vp) == VoronoiExtentions.GetHashCode(siteA) && Math.Abs(vp.X - siteA.X) < 0.01);
					int siteB_idx = vertexMap.Vertices.FindIndex(vp => VoronoiExtentions.GetHashCode(vp) == VoronoiExtentions.GetHashCode(siteB) && Math.Abs(vp.X - siteB.X) < 0.01);
					int siteC_idx = vertexMap.Vertices.FindIndex(vp => VoronoiExtentions.GetHashCode(vp) == VoronoiExtentions.GetHashCode(siteC) && Math.Abs(vp.X - siteC.X) < 0.01);

					// We must ensure the indices are ordered consistently for the Triangle object
					// (though the order A, B, C doesn't matter for barycentric interpolation,
					// it matters for consistent indexing in the Triangle class).
					Triangle foundTriangle = new Triangle(siteA_idx, siteB_idx, siteC_idx);

					return foundTriangle;
				}
			}

			if (p.X == 2 && p.Y == 4)
			{
				VoronoiSite nearestToMid1 = plane.GetNearestSiteTo( 1,-8);
				VoronoiSite nearestToMid2 = plane.GetNearestSiteTo( 0,-8);
				VoronoiSite nearestToMid3 = plane.GetNearestSiteTo(-1,-8);
				;
			}

			return null;
		}

		/// <summary>
		/// Calculates the signed area of the triangle defined by P1, P2, P3.
		/// Used for barycentric coordinate calculation.
		/// </summary>
		private static double SignedArea(Vector2 p1, Vector2 p2, Vector2 p3)
		{
			return (p1.X * (p2.Y - p3.Y) + p2.X * (p3.Y - p1.Y) + p3.X * (p1.Y - p2.Y));
		}

		/// <summary>
		/// Checks if point P is inside the triangle defined by A, B, C.
		/// Uses the property that P must lie on the same side of all three edges.
		/// </summary>
		private static bool IsInside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
		{
			if (p.X == 2 && p.Y == 4)
				;
			double areaABC = SignedArea(a, b, c);

			// Handle degenerate triangles (shouldn't happen with good DT, but good practice)
			if (Math.Abs(areaABC) < double.Epsilon) return false;

			// Calculate barycentric coordinates (lambda_A, lambda_B, lambda_C)
			// We only need to check if they are all non-negative.
			double lambdaA = SignedArea(p, b, c);
			double lambdaB = SignedArea(a, p, c);
			double lambdaC = areaABC - lambdaA - lambdaB;

			// Due to floating point precision, check slightly outside [0, 1]
			const double epsilon = 1e-6f;
			return (lambdaA >= -epsilon) && (lambdaB >= -epsilon) && (lambdaC >= -epsilon);
		}

		/// <summary>
		/// Calculates the barycentric coordinates (lambdaA, lambdaB, lambdaC) for point P 
		/// relative to triangle A, B, C.
		/// </summary>
		private static (double lambdaA, double lambdaB, double lambdaC) CalculateBarycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
		{
			double areaABC = SignedArea(a, b, c);

			if (Math.Abs(areaABC) < float.Epsilon)
			{
				// Degenerate triangle, return zero weights
				return (0, 0, 0);
			}

			double lambdaA = SignedArea(p, b, c) / areaABC;
			double lambdaB = SignedArea(a, p, c) / areaABC;
			// Optimization: lambdaC = 1 - lambdaA - lambdaB
			double lambdaC = 1.0 - lambdaA - lambdaB;

			return (lambdaA, lambdaB, lambdaC);
		}



		/// <summary>
		/// Generates the height map texture array using barycentric interpolation.
		/// </summary>
		/// <returns>A 1D float array representing the height map texture (row-major order).</returns>
		private static float[,] GenerateHeightMap<T>(NodeMap<T> heightMap, int width, int height, bool blend, bool addNoise, int buffer=0) where T : INumber<T>
		{
			var vertexMap = heightMap.GetVertexMap();
			List<VoronoiPoint> verts = vertexMap.Vertices;
			Dictionary<int, double> vertexHeightMap = verts.ToDictionary(v => heightMap.GetNodeIndex(v), v => Convert.ToDouble(heightMap.Get(heightMap.GetNodeIndex(v))));
			VoronoiPlane plane = new VoronoiPlane(vertexMap.MinX, vertexMap.MinY, vertexMap.Width, vertexMap.Height);
			plane.SetSites(vertexMap.Vertices.Select(p => new VoronoiSite(p.X, p.Y)).ToList());
			plane.Tessellate();
			float[,] heightMapData = new float[width, height];

			for (int j = 0; j < height; j++) // Row (Y)
			{
				for (int i = 0; i < width; i++) // Column (X)
				{
					if (i- buffer == 2 && j- buffer == 4)
					{
						;
					}

					// 1. Find the containing triangle
					Triangle containingTriangle = FindContainingTriangle(vertexMap, plane, new Vector2(i - buffer, j - buffer));

					double heightValue;

					if (containingTriangle == null)
					{
						// If the point is outside the triangulation area, assign a default value (e.g., 0)
						// or use the height of the nearest vertex (more complex).
						VoronoiSite near = plane.GetNearestSiteTo(i - buffer, j - buffer);
						int idx = vertexMap.Vertices.FindIndex(vp => VoronoiExtentions.GetHashCode(vp) == VoronoiExtentions.GetHashCode(near));
						heightValue = vertexHeightMap[idx];
						heightMapData[i, j] = (float)heightValue;
						continue;
					}

					// 2. Get the vertices and heights of the containing triangle
					VoronoiPoint A = vertexMap.Vertices[containingTriangle.IndexA];
					VoronoiPoint B = vertexMap.Vertices[containingTriangle.IndexB];
					VoronoiPoint C = vertexMap.Vertices[containingTriangle.IndexC];

					double HA = vertexHeightMap[containingTriangle.IndexA];
					double HB = vertexHeightMap[containingTriangle.IndexB];
					double HC = vertexHeightMap[containingTriangle.IndexC];

					// 3. Calculate barycentric coordinates
					var (lambdaA, lambdaB, lambdaC) = CalculateBarycentric(
						new Vector2(i - buffer, j - buffer),
						new Vector2((float)A.X, (float)A.Y),
						new Vector2((float)B.X, (float)B.Y),
						new Vector2((float)C.X, (float)C.Y));

					// 4. Interpolate the height smoothly
					if (blend)
						heightValue = (lambdaA * HA) + (lambdaB * HB) + (lambdaC * HC);
					else
						heightValue = HA;

					heightMapData[i, j] = (float)heightValue;
					/*VoronoiSite n = plane.GetNearestSiteTo(i,j);
					heightMapData[i, j] = (float)vertexHeightMap[vertexMap.Vertices.FindIndex(v => VoronoiExtentions.GetHashCode(v) == VoronoiExtentions.GetHashCode(n))];*/
				}
			}
			if (addNoise)
				for (int j = 0; j < height; j++) // Row (Y)
				{
					for (int i = 0; i < width; i++) // Column (X)
					{
						double heightValue = heightMapData[i, j];
						double r = RandomHelper.NextDouble();
						if (r > 0.990 && i > 0)
							heightValue = heightMapData[i - 1, j];
						else if (r > 0.980 && j > 0)
							heightValue = heightMapData[i, j - 1];
						else if (r > 0.970 && i > 0 && j > 0)
							heightValue = heightMapData[i - 1, j - 1];
						else if (r > 0.960 && i < width-1)
							heightValue = heightMapData[i + 1, j];
						else if (r > 0.950 && j < height-1)
							heightValue = heightMapData[i, j + 1];
						else if (r > 0.940 && i < width-1 && j < height-1)
							heightValue = heightMapData[i + 1, j + 1];
						else if (r > 0.920 && i > 0)
							heightValue = (heightValue + heightMapData[i - 1, j]) / 2;
						else if (r > 0.900 && j > 0)
							heightValue = (heightValue + heightMapData[i, j - 1]) / 2;
						else if (r > 0.880 && i > 0 && j > 0)
							heightValue = (heightValue + heightMapData[i - 1, j - 1]) / 2;
						else if (r > 0.860 && i < width-1)
							heightValue = (heightValue + heightMapData[i + 1, j]) / 2;
						else if (r > 0.840 && j < height-1)
							heightValue = (heightValue + heightMapData[i, j + 1]) / 2;
						else if (r > 0.820 && i < width-1 && j < height-1)
							heightValue = (heightValue + heightMapData[i + 1, j + 1]) / 2;
						else if (r > 0.815)
						{
							if (i > 0 && j > 0 && heightValue > heightMapData[i - 1, j] && heightValue > heightMapData[i, j - 1])
								heightValue *= 1.03;
						}
						heightMapData[i, j] = (float)heightValue;
					}
				}

			return heightMapData;
		}

		private static float[,] ComputeHillshade(float[,] heightMap, double azimuth = 315, double altitude = 30, double zFactor = 100.0)
		{
			int width = heightMap.GetLength(0);
			int height = heightMap.GetLength(1);

			// Convert angles to radians
			double azimuthRad = (360.0 - azimuth) * Math.PI / 180.0;
			double altitudeRad = altitude * Math.PI / 180.0;

			// Pre-calculate trigonometric values for the light source
			double cosAltitude = Math.Cos(altitudeRad);
			double sinAltitude = Math.Sin(altitudeRad);

			// Resulting hillshade array
			float[,] hillshade = new float[width, height];

			Parallel.For(0, width, xx =>
			{
				for (int yy = 0; yy < height; yy++)
				{
					int x = Math.Clamp(xx, 1, width - 2);
					int y = Math.Clamp(yy, 1, height - 2);
					// Calculate partial derivatives using the neighborhood of 4 pixels
					double dzdx = (heightMap[x + 1, y] - heightMap[x - 1, y]) / 2.0;
					double dzdy = (heightMap[x, y + 1] - heightMap[x, y - 1]) / 2.0;

					//dzdx = Math.Sign(dzdx) * Math.Pow(Math.Abs(dzdx), 1.35);
					//dzdy = Math.Sign(dzdy) * Math.Pow(Math.Abs(dzdy), 1.35);

					// Apply Z-factor
					dzdx *= zFactor;
					dzdy *= zFactor;

					// Calculate slope and aspect
					double slopeRad = Math.Atan(Math.Sqrt(dzdx * dzdx + dzdy * dzdy));
					double aspectRad = Math.Atan2(dzdy, -dzdx);

					// Calculate the hillshade value using the formula
					double hillshadeVal = (
						cosAltitude * Math.Cos(slopeRad) +
						sinAltitude * Math.Sin(slopeRad) * Math.Cos(azimuthRad - aspectRad)
					);

					hillshade[xx, yy] = (float)hillshadeVal;
				}
			});

			return hillshade;
		}

	}
}
