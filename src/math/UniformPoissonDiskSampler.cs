using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace OLearyMapGen.math
{
	/// <summary>
	/// Sourced from https://theinstructionlimit.com/fast-uniform-poisson-disk-sampling-in-c
	/// Which was itself...
	/// Adapted from java source by Herman Tulleken
	/// http://www.luma.co.za/labs/2008/02/27/poisson-disk-sampling/

	/// The algorithm is from the "Fast Poisson Disk Sampling in Arbitrary Dimensions" paper by Robert Bridson
	/// http://www.cs.ubc.ca/~rbridson/docs/bridson-siggraph07-poissondisk.pdf
	/// </summary>
	public static class UniformPoissonDiskSampler
	{
		public const int DefaultPointsPerIteration = 30;

		static readonly float SquareRootTwo = (float)Math.Sqrt(2);

		struct Settings
		{
			public Vector2 TopLeft, LowerRight, Center;
			public Vector2 Dimensions;
			public float? RejectionSqDistance;
			public float MinimumDistance;
			public float CellSize;
			public int GridWidth, GridHeight;
		}

		struct State
		{
			public Vector2?[,] Grid;
			public List<Vector2> ActivePoints, Points;
		}

		public static List<Vector2> SampleCircle(Vector2 center, float radius, float minimumDistance)
		{
			return SampleCircle(center, radius, minimumDistance, DefaultPointsPerIteration);
		}

		public static List<Vector2> SampleCircle(Vector2 center, float radius, float minimumDistance, int pointsPerIteration)
		{
			return Sample(center - new Vector2(radius), center + new Vector2(radius), radius, minimumDistance, pointsPerIteration);
		}

		public static List<Vector2> SampleRectangle(Vector2 topLeft, Vector2 lowerRight, float minimumDistance)
		{
			return SampleRectangle(topLeft, lowerRight, minimumDistance, DefaultPointsPerIteration);
		}

		public static List<Vector2> SampleRectangle(Vector2 topLeft, Vector2 lowerRight, float minimumDistance, int pointsPerIteration)
		{
			return Sample(topLeft, lowerRight, null, minimumDistance, pointsPerIteration);
		}

		static List<Vector2> Sample(Vector2 topLeft, Vector2 lowerRight, float? rejectionDistance, float minimumDistance, int pointsPerIteration)
		{
			var settings = new Settings
			{
				TopLeft = topLeft,
				LowerRight = lowerRight,
				Dimensions = lowerRight - topLeft,
				Center = (topLeft + lowerRight) / 2,
				CellSize = minimumDistance / SquareRootTwo,
				MinimumDistance = minimumDistance,
				RejectionSqDistance = rejectionDistance == null ? null : rejectionDistance * rejectionDistance
			};
			settings.GridWidth = (int)(settings.Dimensions.X / settings.CellSize) + 1;
			settings.GridHeight = (int)(settings.Dimensions.Y / settings.CellSize) + 1;

			var state = new State
			{
				Grid = new Vector2?[settings.GridWidth, settings.GridHeight],
				ActivePoints = new List<Vector2>(),
				Points = new List<Vector2>()
			};

			AddFirstPoint(ref settings, ref state);

			while (state.ActivePoints.Count != 0)
			{
				var listIndex = RandomHelper.Next(state.ActivePoints.Count);

				var point = state.ActivePoints[listIndex];
				var found = false;

				for (var k = 0; k < pointsPerIteration; k++)
					found |= AddNextPoint(point, ref settings, ref state);

				if (!found)
					state.ActivePoints.RemoveAt(listIndex);
			}

			return state.Points;
		}

		static void AddFirstPoint(ref Settings settings, ref State state)
		{
			var added = false;
			while (!added)
			{
				var d = RandomHelper.NextDouble();
				var xr = settings.TopLeft.X + settings.Dimensions.X * d;

				d = RandomHelper.NextDouble();
				var yr = settings.TopLeft.Y + settings.Dimensions.Y * d;

				var p = new Vector2((float)xr, (float)yr);
				if (settings.RejectionSqDistance != null && Vector2.DistanceSquared(settings.Center, p) > settings.RejectionSqDistance)
					continue;
				added = true;

				var index = Denormalize(p, settings.TopLeft, settings.CellSize);

				state.Grid[(int)index.X, (int)index.Y] = p;

				state.ActivePoints.Add(p);
				state.Points.Add(p);
			}
		}

		static bool AddNextPoint(Vector2 point, ref Settings settings, ref State state)
		{
			var found = false;
			var q = GenerateRandomAround(point, settings.MinimumDistance);

			if (q.X >= settings.TopLeft.X && q.X < settings.LowerRight.X &&
			    q.Y > settings.TopLeft.Y && q.Y < settings.LowerRight.Y &&
			    (settings.RejectionSqDistance == null || Vector2.DistanceSquared(settings.Center, q) <= settings.RejectionSqDistance))
			{
				var qIndex = Denormalize(q, settings.TopLeft, settings.CellSize);
				var tooClose = false;

				for (var i = (int)Math.Max(0, qIndex.X - 2); i < Math.Min(settings.GridWidth, qIndex.X + 3) && !tooClose; i++)
				for (var j = (int)Math.Max(0, qIndex.Y - 2); j < Math.Min(settings.GridHeight, qIndex.Y + 3) && !tooClose; j++)
					if (state.Grid[i, j].HasValue && Vector2.Distance(state.Grid[i, j].Value, q) < settings.MinimumDistance)
						tooClose = true;

				if (!tooClose)
				{
					found = true;
					state.ActivePoints.Add(q);
					state.Points.Add(q);
					state.Grid[(int)qIndex.X, (int)qIndex.Y] = q;
				}
			}

			return found;
		}

		static Vector2 GenerateRandomAround(Vector2 center, float minimumDistance)
		{
			var d = RandomHelper.NextDouble();
			var radius = minimumDistance + minimumDistance * d;

			d = RandomHelper.NextDouble();
			var angle = MathHelper.TwoPi * d;

			var newX = radius * Math.Sin(angle);
			var newY = radius * Math.Cos(angle);

			return new Vector2((float)(center.X + newX), (float)(center.Y + newY));
		}

		static Vector2 Denormalize(Vector2 point, Vector2 origin, double cellSize)
		{
			return new Vector2((int)((point.X - origin.X) / cellSize), (int)((point.Y - origin.Y) / cellSize));
		}

		public static void SetSeed(uint seed)
		{
			RandomHelper.SetSeed(seed);
			RandomHelper.SetPosition(0);
		}
	}

	internal static class RandomHelper
	{
		private static uint positionX = 0;
		private static uint SEED = 137;

		const uint SQ5_BIT_NOISE1 = 0xd2a80a3f; // 11010010101010000000101000111111
		const uint SQ5_BIT_NOISE2 = 0xa884f197; // 10101000100001001111000110010111
		const uint SQ5_BIT_NOISE3 = 0x6C736F4B; // 01101100011100110110111101001011
		const uint SQ5_BIT_NOISE4 = 0xB79F3ABB; // 10110111100111110011101010111011
		const uint SQ5_BIT_NOISE5 = 0x1b56c4f5; // 00011011010101101100010011110101

		public static void SetPosition(uint p)
		{
			positionX = p;
		}

		public static void SetSeed(uint seed)
		{
			SEED = seed;
		}

		public static uint SquirrelNoise5()
		{
			uint mangledBits = positionX++;
			mangledBits *= SQ5_BIT_NOISE1;
			mangledBits += SEED;
			mangledBits ^= (mangledBits >> 9);
			mangledBits += SQ5_BIT_NOISE2;
			mangledBits ^= (mangledBits >> 11);
			mangledBits *= SQ5_BIT_NOISE3;
			mangledBits ^= (mangledBits >> 13);
			mangledBits += SQ5_BIT_NOISE4;
			mangledBits ^= (mangledBits >> 15);
			mangledBits *= SQ5_BIT_NOISE5;
			mangledBits ^= (mangledBits >> 17);
			return mangledBits;
		}

		public static double NextDouble()
		{
			const double ONE_OVER_MAX_UINT = (1.0 / 0xFFFFFFFF);
			return ONE_OVER_MAX_UINT * SquirrelNoise5();
		}

		public static int Next(int max)
		{
			double d = NextDouble();
			return (int)Math.Floor(d * max);
		}
	}

	public static class MathHelper
	{
		public const float Pi = (float)Math.PI;
		public const float HalfPi = (float)(Math.PI / 2);
		public const float TwoPi = (float)(Math.PI * 2);
	}
}
